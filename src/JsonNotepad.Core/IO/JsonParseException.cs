namespace JsonNotepad.Core.IO;

/// <summary>A syntax error with a 1-based location in the source text.</summary>
public sealed class JsonParseException : Exception
{
    public JsonParseException(string message, long line, long column, Exception? inner = null)
        : base(message, inner)
    {
        Line = line;
        Column = column;
    }

    public JsonParseException()
    {
    }

    public JsonParseException(string message)
        : base(message)
    {
    }

    public JsonParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>1-based line, or 0 when unknown.</summary>
    public long Line { get; }

    /// <summary>1-based column (in UTF-8 bytes, as reported by System.Text.Json), or 0 when unknown.</summary>
    public long Column { get; }
}
