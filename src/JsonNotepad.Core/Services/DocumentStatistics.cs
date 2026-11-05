using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Services;

/// <summary>Summary numbers about a document (the JSON counterpart of XML Notepad's XmlStats).</summary>
public sealed record DocumentStatistics
{
    public long TotalNodes { get; init; }

    public long Objects { get; init; }

    public long Arrays { get; init; }

    public long Strings { get; init; }

    public long Numbers { get; init; }

    public long Booleans { get; init; }

    public long Nulls { get; init; }

    public int MaxDepth { get; init; }

    public int LargestArray { get; init; }

    public int LargestObject { get; init; }

    public long TotalStringChars { get; init; }

    public int LongestString { get; init; }

    public int DistinctKeys { get; init; }

    public IReadOnlyList<KeyValuePair<string, long>> TopKeys { get; init; } = Array.Empty<KeyValuePair<string, long>>();

    /// <summary>Computes statistics for a snapshot. CPU-bound; call on a worker thread.</summary>
    public static DocumentStatistics Compute(JNode root, CancellationToken cancellationToken)
    {
        long total = 0, objects = 0, arrays = 0, strings = 0, numbers = 0, booleans = 0, nulls = 0, chars = 0;
        int maxDepth = 0, largestArray = 0, largestObject = 0, longest = 0;
        var keys = new Dictionary<string, long>(StringComparer.Ordinal);

        var stack = new Stack<(JNode Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            total++;
            if ((total & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            maxDepth = Math.Max(maxDepth, depth);
            if (node.Name is { } name && keys.Count < 1_000_000)
            {
                keys[name] = keys.TryGetValue(name, out var c) ? c + 1 : 1;
            }
            switch (node.Kind)
            {
                case JNodeKind.Object:
                    objects++;
                    largestObject = Math.Max(largestObject, node.Count);
                    break;
                case JNodeKind.Array:
                    arrays++;
                    largestArray = Math.Max(largestArray, node.Count);
                    break;
                case JNodeKind.String:
                    strings++;
                    int len = node.Value?.Length ?? 0;
                    chars += len;
                    longest = Math.Max(longest, len);
                    break;
                case JNodeKind.Number:
                    numbers++;
                    break;
                case JNodeKind.Boolean:
                    booleans++;
                    break;
                default:
                    nulls++;
                    break;
            }
            for (int i = node.Count - 1; i >= 0; i--)
            {
                stack.Push((node[i], depth + 1));
            }
        }

        return new DocumentStatistics
        {
            TotalNodes = total,
            Objects = objects,
            Arrays = arrays,
            Strings = strings,
            Numbers = numbers,
            Booleans = booleans,
            Nulls = nulls,
            MaxDepth = maxDepth,
            LargestArray = largestArray,
            LargestObject = largestObject,
            TotalStringChars = chars,
            LongestString = longest,
            DistinctKeys = keys.Count,
            TopKeys = keys.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(15).ToList(),
        };
    }
}
