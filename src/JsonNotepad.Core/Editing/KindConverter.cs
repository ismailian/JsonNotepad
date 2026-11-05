using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Editing;

/// <summary>
/// Implements "Change To" — converting a node to another JSON type while keeping as
/// much of its content as makes sense. Each conversion produces a NEW detached node,
/// so the change is a simple, undoable replace.
/// </summary>
public static class KindConverter
{
    public static JNode Convert(JNode source, JNodeKind target)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind == target)
        {
            return source.DeepClone();
        }

        var result = (source.Kind, target) switch
        {
            // Containers <-> containers keep their children.
            (JNodeKind.Object, JNodeKind.Array) => ObjectToArray(source),
            (JNodeKind.Array, JNodeKind.Object) => ArrayToObject(source),

            // Containers -> string: "stringify" (handy for double-encoded JSON fields).
            (JNodeKind.Object or JNodeKind.Array, JNodeKind.String) =>
                JNode.CreateString(JsonDocumentWriter.ToJson(source.DeepClone().WithName(null), FormatOptions.Compact)),

            // String -> container: parse it if it contains JSON of that shape ("unstringify").
            (JNodeKind.String, JNodeKind.Object or JNodeKind.Array) => ParseEmbedded(source.Value, target),

            (_, JNodeKind.String) => JNode.CreateString(source.Kind == JNodeKind.Null ? string.Empty : source.Value ?? string.Empty),
            (_, JNodeKind.Number) => ToNumber(source),
            (_, JNodeKind.Boolean) => JNode.CreateBoolean(ToBoolean(source)),
            (_, JNodeKind.Null) => JNode.CreateNull(),
            _ => JNode.Create(target),
        };
        return result.WithName(source.Name);
    }

    private static JNode ObjectToArray(JNode source)
    {
        var array = JNode.CreateArray();
        foreach (var child in source.Children)
        {
            array.AddChildCore(child.DeepClone().WithName(null));
        }
        return array;
    }

    private static JNode ArrayToObject(JNode source)
    {
        var obj = JNode.CreateObject();
        int i = 0;
        foreach (var child in source.Children)
        {
            obj.AddChildCore(child.DeepClone().WithName(i++.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return obj;
    }

    private static JNode ParseEmbedded(string? text, JNodeKind target)
    {
        if (!string.IsNullOrWhiteSpace(text) && JsonParser.TryParse(text, ParseOptions.Default, out var parsed, out _) && parsed!.Kind == target)
        {
            return parsed;
        }
        return JNode.Create(target);
    }

    private static JNode ToNumber(JNode source)
    {
        return source.Kind switch
        {
            JNodeKind.String when JsonNumber.TryNormalize(source.Value, out var literal) => JNode.CreateNumber(literal),
            JNodeKind.Boolean => JNode.CreateNumber(source.BooleanValue ? "1" : "0"),
            JNodeKind.Object or JNodeKind.Array => JNode.CreateNumber(source.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _ => JNode.CreateNumber("0"),
        };
    }

    private static bool ToBoolean(JNode source) => source.Kind switch
    {
        JNodeKind.String => string.Equals(source.Value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                            || source.Value?.Trim() == "1"
                            || string.Equals(source.Value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase),
        JNodeKind.Number => JsonNumber.TryGetDouble(source.Value, out var d) && d != 0,
        JNodeKind.Object or JNodeKind.Array => source.Count > 0,
        _ => false,
    };
}
