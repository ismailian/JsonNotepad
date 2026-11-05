using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Paths;

namespace JsonNotepad.Core.Validation;

/// <summary>
/// A dependency-free JSON Schema validator covering the vocabulary used by the vast
/// majority of real-world schemas (drafts 4 through 2020-12):
/// type, enum, const, properties, required, additionalProperties, patternProperties,
/// propertyNames, min/maxProperties, dependentRequired, items / prefixItems (and the
/// draft-4..7 array form of items), additionalItems, contains, min/maxItems, uniqueItems,
/// minimum/maximum, exclusiveMinimum/exclusiveMaximum (bool and numeric forms), multipleOf,
/// min/maxLength, pattern, format (date-time, date, time, email, uri, uuid, ipv4, ipv6),
/// allOf/anyOf/oneOf/not, if/then/else, and local $ref (JSON Pointer within the schema,
/// including $defs/definitions). Remote $refs are reported, never fetched.
/// </summary>
/// <remarks>
/// Works directly on <see cref="JNode"/> trees for both the schema and the instance, and is
/// designed to run on a background thread against a snapshot.
/// </remarks>
public sealed class JsonSchemaValidator
{
    private const int MaxRefDepth = 128;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly JNode _schemaRoot;
    private readonly ConcurrentDictionary<string, Regex?> _regexCache = new(StringComparer.Ordinal);

    public JsonSchemaValidator(JNode schemaRoot)
    {
        _schemaRoot = schemaRoot ?? throw new ArgumentNullException(nameof(schemaRoot));
        if (schemaRoot.Kind is not (JNodeKind.Object or JNodeKind.Boolean))
        {
            throw new ArgumentException("A JSON Schema must be an object or a boolean.", nameof(schemaRoot));
        }
    }

    public int MaxErrors { get; init; } = 1000;

    public string? SchemaFileName { get; init; }

    public IReadOnlyList<Diagnostic> Validate(JNode instance, CancellationToken cancellationToken = default)
    {
        var ctx = new Context(this, cancellationToken, new List<Diagnostic>());
        var path = new PathBuilder();
        ValidateNode(instance, _schemaRoot, path, ctx, 0);
        return ctx.Errors;
    }

    private bool ValidateNode(JNode node, JNode schema, PathBuilder path, Context ctx, int refDepth)
    {
        ctx.Token.ThrowIfCancellationRequested();
        if (ctx.Full)
        {
            return false;
        }

        if (schema.Kind == JNodeKind.Boolean)
        {
            if (!schema.BooleanValue)
            {
                return ctx.Fail(path, "No value is allowed here (schema is false).");
            }
            return true;
        }
        if (schema.Kind != JNodeKind.Object)
        {
            return true;
        }

        bool ok = true;

        if (schema.FindProperty("$ref") is { Kind: JNodeKind.String } refNode)
        {
            if (refDepth > MaxRefDepth)
            {
                return ctx.Fail(path, "Schema $ref nesting is too deep (possible cycle).");
            }
            var target = ResolveRef(refNode.Value!);
            if (target is null)
            {
                ctx.Fail(path, $"Cannot resolve $ref \"{refNode.Value}\" (only local references are supported).", DiagnosticSeverity.Warning);
            }
            else
            {
                ok &= ValidateNode(node, target, path, ctx, refDepth + 1);
            }
        }

        foreach (var keyword in schema.Children)
        {
            if (ctx.Full)
            {
                break;
            }
            ok &= keyword.Name switch
            {
                "type" => CheckType(node, keyword, path, ctx),
                "enum" => CheckEnum(node, keyword, path, ctx),
                "const" => JsonEquality.DeepEquals(node, keyword) || ctx.Fail(path, $"Value must be {Describe(keyword)}."),
                "allOf" => CheckAllOf(node, keyword, path, ctx, refDepth),
                "anyOf" => CheckAnyOf(node, keyword, path, ctx, refDepth),
                "oneOf" => CheckOneOf(node, keyword, path, ctx, refDepth),
                "not" => CheckNot(node, keyword, path, ctx, refDepth),
                "if" => CheckIfThenElse(node, schema, keyword, path, ctx, refDepth),
                _ => true,
            };
        }

        switch (node.Kind)
        {
            case JNodeKind.Object:
                ok &= ValidateObject(node, schema, path, ctx, refDepth);
                break;
            case JNodeKind.Array:
                ok &= ValidateArray(node, schema, path, ctx, refDepth);
                break;
            case JNodeKind.String:
                ok &= ValidateString(node, schema, path, ctx);
                break;
            case JNodeKind.Number:
                ok &= ValidateNumber(node, schema, path, ctx);
                break;
        }
        return ok;
    }

