// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Simulation;

/// <summary>
/// Combines long-range directional sensors with short-range vibration and proximity awareness.
/// </summary>
public static class EnemyAwareness
{
    private const double CloseAwarenessRangeFactor = 0.20;
    private const double MinimumCloseAwarenessRange = 75.0;
    private const double MaximumCloseAwarenessRange = 125.0;

    public static double GetCloseAwarenessRange(double acquisitionRange) =>
        Math.Clamp(
            acquisitionRange * CloseAwarenessRangeFactor,
            MinimumCloseAwarenessRange,
            MaximumCloseAwarenessRange);

    /// <summary>
    /// Resolves the authored proximity at which a dormant game piece wakes its reactor.
    /// </summary>
    /// <remarks>
    /// Sleep range is the only verified wake threshold. Target range is retained as a fallback
    /// for incomplete mission data; terrain visibility and sensor heading do not suppress a wake.
    /// </remarks>
    public static double GetWakeRange(double sleepRange, double targetRange) =>
        sleepRange > 0.0
            ? sleepRange
            : Math.Max(0.0, targetRange);

    public static bool CanWake(double distance, double wakeRange) =>
        wakeRange > 0.0 && distance <= wakeRange;

    /// <summary>
    /// Resolves the distance at which a dormant actor can visually acquire a target.
    /// </summary>
    /// <remarks>
    /// GPS supplies the actor's authored target and sleep radii, while the planet's LITE data
    /// limits what can be visually resolved through the mission atmosphere. Rubberband range is
    /// deliberately excluded: it controls movement containment rather than perception.
    /// </remarks>
    public static double GetVisualAcquisitionRange(
        double targetRange,
        double sleepRange,
        double atmosphericVisibilityRange)
    {
        var authoredRange = targetRange > 0.0
            ? targetRange
            : sleepRange;
        if (authoredRange <= 0.0)
        {
            return Math.Max(0.0, atmosphericVisibilityRange);
        }

        if (sleepRange > 0.0)
        {
            authoredRange = Math.Min(authoredRange, sleepRange);
        }

        return atmosphericVisibilityRange > 0.0
            ? Math.Min(authoredRange, atmosphericVisibilityRange)
            : authoredRange;
    }

    public static bool CanObserve(double distance, double visualRange, bool hasLineOfSight) =>
        hasLineOfSight && distance <= visualRange;

    public static bool CanAcquire(
        double distance,
        double acquisitionRange,
        double forwardAlignment,
        double minimumForwardAlignment,
        bool hasLineOfSight)
    {
        if (!CanObserve(distance, acquisitionRange, hasLineOfSight))
        {
            return false;
        }

        return distance <= GetCloseAwarenessRange(acquisitionRange) ||
               forwardAlignment >= minimumForwardAlignment;
    }
}
