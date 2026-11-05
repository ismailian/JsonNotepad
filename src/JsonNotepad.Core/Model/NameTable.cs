using System.Text;

namespace JsonNotepad.Core.Model;

/// <summary>
/// Atomizes property names while parsing — the JSON equivalent of
/// <c>System.Xml.NameTable</c> that XML Notepad relies on.
/// </summary>
/// <remarks>
/// An array of 1,000,000 objects that each have the keys "id", "name" and "email"
/// would otherwise allocate 3,000,000 strings; with the table it allocates three.
/// Lookups are performed on a UTF-16 span via .NET 9 alternate-key lookup so no
/// string is allocated for names already in the table. The table is bounded so
/// documents with unbounded unique keys (e.g. maps keyed by GUID) do not grow it forever.
/// </remarks>
internal sealed class NameTable
{
    private const int MaxEntries = 200_000;
    private const int MaxNameLength = 128;

    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public NameTable()
    {
        _lookup = _names.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public int Count => _names.Count;

    public string Get(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaxNameLength * 3)
        {
            return Encoding.UTF8.GetString(utf8);
        }

        Span<char> buffer = stackalloc char[utf8.Length];
        int len = Encoding.UTF8.GetChars(utf8, buffer);
        return Get(buffer[..len]);
    }

    public string Get(ReadOnlySpan<char> chars)
    {
        if (_lookup.TryGetValue(chars, out var existing))
        {
            return existing;
        }
        var s = chars.ToString();
        if (chars.Length <= MaxNameLength && _names.Count < MaxEntries)
        {
            _names[s] = s;
        }
        return s;
    }

    public string Get(string s)
    {
        if (_names.TryGetValue(s, out var existing))
        {
            return existing;
        }
        if (s.Length <= MaxNameLength && _names.Count < MaxEntries)
        {
            _names[s] = s;
        }
        return s;
    }
}
