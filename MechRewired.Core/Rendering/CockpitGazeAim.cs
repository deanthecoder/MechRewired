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

namespace MechRewired.Rendering;

/// <summary>Projects head aim onto the HUD and keeps the reticle inside the visible windshield.</summary>
public static class CockpitGazeAim
{
    private const float Epsilon = 0.00001f;
    private const int MaximumVertices = 32;

    /// <summary>
    /// All inputs use HUD-local coordinates: its plane is Z=0 and the eye lies at positive Z.
    /// Windshield vertices describe a convex opening, not surrounding glass or side windows.
    /// The inset is a HUD-plane distance, applied to both the glass outline and HUD rectangle.
    /// Returns false when no safe projection exists; callers should suppress the reticle and firing.
    /// </summary>
    public static bool TryGetAimPoint(Vector3 eye, Vector3 headForward,
        ReadOnlySpan<Vector3> windshieldVertices, Vector2 hudHalfSize, float inset, out Vector2 aimPoint)
        => TryGetAimPoint(eye, headForward, windshieldVertices, hudHalfSize, inset, out aimPoint, out _);

    /// <summary>Projects head aim and returns smooth signed edge-turn drive within the visible opening.</summary>
    public static bool TryGetAimPoint(Vector3 eye, Vector3 headForward,
        ReadOnlySpan<Vector3> windshieldVertices, Vector2 hudHalfSize, float inset,
        out Vector2 aimPoint, out Vector2 edgeTurn)
    {
        aimPoint = default;
        edgeTurn = default;
        if (!IsFinite(eye) || !IsFinite(headForward) || eye.Z <= Epsilon ||
            !float.IsFinite(inset) || inset < 0 ||
            !float.IsFinite(hudHalfSize.X) || !float.IsFinite(hudHalfSize.Y) ||
            hudHalfSize.X <= inset || hudHalfSize.Y <= inset ||
            windshieldVertices.Length is < 3 or > MaximumVertices || headForward.LengthSquared() < Epsilon * Epsilon)
            return false;

        // Looking directly backwards has no meaningful left/right boundary to select.
        if (headForward.Z >= 0 && Math.Abs(headForward.X) + Math.Abs(headForward.Y) < Epsilon)
            return false;

        Span<Vector2> points = stackalloc Vector2[MaximumVertices];
        var eye2 = new Vector2(eye.X, eye.Y);
        for (var i = 0; i < windshieldVertices.Length; i++)
        {
            var vertex = windshieldVertices[i];
            if (!IsFinite(vertex) || eye.Z - vertex.Z <= Epsilon)
                return false;
            points[i] = eye2 + (new Vector2(vertex.X, vertex.Y) - eye2) * (eye.Z / (eye.Z - vertex.Z));
            if (!float.IsFinite(points[i].X) || !float.IsFinite(points[i].Y))
                return false;
            // Small fixed vertex counts make insertion sorting cheaper than allocating a collection.
            for (var j = i; j > 0 && ComesBefore(points[j], points[j - 1]); j--)
                (points[j - 1], points[j]) = (points[j], points[j - 1]);
        }

        Span<Vector2> hull = stackalloc Vector2[MaximumVertices * 2];
        var hullCount = 0;
        for (var i = 0; i < windshieldVertices.Length; i++)
        {
            while (hullCount >= 2 && Cross(hull[hullCount - 1] - hull[hullCount - 2], points[i] - hull[hullCount - 1]) <= Epsilon)
                hullCount--;
            hull[hullCount++] = points[i];
        }
        var lowerCount = hullCount;
        for (var i = windshieldVertices.Length - 2; i >= 0; i--)
        {
            while (hullCount > lowerCount && Cross(hull[hullCount - 1] - hull[hullCount - 2], points[i] - hull[hullCount - 1]) <= Epsilon)
                hullCount--;
            hull[hullCount++] = points[i];
        }
        if (--hullCount < 3)
            return false;

        Span<Vector2> polygon = stackalloc Vector2[MaximumVertices + 4];
        Span<Vector2> clipped = stackalloc Vector2[MaximumVertices + 4];
        var half = hudHalfSize - new Vector2(inset);
        polygon[0] = new Vector2(-half.X, -half.Y);
        polygon[1] = new Vector2(half.X, -half.Y);
        polygon[2] = new Vector2(half.X, half.Y);
        polygon[3] = new Vector2(-half.X, half.Y);
        var count = 4;
        for (var edgeIndex = 0; edgeIndex < hullCount; edgeIndex++)
        {
            var start = hull[edgeIndex];
            var edge = hull[(edgeIndex + 1) % hullCount] - start;
            var offset = inset * edge.Length();
            var outputCount = 0;
            var previous = polygon[count - 1];
            var previousDistance = Cross(edge, previous - start) - offset;
            for (var i = 0; i < count; i++)
            {
                var current = polygon[i];
                var distance = Cross(edge, current - start) - offset;
                if ((distance >= 0) != (previousDistance >= 0))
                    clipped[outputCount++] = Vector2.Lerp(previous, current, previousDistance / (previousDistance - distance));
                if (distance >= 0)
                    clipped[outputCount++] = current;
                previous = current;
                previousDistance = distance;
            }
            count = outputCount;
            if (count < 3)
                return false;
            clipped[..count].CopyTo(polygon);
        }

        var area = 0.0f;
        for (var i = 0; i < count; i++)
            area += Cross(polygon[i], polygon[(i + 1) % count]);
        if (area <= Epsilon)
            return false;

        var direction = Vector3.Normalize(headForward);
        var target = eye2 + new Vector2(direction.X, direction.Y) * (eye.Z / Math.Max(-direction.Z, Epsilon));
        target = Vector2.Clamp(target, new Vector2(-1000000), new Vector2(1000000));
        var inside = true;
        var nearestDistance = double.PositiveInfinity;
        for (var i = 0; i < count; i++)
        {
            var start = polygon[i];
            var edge = polygon[(i + 1) % count] - start;
            inside &= Cross(edge, target - start) >= 0;
            var edgeLengthSquared = edge.LengthSquared();
            var fraction = edgeLengthSquared > Epsilon * Epsilon
                ? Math.Clamp(Vector2.Dot(target - start, edge) / edgeLengthSquared, 0, 1) : 0;
            var closest = start + edge * fraction;
            var dx = (double)closest.X - target.X;
            var dy = (double)closest.Y - target.Y;
            var distance = dx * dx + dy * dy;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                aimPoint = closest;
            }
        }
        if (inside)
            aimPoint = target;
        edgeTurn = new Vector2(
            GetEdgeDrive(polygon[..count], aimPoint, horizontal: true),
            GetEdgeDrive(polygon[..count], aimPoint, horizontal: false));
        return true;
    }

    private static float GetEdgeDrive(ReadOnlySpan<Vector2> polygon, Vector2 point, bool horizontal)
    {
        var coordinate = horizontal ? point.Y : point.X;
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            var aAxis = horizontal ? a.Y : a.X;
            var bAxis = horizontal ? b.Y : b.X;
            var aValue = horizontal ? a.X : a.Y;
            var bValue = horizontal ? b.X : b.Y;
            if (Math.Abs(aAxis - bAxis) <= Epsilon)
            {
                if (Math.Abs(coordinate - aAxis) <= Epsilon)
                {
                    minimum = Math.Min(minimum, Math.Min(aValue, bValue));
                    maximum = Math.Max(maximum, Math.Max(aValue, bValue));
                }
                continue;
            }
            if (coordinate < Math.Min(aAxis, bAxis) - Epsilon || coordinate > Math.Max(aAxis, bAxis) + Epsilon)
                continue;
            var fraction = Math.Clamp((coordinate - aAxis) / (bAxis - aAxis), 0, 1);
            var value = aValue + (bValue - aValue) * fraction;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        var halfRange = (maximum - minimum) * 0.5f;
        if (!float.IsFinite(halfRange) || halfRange <= Epsilon)
            return 0;
        var normalized = Math.Clamp((horizontal ? point.X : point.Y) - (minimum + maximum) * 0.5f, -halfRange, halfRange) / halfRange;
        var magnitude = Math.Abs(normalized);
        if (magnitude <= 0.75f)
            return 0;
        var ramp = Math.Clamp((magnitude - 0.75f) / 0.25f, 0, 1);
        var smooth = ramp * ramp * (3 - 2 * ramp);
        return MathF.CopySign(smooth, normalized);
    }

    private static bool ComesBefore(Vector2 a, Vector2 b) => a.X < b.X || (a.X == b.X && a.Y < b.Y);
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
