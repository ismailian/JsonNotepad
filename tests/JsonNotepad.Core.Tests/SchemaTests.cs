using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;
using JsonNotepad.Core.Validation;

namespace JsonNotepad.Core.Tests;

public sealed class SchemaTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static IReadOnlyList<Diagnostic> Validate(string schema, string instance, int maxErrors = 1000) =>
        new JsonSchemaValidator(JsonParser.Parse(schema)) { MaxErrors = maxErrors }.Validate(JsonParser.Parse(instance));

    private static void Valid(string schema, string instance)
    {
        var errors = Validate(schema, instance);
        if (errors.Count > 0)
        {
            Assert.Fail($"Expected {instance} to be valid against {schema}, got:\n  " + string.Join("\n  ", errors.Select(e => $"{e.Path}: {e.Message}")));
        }
    }

    /// <summary>Asserts exactly the given error paths (in order) are reported, and returns the diagnostics.</summary>
    private static IReadOnlyList<Diagnostic> Invalid(string schema, string instance, params string[] paths)
    {
        var errors = Validate(schema, instance);
        Assert.SequenceEqual(paths, errors.Select(e => e.Path ?? "<null>"), $"error paths for {instance} against {schema}: " + string.Join(" | ", errors.Select(e => e.Message)));
        Assert.True(errors.All(e => e.Severity == DiagnosticSeverity.Error && e.Source == "Schema"));
        return errors;
    }

    // ----- type / enum / const --------------------------------------------------

    [Test]
    public void TypeKeyword()
    {
        Valid("""{"type":"string"}""", "\"x\"");
        var e = Invalid("""{"type":"string"}""", "1", "$");
        Assert.Equal("Expected string but found integer.", e[0].Message);
        Valid("""{"type":"object"}""", "{}");
        Valid("""{"type":"array"}""", "[]");
        Valid("""{"type":"boolean"}""", "false");
        Valid("""{"type":"null"}""", "null");
        Valid("""{"type":"number"}""", "1");
        Valid("""{"type":"number"}""", "1.5");
        Invalid("""{"type":"number"}""", "\"1\"", "$");
        Invalid("""{"type":"object"}""", "[]", "$");
        Invalid("""{"type":"null"}""", "false", "$");
    }

    [Test]
    public void IntegerType()
    {
        Valid("""{"type":"integer"}""", "1");
        Valid("""{"type":"integer"}""", "-0");
        Valid("""{"type":"integer"}""", "1.0");
        Valid("""{"type":"integer"}""", "1e3");
        Valid("""{"type":"integer"}""", "12345678901234567890");
        var e = Invalid("""{"type":"integer"}""", "1.5", "$");
        Assert.Equal("Expected integer but found number.", e[0].Message);
        Invalid("""{"type":"integer"}""", "\"1\"", "$");
    }

    [Test]
    public void TypeArray()
    {
        Valid("""{"type":["string","null"]}""", "null");
        Valid("""{"type":["string","null"]}""", "\"s\"");
        var e = Invalid("""{"type":["string","null"]}""", "1", "$");
        Assert.Contains("one of [string, null]", e[0].Message);
    }

    [Test]
    public void EnumKeyword()
    {
        const string schema = """{"enum":[1,"a",{"x":[1,2]},null]}""";
        Valid(schema, "1");
        Valid(schema, "1.0");
        Valid(schema, "\"a\"");
        Valid(schema, """{"x":[1,2]}""");
        Valid(schema, "null");
        Invalid(schema, "\"b\"", "$");
        Invalid(schema, """{"x":[2,1]}""", "$");
        Invalid(schema, "true", "$");
    }

    [Test]
    public void ConstKeyword()
    {
        Valid("""{"const":{"a":[1,{"b":null}],"c":"d"}}""", """{"c":"d","a":[1.0,{"b":null}]}""");
        Invalid("""{"const":{"a":1}}""", """{"a":1,"b":2}""", "$");
        Invalid("""{"const":"1"}""", "1", "$");
        Valid("""{"const":null}""", "null");
    }

    // ----- objects ------------------------------------------------------------

    [Test]
    public void RequiredKeyword()
    {
        var e = Invalid("""{"required":["a","b","c"]}""", """{"a":1}""", "$", "$");
        Assert.Equal("Missing required property \"b\".", e[0].Message);
        Assert.Equal("Missing required property \"c\".", e[1].Message);
        Valid("""{"required":["a"]}""", """{"a":null}""");
        Valid("""{"required":["a"]}""", "[]");
    }

    [Test]
    public void PropertiesAndNestedPaths()
    {
        const string schema = """
            {"properties":{"user":{"properties":{"age":{"type":"integer"},"first name":{"type":"string"},
              "pets":{"items":{"properties":{"kind":{"enum":["cat","dog"]}}}}}}}}
            """;
        Valid(schema, """{"user":{"age":3,"first name":"x","pets":[{"kind":"cat"}]},"other":1}""");
        Invalid(schema, """{"user":{"age":"x"}}""", "$.user.age");
        Invalid(schema, """{"user":{"first name":1}}""", "$.user['first name']");
        Invalid(schema, """{"user":{"pets":[{"kind":"cat"},{"kind":"cow"}]}}""", "$.user.pets[1].kind");
    }

    [Test]
    public void AdditionalPropertiesFalse()
    {
        const string schema = """{"properties":{"a":{}},"additionalProperties":false}""";
        Valid(schema, """{"a":1}""");
        var e = Invalid(schema, """{"a":1,"b":2,"c":3}""", "$", "$");
        Assert.Equal("Property \"b\" is not allowed.", e[0].Message);
        Assert.Equal("Property \"c\" is not allowed.", e[1].Message);
    }

    [Test]
    public void AdditionalPropertiesSchema()
    {
        const string schema = """{"properties":{"a":{"type":"string"}},"additionalProperties":{"type":"number"}}""";
        Valid(schema, """{"a":"s","b":1,"c":2.5}""");
        Invalid(schema, """{"a":"s","b":"x"}""", "$.b");
        Invalid(schema, """{"a":1}""", "$.a");
    }

    [Test]
    public void PatternProperties()
    {
        const string schema = """{"patternProperties":{"^x-":{"type":"string"},"^n":{"type":"number"}},"additionalProperties":false}""";
        Valid(schema, """{"x-a":"s","n1":1}""");
        Invalid(schema, """{"x-a":"s","x-b":1,"y":1}""", "$['x-b']", "$");
        Invalid(schema, """{"nope":"s"}""", "$.nope");

        // A property may match both "properties" and a pattern: both apply.
        const string both = """{"properties":{"x-id":{"minLength":3}},"patternProperties":{"^x-":{"type":"string"}}}""";
        Invalid(both, """{"x-id":"ab"}""", "$['x-id']");
        Invalid(both, """{"x-id":123}""", "$['x-id']");
    }

    [Test]
    public void PropertyNames()
    {
        const string schema = """{"propertyNames":{"pattern":"^[a-z]+$","maxLength":5}}""";
        Valid(schema, """{"abc":1,"de":2}""");
        Invalid(schema, """{"abc":1,"Bad":2,"toolong":3}""", "$.Bad", "$.toolong");
    }

    [Test]
    public void MinAndMaxProperties()
    {
        Valid("""{"minProperties":1,"maxProperties":2}""", """{"a":1}""");
        Valid("""{"minProperties":1,"maxProperties":2}""", """{"a":1,"b":2}""");
        var e = Invalid("""{"minProperties":1}""", "{}", "$");
        Assert.Contains("at least 1", e[0].Message);
        Invalid("""{"maxProperties":2}""", """{"a":1,"b":2,"c":3}""", "$");
    }

    [Test]
    public void DependentRequired()
    {
        const string schema = """{"dependentRequired":{"card":["billing","cvv"]}}""";
        Valid(schema, """{"x":1}""");
        Valid(schema, """{"card":1,"billing":2,"cvv":3}""");
        var e = Invalid(schema, """{"card":1,"billing":2}""", "$");
        Assert.Equal("Property \"cvv\" is required when \"card\" is present.", e[0].Message);
    }

    // ----- arrays ---------------------------------------------------------------

    [Test]
    public void ItemsSchema()
    {
        Valid("""{"items":{"type":"number"}}""", "[1,2.5,3]");
        Invalid("""{"items":{"type":"number"}}""", """[1,"a",3,"b"]""", "$[1]", "$[3]");
        Valid("""{"items":{"type":"number"}}""", "[]");
        Valid("""{"items":{"type":"number"}}""", "\"not an array\"");
    }

    [Test]
    public void PrefixItems()
    {
        const string schema = """{"prefixItems":[{"type":"string"},{"type":"number"}],"items":false}""";
        Valid(schema, """["a",1]""");
        Valid(schema, """["a"]""");
        Invalid(schema, """["a",1,true]""", "$[2]");
        Invalid(schema, "[1]", "$[0]");

        const string open = """{"prefixItems":[{"type":"string"}],"items":{"type":"boolean"}}""";
        Valid(open, """["a",true,false]""");
        Invalid(open, """["a",true,1]""", "$[2]");
    }

    [Test]
    public void Draft7TupleItemsAndAdditionalItems()
    {
        const string closed = """{"items":[{"type":"string"},{"type":"integer"}],"additionalItems":false}""";
        Valid(closed, """["a",1]""");
        var e = Invalid(closed, """["a",1,null]""", "$[2]");
        Assert.Contains("Additional items", e[0].Message);
        Invalid(closed, """[1,1]""", "$[0]");

        const string typed = """{"items":[{"type":"string"}],"additionalItems":{"type":"number"}}""";
        Valid(typed, """["a",1,2]""");
        Invalid(typed, """["a","b"]""", "$[1]");

        Valid("""{"items":[{"type":"string"}]}""", """["a",1,{}]""");
    }

    [Test]
    public void Contains()
    {
        Valid("""{"contains":{"type":"string"}}""", """[1,"a"]""");
        var e = Invalid("""{"contains":{"type":"string"}}""", "[1,2]", "$");
        Assert.Contains("at least one", e[0].Message);
        Invalid("""{"contains":{"type":"string"}}""", "[]", "$");
        Valid("""{"contains":{"type":"string"},"minContains":2}""", """["a",1,"b"]""");
        Invalid("""{"contains":{"type":"string"},"minContains":2}""", """["a",1]""", "$");
        Invalid("""{"contains":{"type":"string"},"maxContains":1}""", """["a","b"]""", "$");
        Valid("""{"contains":{"type":"string"},"minContains":0}""", "[1]");
    }

    [Test]
    public void MinAndMaxItems()
    {
        Valid("""{"minItems":1,"maxItems":2}""", "[1]");
        Invalid("""{"minItems":1}""", "[]", "$");
        Invalid("""{"maxItems":2}""", "[1,2,3]", "$");
    }

    [Test]
    public void UniqueItems()
    {
        Valid("""{"uniqueItems":true}""", """[1,"1",[1],{"a":1},true,null]""");
        Invalid("""{"uniqueItems":true}""", "[1,2,1.0]", "$[2]");
        Invalid("""{"uniqueItems":true}""", """[{"a":1,"b":[2]},{"b":[2],"a":1}]""", "$[1]");
        Invalid("""{"uniqueItems":true}""", """["x",null,"x",null]""", "$[2]");
        Valid("""{"uniqueItems":false}""", "[1,1]");
        Valid("""{"uniqueItems":true}""", "[[1,2],[2,1]]");
    }

    // ----- numbers ------------------------------------------------------------

    [Test]
    public void MinimumAndMaximum()
    {
        Valid("""{"minimum":0,"maximum":10}""", "0");
        Valid("""{"minimum":0,"maximum":10}""", "10");
        Valid("""{"minimum":0,"maximum":10}""", "5.5");
        var low = Invalid("""{"minimum":0}""", "-1", "$");
        Assert.Equal("Value must be at least 0.", low[0].Message);
        Invalid("""{"maximum":10}""", "10.0001", "$");
        Valid("""{"minimum":0}""", "\"-1\"");
    }

    [Test]
    public void Draft4BooleanExclusiveBounds()
    {
        const string schema = """{"minimum":0,"exclusiveMinimum":true,"maximum":5,"exclusiveMaximum":true}""";
        Valid(schema, "0.1");
        Valid(schema, "4.9");
        var e = Invalid(schema, "0", "$");
        Assert.Equal("Value must be greater than 0.", e[0].Message);
        Invalid(schema, "5", "$");

        const string inclusive = """{"minimum":0,"exclusiveMinimum":false,"maximum":5,"exclusiveMaximum":false}""";
        Valid(inclusive, "0");
        Valid(inclusive, "5");
    }

    [Test]
    public void NumericExclusiveBounds()
    {
        Valid("""{"exclusiveMinimum":0}""", "0.0001");
        Invalid("""{"exclusiveMinimum":0}""", "0", "$");
        Invalid("""{"exclusiveMinimum":0}""", "-3", "$");
        Valid("""{"exclusiveMaximum":5}""", "4.9");
        var e = Invalid("""{"exclusiveMaximum":5}""", "5", "$");
        Assert.Equal("Value must be less than 5.", e[0].Message);
    }

    [Test]
    public void MultipleOf()
    {
        Valid("""{"multipleOf":0.1}""", "0.3");
        Valid("""{"multipleOf":0.1}""", "1.2");
        Valid("""{"multipleOf":0.1}""", "-7");
        Invalid("""{"multipleOf":0.1}""", "0.35", "$");
        Valid("""{"multipleOf":0.01}""", "19.99");
        Invalid("""{"multipleOf":0.01}""", "19.999", "$");
        Valid("""{"multipleOf":3}""", "9");
        Valid("""{"multipleOf":3}""", "0");
        Invalid("""{"multipleOf":3}""", "10", "$");
        Valid("""{"multipleOf":1.5}""", "4.5");
        Valid("""{"multipleOf":2}""", "1e2");
    }

    // ----- strings ------------------------------------------------------------

    [Test]
    public void LengthCountsCodePoints()
    {
        var twoEmoji = "\"" + "😀😀" + "\""; // 4 UTF-16 units, 2 code points
        Valid("""{"maxLength":2}""", twoEmoji);
        Valid("""{"minLength":2}""", twoEmoji);
        var e = Invalid("""{"minLength":3}""", twoEmoji, "$");
        Assert.Contains("(is 2)", e[0].Message);
        Invalid("""{"maxLength":1}""", twoEmoji, "$");
        Valid("""{"minLength":0,"maxLength":0}""", "\"\"");
        Valid("""{"maxLength":3}""", "\"ééé\"");
    }

    [Test]
    public void PatternKeyword()
    {
        Valid("""{"pattern":"^a+$"}""", "\"aaa\"");
        Invalid("""{"pattern":"^a+$"}""", "\"ab\"", "$");
        Valid("""{"pattern":"b"}""", "\"abc\"");
        Valid("""{"pattern":"^\\d{3}-\\d{4}$"}""", "\"555-1234\"");

        var bad = Validate("""{"pattern":"("}""", "\"x\"");
        var warning = Assert.Single(bad);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("not a valid regular expression", warning.Message);
    }

    [Test]
    public void Formats()
    {
        var table = new (string Format, string Value, bool Ok)[]
        {
            ("date-time", "2024-01-02T03:04:05Z", true),
            ("date-time", "2024-01-02T03:04:05.123+02:00", true),
            ("date-time", "2024-01-02", false),
            ("date-time", "2024-13-02T00:00:00Z", false),
            ("date-time", "2024-01-02T03:04:05", false),
            ("date", "2024-02-29", true),
            ("date", "2023-02-29", false),
            ("date", "2024-2-9", false),
            ("time", "12:34:56", true),
            ("time", "12:34:56.5Z", true),
            ("time", "1:2:3", false),
            ("email", "a.b@example.co", true),
            ("email", "a@b", false),
            ("email", "two words@x.org", false),
            ("uri", "https://example.org/a?b=c", true),
            ("uri", "relative/path", false),
            ("uri-reference", "relative/path", true),
            ("uuid", "123e4567-e89b-12d3-a456-426614174000", true),
            ("uuid", "123e4567e89b12d3a456426614174000", false),
            ("ipv4", "192.168.0.1", true),
            ("ipv4", "256.1.1.1", false),
            ("ipv4", "1.2.3", false),
            ("ipv4", "::1", false),
            ("ipv6", "::1", true),
            ("ipv6", "2001:db8::8a2e:370:7334", true),
            ("ipv6", "1.2.3.4", false),
            ("some-unknown-format", "anything", true),
        };
        foreach (var (format, value, ok) in table)
        {
            var errors = Validate($$"""{"format":"{{format}}"}""", "\"" + value + "\"");
            Assert.Equal(ok ? 0 : 1, errors.Count, $"{format} \"{value}\"");
        }
        Valid("""{"format":"email"}""", "42");
    }

    // ----- combinators --------------------------------------------------------

    [Test]
    public void AllOf()
    {
        const string schema = """{"allOf":[{"type":"number"},{"minimum":5},{"maximum":10}]}""";
        Valid(schema, "7");
        Invalid(schema, "3", "$");
        Invalid(schema, "\"x\"", "$");
    }

    [Test]
    public void AnyOf()
    {
        const string schema = """{"anyOf":[{"type":"string","maxLength":2},{"type":"number"}]}""";
        Valid(schema, "1");
        Valid(schema, "\"ab\"");
        var e = Invalid(schema, "\"abc\"", "$");
        Assert.Contains("anyOf", e[0].Message);
        Invalid(schema, "true", "$");
    }

    [Test]
    public void OneOf()
    {
        const string schema = """{"oneOf":[{"type":"number"},{"type":"integer"}]}""";
        Valid(schema, "1.5");
        var both = Invalid(schema, "1", "$");
        Assert.Contains("more than one", both[0].Message);
        var none = Invalid(schema, "\"x\"", "$");
        Assert.Contains("does not match any", none[0].Message);
    }

    [Test]
    public void Not()
    {
        Valid("""{"not":{"type":"string"}}""", "1");
        Invalid("""{"not":{"type":"string"}}""", "\"x\"", "$");
        Invalid("""{"properties":{"a":{"not":{"const":0}}}}""", """{"a":0}""", "$.a");
    }

    [Test]
    public void IfThenElse()
    {
        const string schema = """
            {"if":{"properties":{"kind":{"const":"a"}},"required":["kind"]},
             "then":{"required":["a"]},
             "else":{"required":["b"]}}
            """;
        Valid(schema, """{"kind":"a","a":1}""");
        Valid(schema, """{"kind":"b","b":1}""");
        Assert.Equal("Missing required property \"a\".", Invalid(schema, """{"kind":"a"}""", "$")[0].Message);
        Assert.Equal("Missing required property \"b\".", Invalid(schema, """{"kind":"b"}""", "$")[0].Message);
        Assert.Equal("Missing required property \"b\".", Invalid(schema, "{}", "$")[0].Message);

        Valid("""{"if":{"type":"string"}}""", "1");
        Valid("""{"if":{"type":"string"},"then":{"minLength":1}}""", "5");
        Invalid("""{"if":{"type":"string"},"then":{"minLength":1}}""", "\"\"", "$");
    }

    [Test]
    public void CombinatorErrorsInsideBranchesAreNotReported()
    {
        // anyOf / oneOf / not / if probe their subschemas silently; only the summary error is reported.
        var errors = Validate("""{"anyOf":[{"required":["a","b","c"]},{"required":["d"]}]}""", "{}");
        Assert.Single(errors);
    }

    // ----- $ref -----------------------------------------------------------------

    [Test]
    public void RefToDefs()
    {
        const string schema = """
            {"$defs":{"pos":{"type":"integer","minimum":1}},
             "properties":{"n":{"$ref":"#/$defs/pos"},"list":{"items":{"$ref":"#/$defs/pos"}}}}
            """;
        Valid(schema, """{"n":1,"list":[2,3]}""");
        Invalid(schema, """{"n":0}""", "$.n");
        Invalid(schema, """{"list":[1,1.5,-1]}""", "$.list[1]", "$.list[2]");
    }

    [Test]
    public void RefToDefinitionsAndPointerEscapes()
    {
        Invalid("""{"definitions":{"s":{"type":"string"}},"items":{"$ref":"#/definitions/s"}}""", """["a",1]""", "$[1]");
        Invalid("""{"$defs":{"a/b":{"type":"null"},"t~n":{"type":"boolean"}},"properties":{"x":{"$ref":"#/$defs/a~1b"},"y":{"$ref":"#/$defs/t~0n"}}}""", """{"x":1,"y":2}""", "$.x", "$.y");
        Invalid("""{"$defs":{"list":[{"type":"string"},{"type":"number"}]},"$ref":"#/$defs/list/1"}""", "\"s\"", "$");
        Valid("""{"$defs":{"a b":{"type":"null"}},"$ref":"#/$defs/a%20b"}""", "null");
    }

    [Test]
    public void RecursiveRefToRoot()
    {
        const string schema = """{"required":["id"],"properties":{"child":{"$ref":"#"}}}""";
        Valid(schema, """{"id":1,"child":{"id":2,"child":{"id":3}}}""");
        Invalid(schema, """{"id":1,"child":{"id":2,"child":{}}}""", "$.child.child");
    }

    [Test]
    public void CyclicRefTerminates()
    {
        var errors = Validate("""{"$defs":{"a":{"$ref":"#/$defs/b"},"b":{"$ref":"#/$defs/a"}},"$ref":"#/$defs/a"}""", "1");
        var e = Assert.Single(errors);
        Assert.Contains("too deep", e.Message);
    }

    [Test]
    public void UnresolvableRefIsAWarning()
    {
        foreach (var reference in new[] { "http://example.com/schema.json", "#/$defs/missing", "other.json#/x" })
        {
            var errors = Validate($$"""{"$ref":"{{reference}}"}""", "1");
            var w = Assert.Single(errors, reference);
            Assert.Equal(DiagnosticSeverity.Warning, w.Severity);
            Assert.Contains(reference, w.Message);
            Assert.Equal("$", w.Path);
        }
    }

    // ----- boolean schemas, paths, limits ---------------------------------------

    [Test]
    public void BooleanSchemas()
    {
        Valid("true", """{"anything":[1,2]}""");
        Valid("{}", "null");
        Invalid("false", "1", "$");
        Invalid("""{"properties":{"a":false}}""", """{"a":1}""", "$.a");
        Valid("""{"properties":{"a":false}}""", """{"b":1}""");
        Invalid("""{"items":{"properties":{"x":{"anyOf":[false,{"type":"string"}]}}}}""", """[{"x":"ok"},{"x":1}]""", "$[1].x");
        Assert.Throws<ArgumentException>(() => new JsonSchemaValidator(JNode.CreateNumber("1")));
        Assert.Throws<ArgumentException>(() => new JsonSchemaValidator(JNode.CreateArray()));
    }

    [Test]
    public void ErrorPathsAreResolvableJsonPaths()
    {
        const string schema = """{"properties":{"list":{"items":{"properties":{"first name":{"type":"string"},"it's":{"type":"string"}}}}}}""";
        const string instance = """{"list":[{"first name":"a"},{"first name":2,"it's":false}]}""";
        var root = JsonParser.Parse(instance);
        var errors = Validate(schema, instance);
        Assert.SequenceEqual(new[] { "$.list[1]['first name']", "$.list[1]['it\\'s']" }, errors.Select(e => e.Path!));
        foreach (var e in errors)
        {
            Assert.NotNull(Paths.JsonPath.Resolve(root, e.Path!), "path must resolve: " + e.Path);
        }
    }

    [Test]
    public void MaxErrorsCapsTheReport()
    {
        var instance = "[" + string.Join(",", Enumerable.Range(0, 50)) + "]";
        Assert.Equal(50, Validate("""{"items":{"type":"string"}}""", instance).Count);
        Assert.Equal(5, Validate("""{"items":{"type":"string"}}""", instance, maxErrors: 5).Count);
        Assert.Equal(1, Validate("""{"items":{"type":"string","minimum":100}}""", instance, maxErrors: 1).Count);
    }

    [Test]
    public void DiagnosticsCarrySchemaFileName()
    {
        var validator = new JsonSchemaValidator(JsonParser.Parse("""{"type":"string"}""")) { SchemaFileName = "s.json" };
        var e = Assert.Single(validator.Validate(JNode.CreateNull()));
        Assert.Equal("s.json", e.FileName);
    }

    [Test]
    public void ValidationHonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var validator = new JsonSchemaValidator(JsonParser.Parse("{}"));
        Assert.Throws<OperationCanceledException>(() => validator.Validate(JNode.CreateNull(), cts.Token));
    }

    // ----- SchemaValidationService -------------------------------------------------

    [Test]
    public void ResolveSchemaPath()
    {
        var doc = Json.Doc("""{"$schema":"schemas/s.json"}""");
        var docPath = _temp.File("doc.json");
        Assert.Equal(Path.Combine(_temp.Path, "schemas", "s.json"), SchemaValidationService.ResolveSchemaPath(doc, docPath, null, out var note));
        Assert.Null(note);

        Assert.Equal("explicit.json", SchemaValidationService.ResolveSchemaPath(doc, docPath, "explicit.json", out _), "an explicit association wins");

        var remote = Json.Doc("""{"$schema":"https://json-schema.org/draft/2020-12/schema"}""");
        Assert.Null(SchemaValidationService.ResolveSchemaPath(remote, docPath, null, out var remoteNote));
        Assert.Contains("not downloaded", remoteNote);

        Assert.Null(SchemaValidationService.ResolveSchemaPath(Json.Doc("{}"), docPath, null, out _));
        Assert.Null(SchemaValidationService.ResolveSchemaPath(Json.Doc("""{"$schema":5}"""), docPath, null, out _));
        Assert.Null(SchemaValidationService.ResolveSchemaPath(Json.Doc("[]"), docPath, null, out _));
    }

    [Test]
    public async Task ValidateAsyncUsesFileAndReportsBrokenSchemas()
    {
        var schemaPath = _temp.File("schema.json");
        await File.WriteAllTextAsync(schemaPath, """{"properties":{"n":{"type":"number"}}}""");
        var doc = Json.Doc("""{"n":"x"}""");
        var service = new SchemaValidationService();

        var result = await service.ValidateAsync(doc, schemaPath, CancellationToken.None);
        Assert.Equal(doc.Version, result.DocumentVersion);
        Assert.Equal(schemaPath, result.SchemaPath);
        Assert.Equal("$.n", Assert.Single(result.Diagnostics).Path);
        Assert.Equal(schemaPath, result.Diagnostics[0].FileName);

        // Edit the schema: the cache notices the new timestamp.
        await File.WriteAllTextAsync(schemaPath, """{"properties":{"n":{"type":"string"}}}""");
        File.SetLastWriteTimeUtc(schemaPath, DateTime.UtcNow.AddMinutes(1));
        Assert.Empty((await service.ValidateAsync(doc, schemaPath, CancellationToken.None)).Diagnostics);

        await File.WriteAllTextAsync(schemaPath, "{ broken");
        File.SetLastWriteTimeUtc(schemaPath, DateTime.UtcNow.AddMinutes(2));
        var broken = Assert.Single((await service.ValidateAsync(doc, schemaPath, CancellationToken.None)).Diagnostics);
        Assert.Contains("not valid JSON", broken.Message);
        Assert.Equal(1L, broken.Line);

        var missing = Assert.Single((await service.ValidateAsync(doc, _temp.File("nope.json"), CancellationToken.None)).Diagnostics);
        Assert.Contains("Cannot load schema", missing.Message);
    }
}
