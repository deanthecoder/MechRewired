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

namespace MechRewired.Resources;

/// <summary>Samples a time-authored path with continuous velocity and stationary endpoints.</summary>
public static class MechWarriorWorldPathInterpolator
{
    private const float MinimumSegmentSeconds = 0.001f;

    public static MechWarriorWorldPathSample Sample(
        IReadOnlyList<MechWarriorWorldPathPoint> points,
        int segmentIndex,
        float segmentElapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 2)
        {
            throw new ArgumentException("A sampled path must contain at least two points.", nameof(points));
        }

        if (segmentIndex < 0 || segmentIndex >= points.Count - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        }

        var duration = GetDuration(points[segmentIndex]);
        var weight = Math.Clamp(segmentElapsedSeconds / duration, 0.0f, 1.0f);
        var weightSquared = weight * weight;
        var weightCubed = weightSquared * weight;
        var from = points[segmentIndex].Position;
        var to = points[segmentIndex + 1].Position;
        var fromVelocity = GetPointVelocity(points, segmentIndex);
        var toVelocity = GetPointVelocity(points, segmentIndex + 1);

        var fromPositionWeight = 2.0f * weightCubed - 3.0f * weightSquared + 1.0f;
        var fromVelocityWeight = weightCubed - 2.0f * weightSquared + weight;
        var toPositionWeight = -2.0f * weightCubed + 3.0f * weightSquared;
        var toVelocityWeight = weightCubed - weightSquared;
        var position = from * fromPositionWeight +
                       fromVelocity * (fromVelocityWeight * duration) +
                       to * toPositionWeight +
                       toVelocity * (toVelocityWeight * duration);

        var fromPositionDerivative = 6.0f * weightSquared - 6.0f * weight;
        var fromVelocityDerivative = 3.0f * weightSquared - 4.0f * weight + 1.0f;
        var toPositionDerivative = -fromPositionDerivative;
        var toVelocityDerivative = 3.0f * weightSquared - 2.0f * weight;
        var velocity = (from * fromPositionDerivative +
                        fromVelocity * (fromVelocityDerivative * duration) +
                        to * toPositionDerivative +
                        toVelocity * (toVelocityDerivative * duration)) / duration;
        return new MechWarriorWorldPathSample(position, velocity);
    }

    private static Vector3 GetPointVelocity(
        IReadOnlyList<MechWarriorWorldPathPoint> points,
        int pointIndex)
    {
        if (pointIndex == 0 || pointIndex == points.Count - 1)
        {
            return Vector3.Zero;
        }

        var previousDuration = GetDuration(points[pointIndex - 1]);
        var nextDuration = GetDuration(points[pointIndex]);
        var previousVelocity =
            (points[pointIndex].Position - points[pointIndex - 1].Position) / previousDuration;
        var nextVelocity =
            (points[pointIndex + 1].Position - points[pointIndex].Position) / nextDuration;
        return (previousVelocity * nextDuration + nextVelocity * previousDuration) /
               (previousDuration + nextDuration);
    }

    private static float GetDuration(MechWarriorWorldPathPoint point) =>
        Math.Max(point.TravelSeconds, MinimumSegmentSeconds);
}
