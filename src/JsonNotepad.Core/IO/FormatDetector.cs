using System.Text;

namespace JsonNotepad.Core.IO;

/// <summary>
/// Sniffs the encoding (BOM) and the formatting style (line endings, indentation)
/// from the first block of a file so that saves preserve the author's conventions.
/// </summary>
public static class FormatDetector
{
    public const int SampleSize = 64 * 1024;

    /// <summary>Detects a byte order mark. Returns the encoding and the BOM length (0 when none).</summary>
    public static (Encoding Encoding, int BomLength) DetectEncoding(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4 && head[0] == 0xFF && head[1] == 0xFE && head[2] == 0 && head[3] == 0)
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
        }
        if (head.Length >= 4 && head[0] == 0 && head[1] == 0 && head[2] == 0xFE && head[3] == 0xFF)
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4);
        }
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 3);
        }
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), 2);
        }
        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF)
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), 2);
        }

        // BOM-less UTF-16 heuristic: JSON text starts with ASCII, so a zero in the
        // first two bytes gives the byte order away (RFC 4627 §3).
        if (head.Length >= 4)
        {
            if (head[0] == 0 && head[1] != 0 && head[2] == 0)
            {
                return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 0);
            }
            if (head[0] != 0 && head[1] == 0 && head[3] == 0 && head[2] != 0)
            {
                return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 0);
            }
        }
        return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 0);
    }

    /// <summary>Inspects decoded text to infer line endings and indentation.</summary>
    public static FileFormatInfo DetectStyle(ReadOnlySpan<char> sample, Encoding encoding, bool hasBom, bool isWholeFile)
    {
        int lf = sample.IndexOf('\n');
        bool compact = lf < 0 && (isWholeFile || sample.Length >= SampleSize / 2);
        string newLine = lf > 0 && sample[lf - 1] == '\r' ? "\r\n" : lf >= 0 ? "\n" : "\r\n";

        int indentSize = 2;
        bool tabs = false;
        int smallest = int.MaxValue;

        // Look at the leading whitespace of the first lines that are indented.
        int pos = 0;
        int linesInspected = 0;
        while (pos < sample.Length && linesInspected < 200)
        {
            int nl = sample[pos..].IndexOf('\n');
            if (nl < 0)
            {
                break;
            }
            pos += nl + 1;
            linesInspected++;
            int spaces = 0;
            while (pos + spaces < sample.Length && sample[pos + spaces] == ' ')
            {
                spaces++;
            }
            if (pos < sample.Length && sample[pos] == '\t')
            {
                tabs = true;
                break;
            }
            if (spaces > 0 && spaces < smallest)
            {
                smallest = spaces;
                if (smallest == 1)
                {
                    break;
                }
            }
        }
        if (!tabs && smallest != int.MaxValue)
        {
            indentSize = Math.Clamp(smallest, 1, 8);
        }

        bool trailingNewLine = true;
        if (isWholeFile)
        {
            var trimmed = sample.TrimEnd(" \t");
            trailingNewLine = trimmed.Length > 0 && (trimmed[^1] == '\n');
        }

        return new FileFormatInfo
        {
            Encoding = encoding,
            HasByteOrderMark = hasBom,
            NewLine = newLine,
            IndentSize = indentSize,
            UseTabs = tabs,
            WasCompact = compact,
            HadTrailingNewLine = trailingNewLine,
        };
    }
}
