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

/// <summary>
/// Tests occluders along a finite sight line, rejecting triangles outside its bounds first.
/// </summary>
/// <remarks>
/// Bounds cover every vertex, rather than a triangle's centre, so large walls crossing the
/// sight line are retained. Callers supply current vertices; moving scenery requires no cache rebuild.
/// </remarks>
public readonly struct TriangleSightSegment
{
    private readonly Vector3 m_origin;
    private readonly Vector3 m_direction;
    private readonly Vector3 m_minimum;
    private readonly Vector3 m_maximum;
    private readonly float m_maximumDistance;

    public TriangleSightSegment(Vector3 start, Vector3 end, float targetMargin)
    {
        var offset = end - start;
        var distance = offset.Length();
        m_origin = start;
        m_direction = distance > 0.0f ? offset / distance : Vector3.Zero;
        m_maximumDistance = distance - Math.Max(targetMargin, 0.0f);
        // Full endpoints are conservative: the exact test excludes the target margin.
        m_minimum = Vector3.Min(start, end) - new Vector3(0.00001f);
        m_maximum = Vector3.Max(start, end) + new Vector3(0.00001f);
    }

    public bool HasLength => m_maximumDistance > 0.0f;

    public bool IsBlockedBy(Vector3 a, Vector3 b, Vector3 c)
    {
        if (!HasLength ||
            (a.X < m_minimum.X && b.X < m_minimum.X && c.X < m_minimum.X) ||
            (a.X > m_maximum.X && b.X > m_maximum.X && c.X > m_maximum.X) ||
            (a.Y < m_minimum.Y && b.Y < m_minimum.Y && c.Y < m_minimum.Y) ||
            (a.Y > m_maximum.Y && b.Y > m_maximum.Y && c.Y > m_maximum.Y) ||
            (a.Z < m_minimum.Z && b.Z < m_minimum.Z && c.Z < m_minimum.Z) ||
            (a.Z > m_maximum.Z && b.Z > m_maximum.Z && c.Z > m_maximum.Z))
        {
            return false;
        }

        const float epsilon = 0.000001f;
        var edge1 = b - a;
        var edge2 = c - a;
        var perpendicular = Vector3.Cross(m_direction, edge2);
        var determinant = Vector3.Dot(edge1, perpendicular);
        if (MathF.Abs(determinant) < epsilon)
        {
            return false;
        }

        var inverseDeterminant = 1.0f / determinant;
        var originOffset = m_origin - a;
        var u = Vector3.Dot(originOffset, perpendicular) * inverseDeterminant;
        if (u is < 0.0f or > 1.0f)
        {
            return false;
        }

        var cross = Vector3.Cross(originOffset, edge1);
        var v = Vector3.Dot(m_direction, cross) * inverseDeterminant;
        if (v < 0.0f || u + v > 1.0f)
        {
            return false;
        }

        var distance = Vector3.Dot(edge2, cross) * inverseDeterminant;
        return distance >= 0.0f && distance < m_maximumDistance;
    }
}
