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
/// Maintains a distance-driven BattleMech gait shared by animation, cockpit motion and footfall audio.
/// </summary>
public sealed class MechGait
{
    public const double CycleDistanceMeters = 18.0;
    public const double MaximumPoseSpeedFraction = 0.4;
    public const double MaximumStrideScale = 1.45;
    public const double PivotGaitRadiusMeters = 8.0;
    public const double PivotPoseWeight = 0.18;

    private const double EngageRate = 4.0;
    private const double SettleRate = 0.6;

    private double m_unwrappedPhase;
    private bool m_wasActive;

    public double Phase
    {
        get
        {
            var phase = m_unwrappedPhase % Math.Tau;
            return phase < 0.0 ? phase + Math.Tau : phase;
        }
    }

    public double Weight { get; private set; }

    /// <summary>
    /// Advances the gait and returns whether either foot has just planted.
    /// </summary>
    public bool Advance(
        double signedDistanceMeters,
        double headingChangeRadians,
        double speedFraction,
        double seconds)
    {
        if (seconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        var moving = Math.Abs(signedDistanceMeters) > 0.0001;
        var pivoting = Math.Abs(headingChangeRadians) > 0.0001;
        var active = moving || pivoting;
        var movementWeight = moving
            ? Math.Min(Math.Abs(speedFraction), MaximumPoseSpeedFraction)
            : 0.0;
        var targetWeight = Math.Max(movementWeight, pivoting ? PivotPoseWeight : 0.0);
        Weight = MoveTowards(
            Weight,
            targetWeight,
            seconds * (targetWeight < Weight ? SettleRate : EngageRate));

        var planted = false;
        if (active)
        {
            // At high speed the mech covers more ground per stride instead of taking implausibly rapid steps.
            var highSpeedFraction = Math.Clamp(
                (Math.Abs(speedFraction) - MaximumPoseSpeedFraction) / (1.0 - MaximumPoseSpeedFraction),
                0.0,
                1.0);
            var strideScale = 1.0 + highSpeedFraction * (MaximumStrideScale - 1.0);
            var movementCycles = signedDistanceMeters / (CycleDistanceMeters * strideScale);
            var pivotCycles = Math.Abs(headingChangeRadians) * PivotGaitRadiusMeters / CycleDistanceMeters;
            var phaseAdvance = moving ? movementCycles * Math.Tau : pivotCycles * Math.Tau;
            var previousHalfCycle = Math.Floor(m_unwrappedPhase / Math.PI);
            m_unwrappedPhase += phaseAdvance;
            planted = !m_wasActive || Math.Floor(m_unwrappedPhase / Math.PI) != previousHalfCycle;
        }

        m_wasActive = active;
        return planted;
    }

    private static double MoveTowards(double current, double target, double maximumChange)
    {
        if (Math.Abs(target - current) <= maximumChange)
        {
            return target;
        }

        return current + Math.Sign(target - current) * maximumChange;
    }
}
