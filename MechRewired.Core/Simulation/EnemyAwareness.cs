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

    public static bool CanAcquire(
        double distance,
        double acquisitionRange,
        double forwardAlignment,
        double minimumForwardAlignment,
        bool hasLineOfSight)
    {
        if (!hasLineOfSight || distance > acquisitionRange)
        {
            return false;
        }

        return distance <= GetCloseAwarenessRange(acquisitionRange) ||
               forwardAlignment >= minimumForwardAlignment;
    }
}
