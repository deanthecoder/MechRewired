// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>
/// Performs CPU-side ray tests against the triangles used by the debug tools.
/// </summary>
public static class DebugTriangleRaycaster
{
    public static bool TryFindNearest(
        IEnumerable<DebugTriangle> triangles,
        Vector3 origin,
        Vector3 direction,
        out DebugTriangle nearestTriangle,
        out float nearestDistance)
    {
        nearestTriangle = null;
        nearestDistance = float.PositiveInfinity;
        foreach (var triangle in triangles)
        {
            if (TryIntersectRay(origin, direction, triangle, out var distance) && distance < nearestDistance)
            {
                nearestTriangle = triangle;
                nearestDistance = distance;
            }
        }

        return nearestTriangle != null;
    }

    private static bool TryIntersectRay(
        Vector3 origin,
        Vector3 direction,
        DebugTriangle triangle,
        out float distance)
    {
        const float epsilon = 0.000001f;
        var edge1 = triangle.B - triangle.A;
        var edge2 = triangle.C - triangle.A;
        var perpendicular = direction.Cross(edge2);
        var determinant = edge1.Dot(perpendicular);
        if (Mathf.Abs(determinant) < epsilon)
        {
            distance = 0.0f;
            return false;
        }

        var inverseDeterminant = 1.0f / determinant;
        var originOffset = origin - triangle.A;
        var u = originOffset.Dot(perpendicular) * inverseDeterminant;
        if (u is < 0.0f or > 1.0f)
        {
            distance = 0.0f;
            return false;
        }

        var cross = originOffset.Cross(edge1);
        var v = direction.Dot(cross) * inverseDeterminant;
        if (v < 0.0f || u + v > 1.0f)
        {
            distance = 0.0f;
            return false;
        }

        distance = edge2.Dot(cross) * inverseDeterminant;
        return distance >= 0.0f;
    }
}
