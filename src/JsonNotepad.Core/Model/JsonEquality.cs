namespace JsonNotepad.Core.Model;

/// <summary>
/// Structural equality per JSON Schema semantics: object member order is irrelevant,
/// numbers compare by value (1 == 1.0), everything else compares exactly. Iterative.
/// </summary>
public static class JsonEquality
{
    public static bool DeepEquals(JNode a, JNode b)
    {
        var stack = new Stack<(JNode, JNode)>();
        stack.Push((a, b));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x.Kind != y.Kind)
            {
                return false;
            }
            switch (x.Kind)
            {
                case JNodeKind.Number:
                    if (!JsonNumber.NumericEquals(x.Value, y.Value))
                    {
                        return false;
                    }
                    break;
                case JNodeKind.String:
                case JNodeKind.Boolean:
                    if (!string.Equals(x.Value, y.Value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                    break;
                case JNodeKind.Array:
                    if (x.Count != y.Count)
                    {
                        return false;
                    }
                    for (int i = 0; i < x.Count; i++)
                    {
                        stack.Push((x[i], y[i]));
                    }
                    break;
                case JNodeKind.Object:
                    if (x.Count != y.Count)
                    {
                        return false;
                    }
                    foreach (var member in x.Children)
                    {
                        var other = y.FindProperty(member.Name ?? string.Empty);
                        if (other is null)
                        {
                            return false;
                        }
                        stack.Push((member, other));
                    }
                    break;
            }
        }
        return true;
    }

    /// <summary>Hash consistent with <see cref="DeepEquals"/> (shallow for speed).</summary>
    public static int GetHashCode(JNode node) => node.Kind switch
    {
        JNodeKind.Number => JsonNumber.TryGetDouble(node.Value, out var d) ? d.GetHashCode() : 0,
        JNodeKind.String or JNodeKind.Boolean => HashCode.Combine(node.Kind, node.Value),
        _ => HashCode.Combine(node.Kind, node.Count),
    };
}
