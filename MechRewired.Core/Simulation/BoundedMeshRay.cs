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

/// <summary>Conservative ray bounds and exact triangle tests using an unnormalized ray parameter.</summary>
/// <remarks>Keep the transformed direction unnormalized so a local-space parameter remains a world-space distance.</remarks>
public static class BoundedMeshRay
{
    public static bool IntersectsBounds(Vector3 origin, Vector3 direction, Vector3 minimum, Vector3 maximum, float maximumParameter)
    {
        var near = 0.0f;
        var far = maximumParameter;
        return Clip(origin.X, direction.X, minimum.X, maximum.X, ref near, ref far) &&
               Clip(origin.Y, direction.Y, minimum.Y, maximum.Y, ref near, ref far) &&
               Clip(origin.Z, direction.Z, minimum.Z, maximum.Z, ref near, ref far);
    }

    private static bool Clip(float origin, float direction, float minimum, float maximum, ref float near, ref float far)
    {
        if (direction == 0.0f)
            return origin >= minimum && origin <= maximum;
        var a = (minimum - origin) / direction;
        var b = (maximum - origin) / direction;
        near = MathF.Max(near, MathF.Min(a, b));
        far = MathF.Min(far, MathF.Max(a, b));
        return near <= far;
    }

    public static bool TryIntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
        out float parameter, float determinantEpsilon = 0.000001f)
    {
        parameter = 0.0f;
        var edge1 = b - a;
        var edge2 = c - a;
        var perpendicular = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, perpendicular);
        if (MathF.Abs(determinant) < determinantEpsilon)
            return false;
        var inverseDeterminant = 1.0f / determinant;
        var offset = origin - a;
        var u = Vector3.Dot(offset, perpendicular) * inverseDeterminant;
        if (u is < 0.0f or > 1.0f)
            return false;
        var cross = Vector3.Cross(offset, edge1);
        var v = Vector3.Dot(direction, cross) * inverseDeterminant;
        if (v < 0.0f || u + v > 1.0f)
            return false;
        parameter = Vector3.Dot(edge2, cross) * inverseDeterminant;
        return parameter >= 0.0f;
    }
}
