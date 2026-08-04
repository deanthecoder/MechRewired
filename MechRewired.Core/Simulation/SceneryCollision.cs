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
/// An axis-aligned horizontal footprint that blocks a walking mech.
/// </summary>
/// <remarks>
/// World geometry is already decoded for rendering, so this intentionally stays a small CPU-side
/// broad-phase rather than duplicating every original polygon as a physics body.
/// </remarks>
public sealed record SceneryWallTriangle(Vector2 A, Vector2 B, Vector2 C);

public sealed record SceneryObstacle(
    string Name,
    Vector2 Minimum,
    Vector2 Maximum,
    IReadOnlyList<SceneryWallTriangle> Walls = null)
{
    public IReadOnlyList<SceneryWallTriangle> Walls { get; init; } = Walls ?? Array.Empty<SceneryWallTriangle>();

    public bool Contains(Vector2 position) =>
        position.X >= Minimum.X && position.X <= Maximum.X &&
        position.Y >= Minimum.Y && position.Y <= Maximum.Y;
}

/// <summary>
/// Tests a mech's horizontal travel segment against expanded scenery footprints.
/// </summary>
public static class SceneryCollision
{
    private const float SeparationEpsilon = 0.05f;

    public static bool TryResolveOverlap(
        Vector2 position,
        float mechRadius,
        IEnumerable<SceneryObstacle> obstacles,
        out Vector2 resolvedPosition,
        out SceneryObstacle obstacle)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mechRadius);
        ArgumentNullException.ThrowIfNull(obstacles);

        resolvedPosition = position;
        obstacle = null;
        foreach (var candidate in obstacles)
        {
            if (candidate.Walls.Count > 0)
            {
                continue;
            }

            var minimum = candidate.Minimum - new Vector2(mechRadius);
            var maximum = candidate.Maximum + new Vector2(mechRadius);
            if (!Contains(resolvedPosition, minimum, maximum))
            {
                continue;
            }

            var possiblePositions = new[]
            {
                new Vector2(minimum.X - SeparationEpsilon, resolvedPosition.Y),
                new Vector2(maximum.X + SeparationEpsilon, resolvedPosition.Y),
                new Vector2(resolvedPosition.X, minimum.Y - SeparationEpsilon),
                new Vector2(resolvedPosition.X, maximum.Y + SeparationEpsilon)
            };
            var currentPosition = resolvedPosition;
            resolvedPosition = possiblePositions.MinBy(possible =>
                Vector2.DistanceSquared(currentPosition, possible));
            obstacle ??= candidate;
        }

        return obstacle != null;
    }

    public static bool TryFindBlockingObstacle(
        Vector2 start,
        Vector2 end,
        float mechRadius,
        IEnumerable<SceneryObstacle> obstacles,
        out SceneryObstacle obstacle)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mechRadius);
        ArgumentNullException.ThrowIfNull(obstacles);

        obstacle = null;
        foreach (var candidate in obstacles)
        {
            var minimum = candidate.Minimum - new Vector2(mechRadius);
            var maximum = candidate.Maximum + new Vector2(mechRadius);
            if (candidate.Walls.Count > 0)
            {
                if (IntersectsSegment(start, end, minimum, maximum) &&
                    IsBlockedByWalls(start, end, mechRadius, candidate.Walls))
                {
                    obstacle = candidate;
                    return true;
                }

                continue;
            }

            if (Contains(start, minimum, maximum))
            {
                // Permit a legacy/deployment overlap to move toward the nearest edge, but not deeper inside.
                if (!Contains(end, minimum, maximum) ||
                    DistanceToNearestEdge(end, minimum, maximum) <
                    DistanceToNearestEdge(start, minimum, maximum))
                {
                    continue;
                }

                obstacle = candidate;
                return true;
            }

            if (!IntersectsSegment(start, end, minimum, maximum))
            {
                continue;
            }

            obstacle = candidate;
            return true;
        }

        return false;
    }

    private static bool IsBlockedByWalls(
        Vector2 start,
        Vector2 end,
        float radius,
        IEnumerable<SceneryWallTriangle> walls)
    {
        const float tolerance = 0.0001f;
        var edges = walls
            .SelectMany(wall => new[]
            {
                (Start: wall.A, End: wall.B),
                (Start: wall.B, End: wall.C),
                (Start: wall.C, End: wall.A)
            })
            .ToArray();
        var startDistance = edges.Min(edge => DistanceToSegment(start, edge.Start, edge.End));
        var endDistance = edges.Min(edge => DistanceToSegment(end, edge.Start, edge.End));
        if (startDistance <= radius + tolerance)
        {
            // When a legacy placement or a wide mech footprint already overlaps a corner,
            // permit movement that maintains or increases clearance from the obstacle as a whole.
            return endDistance + tolerance < startDistance;
        }

        return edges.Any(edge =>
            DistanceBetweenSegments(start, end, edge.Start, edge.End) <= radius + tolerance);
    }

    private static float DistanceBetweenSegments(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        if (SegmentsIntersect(a, b, c, d))
        {
            return 0.0f;
        }

        return MathF.Min(
            MathF.Min(DistanceToSegment(a, c, d), DistanceToSegment(b, c, d)),
            MathF.Min(DistanceToSegment(c, a, b), DistanceToSegment(d, a, b)));
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            return Vector2.Distance(point, start);
        }

        var amount = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0.0f, 1.0f);
        return Vector2.Distance(point, start + segment * amount);
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        const float tolerance = 0.000001f;
        var firstDirection = b - a;
        var secondDirection = d - c;
        var denominator = Cross(firstDirection, secondDirection);
        var offset = c - a;
        if (MathF.Abs(denominator) <= tolerance)
        {
            if (MathF.Abs(Cross(offset, firstDirection)) > tolerance)
            {
                return false;
            }

            var firstLengthSquared = firstDirection.LengthSquared();
            if (firstLengthSquared <= tolerance)
            {
                return Vector2.DistanceSquared(a, c) <= tolerance ||
                       Vector2.DistanceSquared(a, d) <= tolerance;
            }

            var firstAmount = Vector2.Dot(c - a, firstDirection) / firstLengthSquared;
            var secondAmount = Vector2.Dot(d - a, firstDirection) / firstLengthSquared;
            return Math.Max(Math.Min(firstAmount, secondAmount), 0.0f) <=
                   Math.Min(Math.Max(firstAmount, secondAmount), 1.0f);
        }

        var travelAmount = Cross(offset, secondDirection) / denominator;
        var edgeAmount = Cross(offset, firstDirection) / denominator;
        return travelAmount is >= 0.0f and <= 1.0f && edgeAmount is >= 0.0f and <= 1.0f;
    }

    private static float Cross(Vector2 left, Vector2 right) =>
        left.X * right.Y - left.Y * right.X;

    private static bool Contains(Vector2 position, Vector2 minimum, Vector2 maximum) =>
        position.X >= minimum.X && position.X <= maximum.X &&
        position.Y >= minimum.Y && position.Y <= maximum.Y;

    private static float DistanceToNearestEdge(Vector2 position, Vector2 minimum, Vector2 maximum) =>
        MathF.Min(
            MathF.Min(position.X - minimum.X, maximum.X - position.X),
            MathF.Min(position.Y - minimum.Y, maximum.Y - position.Y));

    private static bool IntersectsSegment(Vector2 start, Vector2 end, Vector2 minimum, Vector2 maximum)
    {
        var direction = end - start;
        var enter = 0.0f;
        var exit = 1.0f;
        return IntersectsAxis(start.X, direction.X, minimum.X, maximum.X, ref enter, ref exit) &&
               IntersectsAxis(start.Y, direction.Y, minimum.Y, maximum.Y, ref enter, ref exit);
    }

    private static bool IntersectsAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float enter,
        ref float exit)
    {
        if (MathF.Abs(direction) < 0.000001f)
        {
            return origin >= minimum && origin <= maximum;
        }

        var first = (minimum - origin) / direction;
        var second = (maximum - origin) / direction;
        if (first > second)
        {
            (first, second) = (second, first);
        }

        enter = MathF.Max(enter, first);
        exit = MathF.Min(exit, second);
        return enter <= exit;
    }
}