    // ----- Generic keywords ------------------------------------------------

    private static bool CheckType(JNode node, JNode keyword, PathBuilder path, Context ctx)
    {
        bool Matches(string? t) => t switch
        {
            "object" => node.Kind == JNodeKind.Object,
            "array" => node.Kind == JNodeKind.Array,
            "string" => node.Kind == JNodeKind.String,
            "number" => node.Kind == JNodeKind.Number,
            "integer" => node.Kind == JNodeKind.Number && IsInteger(node.Value),
            "boolean" => node.Kind == JNodeKind.Boolean,
            "null" => node.Kind == JNodeKind.Null,
            _ => true,
        };

        if (keyword.Kind == JNodeKind.String)
        {
            return Matches(keyword.Value) || ctx.Fail(path, $"Expected {keyword.Value} but found {DescribeKind(node)}.");
        }
        if (keyword.Kind == JNodeKind.Array)
        {
            var types = keyword.Children.Where(c => c.Kind == JNodeKind.String).Select(c => c.Value).ToList();
            return types.Any(Matches) || ctx.Fail(path, $"Expected one of [{string.Join(", ", types)}] but found {DescribeKind(node)}.");
        }
        return true;
    }

    private static bool CheckEnum(JNode node, JNode keyword, PathBuilder path, Context ctx)
    {
        if (keyword.Kind != JNodeKind.Array)
        {
            return true;
        }
        foreach (var option in keyword.Children)
        {
            if (JsonEquality.DeepEquals(node, option))
            {
                return true;
            }
        }
        var shown = string.Join(", ", keyword.Children.Take(10).Select(Describe));
        if (keyword.Count > 10)
        {
            shown += ", …";
        }
        return ctx.Fail(path, $"Value must be one of: {shown}.");
    }

    private bool CheckAllOf(JNode node, JNode keyword, PathBuilder path, Context ctx, int depth)
    {
        bool ok = true;
        foreach (var sub in keyword.Children)
        {
            ok &= ValidateNode(node, sub, path, ctx, depth + 1);
        }
        return ok;
    }

    private bool CheckAnyOf(JNode node, JNode keyword, PathBuilder path, Context ctx, int depth)
    {
        foreach (var sub in keyword.Children)
        {
            if (Probe(node, sub, path, ctx, depth))
            {
                return true;
            }
        }
        return keyword.Count == 0 || ctx.Fail(path, "Value does not match any of the allowed schemas (anyOf).");
    }

    private bool CheckOneOf(JNode node, JNode keyword, PathBuilder path, Context ctx, int depth)
    {
        int matches = 0;
        foreach (var sub in keyword.Children)
        {
            if (Probe(node, sub, path, ctx, depth) && ++matches > 1)
            {
                break;
            }
        }
        return matches switch
        {
            1 => true,
            0 => ctx.Fail(path, "Value does not match any of the schemas in oneOf."),
            _ => ctx.Fail(path, "Value matches more than one schema in oneOf."),
        };
    }

    private bool CheckNot(JNode node, JNode keyword, PathBuilder path, Context ctx, int depth) =>
        !Probe(node, keyword, path, ctx, depth) || ctx.Fail(path, "Value must not match the schema in \"not\".");

