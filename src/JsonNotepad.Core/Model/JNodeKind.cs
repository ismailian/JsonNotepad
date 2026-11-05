namespace JsonNotepad.Core.Model;

/// <summary>The six JSON value types (RFC 8259).</summary>
public enum JNodeKind : byte
{
    Object,
    Array,
    String,
    Number,
    Boolean,
    Null,
}

public static class JNodeKindExtensions
{
    public static bool IsContainer(this JNodeKind kind) => kind is JNodeKind.Object or JNodeKind.Array;

    public static string DisplayName(this JNodeKind kind) => kind switch
    {
        JNodeKind.Object => "Object",
        JNodeKind.Array => "Array",
        JNodeKind.String => "String",
        JNodeKind.Number => "Number",
        JNodeKind.Boolean => "Boolean",
        JNodeKind.Null => "Null",
        _ => kind.ToString(),
    };

    /// <summary>Name used by JSON Schema's "type" keyword.</summary>
    public static string SchemaName(this JNodeKind kind) => kind switch
    {
        JNodeKind.Object => "object",
        JNodeKind.Array => "array",
        JNodeKind.String => "string",
        JNodeKind.Number => "number",
        JNodeKind.Boolean => "boolean",
        _ => "null",
    };
}
