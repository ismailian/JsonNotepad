using System.Text;

namespace JsonNotepad.Core.IO;

/// <summary>Controls how lenient the parser is.</summary>
public sealed record ParseOptions
{
    public static ParseOptions Default { get; } = new();

    public static ParseOptions Strict { get; } = new() { AllowComments = false, AllowTrailingCommas = false };

    /// <summary>Accept // and /* */ comments (JSONC). Comments are not preserved on save.</summary>
    public bool AllowComments { get; init; } = true;

    public bool AllowTrailingCommas { get; init; } = true;

    public int MaxDepth { get; init; } = 1024;

    /// <summary>Report duplicate keys in objects as warnings.</summary>
    public bool DetectDuplicateKeys { get; init; } = true;

    /// <summary>Upper bound for the number of warnings collected while loading.</summary>
    public int MaxWarnings { get; init; } = 500;
}

/// <summary>Controls how the model is serialized.</summary>
public sealed record FormatOptions
{
    public static FormatOptions Default { get; } = new();

    public static FormatOptions Compact { get; } = new() { Indented = false };

    public bool Indented { get; init; } = true;

    public int IndentSize { get; init; } = 2;

    public bool UseTabs { get; init; }

    public string NewLine { get; init; } = "\r\n";

    /// <summary>Escape every non-ASCII character as \uXXXX.</summary>
    public bool EscapeNonAscii { get; init; }

    /// <summary>Write a line break after the closing bracket of the root value.</summary>
    public bool TrailingNewLine { get; init; } = true;

    internal string IndentUnit => UseTabs ? "\t" : new string(' ', Math.Clamp(IndentSize, 0, 16));
}

/// <summary>
/// What we learned about a file's physical format when it was loaded, so that saving
/// can preserve it (encoding, BOM, line endings, indentation style).
/// </summary>
public sealed record FileFormatInfo
{
    public static FileFormatInfo Default { get; } = new();

    public Encoding Encoding { get; init; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public bool HasByteOrderMark { get; init; }

    public string NewLine { get; init; } = "\r\n";

    public int IndentSize { get; init; } = 2;

    public bool UseTabs { get; init; }

    /// <summary>The file had no line breaks (minified).</summary>
    public bool WasCompact { get; init; }

    public bool HadTrailingNewLine { get; init; } = true;

    public string EncodingDisplayName
    {
        get
        {
            var name = Encoding.CodePage switch
            {
                65001 => "UTF-8",
                1200 => "UTF-16 LE",
                1201 => "UTF-16 BE",
                12000 => "UTF-32 LE",
                12001 => "UTF-32 BE",
                _ => Encoding.WebName.ToUpperInvariant(),
            };
            return HasByteOrderMark ? name + " BOM" : name;
        }
    }

    public string NewLineDisplayName => NewLine == "\n" ? "LF" : "CRLF";

    /// <summary>Format options that reproduce this file's original style.</summary>
    public FormatOptions ToFormatOptions(FormatOptions fallback) => fallback with
    {
        Indented = !WasCompact,
        IndentSize = IndentSize,
        UseTabs = UseTabs,
        NewLine = NewLine,
        TrailingNewLine = HadTrailingNewLine,
    };
}