    private bool CheckIfThenElse(JNode node, JNode schema, JNode ifSchema, PathBuilder path, Context ctx, int depth)
    {
        bool condition = Probe(node, ifSchema, path, ctx, depth);
        var branch = schema.FindProperty(condition ? "then" : "else");
        return branch is null || ValidateNode(node, branch, path, ctx, depth + 1);
    }

    /// <summary>Validates without reporting (for anyOf/oneOf/not/if).</summary>
    private bool Probe(JNode node, JNode schema, PathBuilder path, Context ctx, int depth)
    {
        var probe = ctx.CreateProbe();
        return ValidateNode(node, schema, path, probe, depth + 1) && probe.Errors.Count == 0;
    }

    // ----- Objects -----------------------------------------------------------

    private bool ValidateObject(JNode node, JNode schema, PathBuilder path, Context ctx, int depth)
    {
        bool ok = true;
        var properties = schema.FindProperty("properties");
        var patternProps = schema.FindProperty("patternProperties");
        var additional = schema.FindProperty("additionalProperties");
        var propertyNames = schema.FindProperty("propertyNames");

        if (schema.FindProperty("required") is { Kind: JNodeKind.Array } required)
        {
            foreach (var r in required.Children)
            {
                if (r.Kind == JNodeKind.String && node.FindProperty(r.Value!) is null)
                {
                    ok = ctx.Fail(path, $"Missing required property \"{r.Value}\".");
                }
            }
        }

        if (GetInt(schema, "minProperties") is int minP && node.Count < minP)
        {
            ok = ctx.Fail(path, $"Object must have at least {minP} properties (has {node.Count}).");
        }
        if (GetInt(schema, "maxProperties") is int maxP && node.Count > maxP)
        {
            ok = ctx.Fail(path, $"Object must have at most {maxP} properties (has {node.Count}).");
        }

        if (schema.FindProperty("dependentRequired") is { Kind: JNodeKind.Object } depReq)
        {
            foreach (var dep in depReq.Children)
            {
                if (node.FindProperty(dep.Name!) is null || dep.Kind != JNodeKind.Array)
                {
                    continue;
                }
                foreach (var need in dep.Children)
                {
                    if (need.Kind == JNodeKind.String && node.FindProperty(need.Value!) is null)
                    {
                        ok = ctx.Fail(path, $"Property \"{need.Value}\" is required when \"{dep.Name}\" is present.");
                    }
                }
            }
        }

        foreach (var member in node.Children)
        {
            if (ctx.Full)
            {
                break;
            }
            var name = member.Name ?? string.Empty;
            path.PushName(name);
            bool matched = false;

            if (propertyNames is not null)
            {
                ok &= ValidateNode(JNode.CreateString(name), propertyNames, path, ctx, depth + 1);
            }

            if (properties?.FindProperty(name) is { } propSchema)
            {
                matched = true;
                ok &= ValidateNode(member, propSchema, path, ctx, depth + 1);
            }

            if (patternProps is { Kind: JNodeKind.Object })
            {
                foreach (var pp in patternProps.Children)
                {
                    if (GetRegex(pp.Name!) is { } rx && SafeIsMatch(rx, name))
                    {
                        matched = true;
                        ok &= ValidateNode(member, pp, path, ctx, depth + 1);
                    }
                }
            }

            if (!matched && additional is not null)
            {
                if (additional.Kind == JNodeKind.Boolean && !additional.BooleanValue)
                {
                    path.Pop();
                    ok = ctx.Fail(path, $"Property \"{name}\" is not allowed.");
                    continue;
                }
                ok &= ValidateNode(member, additional, path, ctx, depth + 1);
            }
            path.Pop();
        }
        return ok;
    }

    // ----- Arrays ------------------------------------------------------------

