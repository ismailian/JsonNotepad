using System.Globalization;

namespace JsonNotepad.Core.Model;

/// <summary>Validation and conversion helpers for JSON number literals (RFC 8259 §6).</summary>
public static class JsonNumber
{
    /// <summary>Strict grammar check: <c>-? (0 | [1-9][0-9]*) (. [0-9]+)? ([eE] [+-]? [0-9]+)?</c>.</summary>
    public static bool IsValid(ReadOnlySpan<char> s)
    {
        int i = 0;
        int n = s.Length;
        if (n == 0)
        {
            return false;
        }
        if (s[i] == '-')
        {
            i++;
        }
        if (i >= n)
        {
            return false;
        }
        if (s[i] == '0')
        {
            i++;
        }
        else if (s[i] is >= '1' and <= '9')
        {
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
            }
        }
        else
        {
            return false;
        }

        if (i < n && s[i] == '.')
        {
            i++;
            int start = i;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
            }
            if (i == start)
            {
                return false;
            }
        }

        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < n && (s[i] == '+' || s[i] == '-'))
            {
                i++;
            }
            int start = i;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
            }
            if (i == start)
            {
                return false;
            }
        }
        return i == n;
    }

    /// <summary>
    /// Accepts user input such as " 42 ", "+3", ".5" or "1,5"-free numbers and returns a canonical JSON literal.
    /// The original digits are preserved whenever the input is already valid.
    /// </summary>
    public static bool TryNormalize(string? input, out string literal)
    {
        literal = string.Empty;
        if (input is null)
        {
            return false;
        }
        var s = input.Trim();
        if (IsValid(s))
        {
            literal = s;
            return true;
        }
        // Be forgiving with common inputs that are valid C# numbers but not JSON.
        if (s.StartsWith('+'))
        {
            s = s[1..];
        }
        if (s.StartsWith('.'))
        {
            s = "0" + s;
        }
        else if (s.StartsWith("-.", StringComparison.Ordinal))
        {
            s = "-0" + s[1..];
        }
        if (s.EndsWith('.'))
        {
            s = s[..^1];
        }
        if (IsValid(s))
        {
            literal = s;
            return true;
        }
        if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            literal = d.ToString(CultureInfo.InvariantCulture);
            return IsValid(literal);
        }
        return false;
    }

    public static bool TryGetDouble(string? literal, out double value) =>
        double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static bool TryGetDecimal(string? literal, out decimal value) =>
        decimal.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Numeric equality that ignores representation (1, 1.0 and 1e0 are equal).</summary>
    public static bool NumericEquals(string? a, string? b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }
        if (TryGetDecimal(a, out var da) && TryGetDecimal(b, out var db))
        {
            return da == db;
        }
        return TryGetDouble(a, out var xa) && TryGetDouble(b, out var xb) && xa.Equals(xb);
    }
}
