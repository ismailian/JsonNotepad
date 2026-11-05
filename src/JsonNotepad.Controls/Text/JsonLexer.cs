namespace JsonNotepad.Controls.Text;

internal enum TokenKind : byte
{
    Punctuation,
    Key,
    String,
    Number,
    Keyword,
    Other,
}

internal readonly record struct Token(int Start, int Length, TokenKind Kind);

/// <summary>
/// Single-line JSON tokenizer for syntax coloring. JSON strings can't contain raw line
/// breaks, so every line can be colored independently — which is what lets the text view
/// color only the lines on screen.
/// </summary>
internal static class JsonLexer
{
    /// <summary>Tokenizes <paramref name="line"/> up to <paramref name="limit"/> characters.</summary>
    public static void Tokenize(ReadOnlySpan<char> line, int limit, List<Token> tokens)
    {
        tokens.Clear();
        int n = Math.Min(line.Length, limit);
        int i = 0;
        while (i < n)
        {
            char c = line[i];
            if (c is ' ' or '\t')
            {
                i++;
                continue;
            }
            int start = i;
            switch (c)
            {
                case '{' or '}' or '[' or ']' or ':' or ',':
                    tokens.Add(new Token(i, 1, TokenKind.Punctuation));
                    i++;
                    break;
                case '"':
                    i++;
                    while (i < line.Length)
                    {
                        if (line[i] == '\\')
                        {
                            i += 2;
                            continue;
                        }
                        if (line[i] == '"')
                        {
                            i++;
                            break;
                        }
                        i++;
                    }
                    i = Math.Min(i, line.Length);
                    int j = i;
                    while (j < line.Length && line[j] == ' ')
                    {
                        j++;
                    }
                    tokens.Add(new Token(start, i - start, j < line.Length && line[j] == ':' ? TokenKind.Key : TokenKind.String));
                    break;
                case '-' or (>= '0' and <= '9'):
                    while (i < n && (char.IsAsciiDigit(line[i]) || line[i] is '-' or '+' or '.' or 'e' or 'E'))
                    {
                        i++;
                    }
                    tokens.Add(new Token(start, i - start, TokenKind.Number));
                    break;
                default:
                    while (i < n && char.IsAsciiLetter(line[i]))
                    {
                        i++;
                    }
                    if (i == start)
                    {
                        i++;
                    }
                    var word = line[start..i];
                    tokens.Add(new Token(start, i - start, word is "true" or "false" or "null" ? TokenKind.Keyword : TokenKind.Other));
                    break;
            }
        }
    }
}