    private bool ValidateArray(JNode node, JNode schema, PathBuilder path, Context ctx, int depth)
    {
        bool ok = true;
        if (GetInt(schema, "minItems") is int min && node.Count < min)
        {
            ok = ctx.Fail(path, $"Array must have at least {min} items (has {node.Count}).");
        }
        if (GetInt(schema, "maxItems") is int max && node.Count > max)
        {
            ok = ctx.Fail(path, $"Array must have at most {max} items (has {node.Count}).");
        }

        var prefix = schema.FindProperty("prefixItems");
        var items = schema.FindProperty("items");
        var additionalItems = schema.FindProperty("additionalItems");
        int prefixCount = 0;

        if (prefix is { Kind: JNodeKind.Array })
        {
            prefixCount = prefix.Count;
        }
        else if (items is { Kind: JNodeKind.Array })
        {
            // Draft 4-2019: "items" as an array means tuple validation.
            prefix = items;
            prefixCount = items.Count;
            items = additionalItems;
        }

        for (int i = 0; i < node.Count && !ctx.Full; i++)
        {
            JNode? itemSchema = i < prefixCount ? prefix![i] : items;
            if (itemSchema is null)
            {
                continue;
            }
            path.PushIndex(i);
            if (itemSchema.Kind == JNodeKind.Boolean && !itemSchema.BooleanValue)
            {
                ok = ctx.Fail(path, "Additional items are not allowed here.");
            }
            else
            {
                ok &= ValidateNode(node[i], itemSchema, path, ctx, depth + 1);
            }
            path.Pop();
        }

        if (schema.FindProperty("contains") is { } contains)
        {
            int count = 0;
            foreach (var item in node.Children)
            {
                if (Probe(item, contains, path, ctx, depth))
                {
                    count++;
                }
            }
            int minC = GetInt(schema, "minContains") ?? 1;
            int? maxC = GetInt(schema, "maxContains");
            if (count < minC)
            {
                ok = ctx.Fail(path, minC == 1 ? "Array must contain at least one matching item (contains)." : $"Array must contain at least {minC} matching items.");
            }
            if (maxC is int mc && count > mc)
            {
                ok = ctx.Fail(path, $"Array must contain at most {mc} matching items.");
            }
        }

        if (schema.FindProperty("uniqueItems") is { Kind: JNodeKind.Boolean } unique && unique.BooleanValue)
        {
            var seen = new Dictionary<int, List<JNode>>();
            for (int i = 0; i < node.Count; i++)
            {
                var item = node[i];
                int hash = JsonEquality.GetHashCode(item);
                if (!seen.TryGetValue(hash, out var bucket))
                {
                    seen[hash] = bucket = new List<JNode>();
                }
                if (bucket.Any(b => JsonEquality.DeepEquals(b, item)))
                {
                    path.PushIndex(i);
                    ok = ctx.Fail(path, "Array items must be unique; this item is a duplicate.");
                    path.Pop();
                    break;
                }
                bucket.Add(item);
            }
        }
        return ok;
    }

    // ----- Strings -----------------------------------------------------------

    private bool ValidateString(JNode node, JNode schema, PathBuilder path, Context ctx)
    {
        bool ok = true;
        var s = node.Value ?? string.Empty;
        int? min = GetInt(schema, "minLength");
        int? max = GetInt(schema, "maxLength");
        if (min is not null || max is not null)
        {
            int len = CountCodePoints(s);
            if (min is int mn && len < mn)
            {
                ok = ctx.Fail(path, $"String must be at least {mn} characters long (is {len}).");
            }
            if (max is int mx && len > mx)
            {
                ok = ctx.Fail(path, $"String must be at most {mx} characters long (is {len}).");
            }
        }
        if (schema.FindProperty("pattern") is { Kind: JNodeKind.String } pattern)
        {
            var rx = GetRegex(pattern.Value!);
            if (rx is null)
            {
                ctx.Fail(path, $"Schema pattern \"{pattern.Value}\" is not a valid regular expression.", DiagnosticSeverity.Warning);
            }
            else if (!SafeIsMatch(rx, s))
            {
                ok = ctx.Fail(path, $"String does not match the pattern \"{pattern.Value}\".");
            }
        }
        if (schema.FindProperty("format") is { Kind: JNodeKind.String } format && !FormatChecker.IsValid(format.Value!, s))
        {
            ok = ctx.Fail(path, $"String is not a valid \"{format.Value}\".");
        }
        return ok;
    }

