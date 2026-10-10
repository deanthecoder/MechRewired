// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.
using System.Numerics;

namespace MechRewired.Simulation;

/// <summary>Immutable hierarchy of XZ bounds; exact triangle tests remain the caller's responsibility.</summary>
public sealed class TerrainRayBoundsIndex
{
    public readonly record struct Bounds(Vector2 Minimum, Vector2 Maximum);
    private sealed class Node
    {
        public Bounds Bounds;
        public Node Left;
        public Node Right;
        public int Start;
        public int Count;
    }
    private readonly Bounds[] m_bounds;
    private readonly int[] m_order;
    private readonly Node m_root;

    public TerrainRayBoundsIndex(IReadOnlyList<Bounds> bounds)
    {
        m_bounds = bounds.ToArray();
        m_order = Enumerable.Range(0, bounds.Count).ToArray();
        m_root = bounds.Count == 0 ? null : Build(0, bounds.Count);
    }

    /// <summary>Visits conservative candidates. Returning true from visitor stops traversal.</summary>
    public bool Visit(Vector2 origin, Vector2 direction, float maximumParameter, Func<int, bool> visitor) =>
        m_root != null && Visit(m_root, new Vector3(origin.X, 0, origin.Y), new Vector3(direction.X, 0, direction.Y), maximumParameter, visitor);

    private bool Visit(Node node, Vector3 origin, Vector3 direction, float maximumParameter, Func<int, bool> visitor)
    {
        if (!BoundedMeshRay.IntersectsBounds(origin, direction,
                new Vector3(node.Bounds.Minimum.X, -1, node.Bounds.Minimum.Y),
                new Vector3(node.Bounds.Maximum.X, 1, node.Bounds.Maximum.Y), maximumParameter))
            return false;
        if (node.Left != null)
            return Visit(node.Left, origin, direction, maximumParameter, visitor) ||
                   Visit(node.Right, origin, direction, maximumParameter, visitor);
        for (var i = node.Start; i < node.Start + node.Count; i++)
            if (visitor(m_order[i])) return true;
        return false;
    }

    private Node Build(int start, int count)
    {
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        for (var i = start; i < start + count; i++)
        {
            minimum = Vector2.Min(minimum, m_bounds[m_order[i]].Minimum);
            maximum = Vector2.Max(maximum, m_bounds[m_order[i]].Maximum);
        }
        var node = new Node {Bounds = new Bounds(minimum, maximum), Start = start, Count = count};
        if (count <= 32) return node;
        var x = maximum.X - minimum.X >= maximum.Y - minimum.Y;
        Array.Sort(m_order, start, count, Comparer<int>.Create((a, b) =>
        {
            var first = m_bounds[a].Minimum + m_bounds[a].Maximum;
            var second = m_bounds[b].Minimum + m_bounds[b].Maximum;
            return (x ? first.X : first.Y).CompareTo(x ? second.X : second.Y);
        }));
        var half = count / 2;
        node.Left = Build(start, half);
        node.Right = Build(start + half, count - half);
        return node;
    }
}