    // ----- Numbers ------------------------------------------------------------

    private static bool ValidateNumber(JNode node, JNode schema, PathBuilder path, Context ctx)
    {
        if (!JsonNumber.TryGetDouble(node.Value, out var value))
        {
            return true;
        }
        bool ok = true;
        var exMinNode = schema.FindProperty("exclusiveMinimum");
        var exMaxNode = schema.FindProperty("exclusiveMaximum");
        bool draft4ExMin = exMinNode is { Kind: JNodeKind.Boolean } && exMinNode.BooleanValue;
        bool draft4ExMax = exMaxNode is { Kind: JNodeKind.Boolean } && exMaxNode.BooleanValue;

        if (GetDouble(schema, "minimum") is double min)
        {
            if (draft4ExMin ? value <= min : value < min)
            {
                ok = ctx.Fail(path, draft4ExMin ? $"Value must be greater than {Fmt(min)}." : $"Value must be at least {Fmt(min)}.");
            }
        }
        if (GetDouble(schema, "maximum") is double max)
        {
            if (draft4ExMax ? value >= max : value > max)
            {
                ok = ctx.Fail(path, draft4ExMax ? $"Value must be less than {Fmt(max)}." : $"Value must be at most {Fmt(max)}.");
            }
        }
        if (exMinNode is { Kind: JNodeKind.Number } && GetDouble(schema, "exclusiveMinimum") is double exMin && value <= exMin)
        {
            ok = ctx.Fail(path, $"Value must be greater than {Fmt(exMin)}.");
        }
        if (exMaxNode is { Kind: JNodeKind.Number } && GetDouble(schema, "exclusiveMaximum") is double exMax && value >= exMax)
        {
            ok = ctx.Fail(path, $"Value must be less than {Fmt(exMax)}.");
        }
        if (schema.FindProperty("multipleOf") is { Kind: JNodeKind.Number } multipleOf && !IsMultipleOf(node.Value!, multipleOf.Value!))
        {
            ok = ctx.Fail(path, $"Value must be a multiple of {multipleOf.Value}.");
        }
        return ok;
    }

    private static bool IsMultipleOf(string value, string divisor)
    {
        if (JsonNumber.TryGetDecimal(value, out var v) && JsonNumber.TryGetDecimal(divisor, out var d) && d != 0)
        {
            return v % d == 0;
        }
        if (JsonNumber.TryGetDouble(value, out var dv) && JsonNumber.TryGetDouble(divisor, out var dd) && dd != 0)
        {
            var q = dv / dd;
            return Math.Abs(q - Math.Round(q)) < 1e-9;
        }
        return true;
    }

    // ----- Helpers -------------------------------------------------------------

    private JNode? ResolveRef(string reference)
    {
        if (reference == "#")
        {
            return _schemaRoot;
        }
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }
        var current = _schemaRoot;
        foreach (var raw in reference[2..].Split('/'))
        {
            var token = Uri.UnescapeDataString(raw).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            JNode? next = current.Kind switch
            {
                JNodeKind.Object => current.FindProperty(token),
                JNodeKind.Array when int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < current.Count => current[i],
                _ => null,
            };
            if (next is null)
            {
                return null;
            }
            current = next;
        }
        return current;
    }

    private Regex? GetRegex(string pattern) => _regexCache.GetOrAdd(pattern, p =>
    {
        try
        {
            return new Regex(p, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    });

    private static bool SafeIsMatch(Regex rx, string input)
    {
        try
        {
            return rx.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return true; // don't report a violation we could not evaluate
        }
    }

    private static int? GetInt(JNode schema, string name) =>
        schema.FindProperty(name) is { Kind: JNodeKind.Number } n && JsonNumber.TryGetDouble(n.Value, out var d) && d >= 0
            ? (int)Math.Min(d, int.MaxValue)
            : null;

    private static double? GetDouble(JNode schema, string name) =>
        schema.FindProperty(name) is { Kind: JNodeKind.Number } n && JsonNumber.TryGetDouble(n.Value, out var d) ? d : null;

    private static bool IsInteger(string? literal) =>
        JsonNumber.TryGetDouble(literal, out var d) && Math.Floor(d) == d && !double.IsInfinity(d);

    private static int CountCodePoints(string s)
    {
        int count = 0;
        foreach (var _ in s.EnumerateRunes())
        {
            count++;
        }
        return count;
    }

    private static string Fmt(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static string DescribeKind(JNode node) =>
        node.Kind == JNodeKind.Number && IsInteger(node.Value) ? "integer" : node.Kind.SchemaName();

    private static string Describe(JNode node) => node.Kind switch
    {
        JNodeKind.String => "\"" + node.Value + "\"",
        JNodeKind.Null => "null",
        JNodeKind.Object => "{…}",
        JNodeKind.Array => "[…]",
        _ => node.Value ?? string.Empty,
    };

    /// <summary>Validation state; probes share the token but collect errors privately.</summary>
    private sealed class Context(JsonSchemaValidator owner, CancellationToken token, List<Diagnostic> errors, bool isProbe = false)
    {
        public CancellationToken Token { get; } = token;

        public List<Diagnostic> Errors { get; } = errors;

        public bool Full => !isProbe && Errors.Count >= owner.MaxErrors;

        public Context CreateProbe() => new(owner, Token, new List<Diagnostic>(), isProbe: true);

        public bool Fail(PathBuilder path, string message, DiagnosticSeverity severity = DiagnosticSeverity.Error)
        {
            if (isProbe)
            {
                if (Errors.Count == 0)
                {
                    Errors.Add(new Diagnostic(severity, message));
                }
                return false;
            }
            if (Errors.Count < owner.MaxErrors)
            {
                Errors.Add(new Diagnostic(severity, message, path.ToString(), Source: "Schema", FileName: owner.SchemaFileName));
            }
            return severity != DiagnosticSeverity.Error;
        }
    }

    /// <summary>Incrementally maintained JSONPath; materialized only when an error is reported.</summary>
    private sealed class PathBuilder
    {
        private readonly List<string> _segments = new();

        public void PushName(string name) => _segments.Add(JsonPath.FormatMember(name));

        public void PushIndex(int index) => _segments.Add(JsonPath.FormatIndex(index));

        public void Pop() => _segments.RemoveAt(_segments.Count - 1);

        public override string ToString()
        {
            var sb = new StringBuilder(JsonPath.RootToken);
            foreach (var s in _segments)
            {
                sb.Append(s);
            }
            return sb.ToString();
        }
    }
}

/// <summary>Checks for the most common "format" values; unknown formats always pass (annotation only).</summary>
internal static partial class FormatChecker
{
    public static bool IsValid(string format, string value) => format switch
    {
        "date-time" => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) && DateTimeRegex().IsMatch(value),
        "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        "time" => TimeRegex().IsMatch(value),
        "email" => EmailRegex().IsMatch(value),
        "uri" => Uri.TryCreate(value, UriKind.Absolute, out _),
        "uri-reference" => Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _),
        "uuid" => Guid.TryParseExact(value, "D", out _),
        "ipv4" => IPAddress.TryParse(value, out var a4) && a4.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && value.Count(c => c == '.') == 3,
        "ipv6" => IPAddress.TryParse(value, out var a6) && a6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6,
        _ => true,
    };

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}[Tt ]\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:\d{2})$")]
    private static partial Regex DateTimeRegex();

    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:\d{2})?$")]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
