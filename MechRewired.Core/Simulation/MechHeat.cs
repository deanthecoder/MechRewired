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
/// Tracks a mech's generated heat and passive heat-sink cooling independently of presentation or shutdown rules.
/// </summary>
public sealed class MechHeat
{
    /// <summary>
    /// The fixed heat cushion present before a mech's effective heat sinks are
    /// added to its critical threshold.
    /// </summary>
    public const double BaseCriticalHeat = 30.0;

    private const double HeatRateResponsePerSecond = 4.0;

    private double m_generatedSinceLastAdvance;

    public MechHeat(double maximumHeat, double coolingPerSecond)
    {
        if (maximumHeat <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumHeat), maximumHeat, "Maximum heat must be positive.");
        }

        if (coolingPerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(coolingPerSecond), coolingPerSecond, "Cooling must be positive.");
        }

        MaximumHeat = maximumHeat;
        CoolingPerSecond = coolingPerSecond;
    }

    public double MaximumHeat { get; }

    public double CoolingPerSecond { get; }

    public double CurrentHeat { get; private set; }

    /// <summary>
    /// A short-smoothed net heat-flow rate for the cockpit's dH/dT gauge.
    /// </summary>
    public double HeatRate { get; private set; }

    public double Fraction => CurrentHeat / MaximumHeat;

    public static double GetCriticalHeatThreshold(int effectiveHeatSinkCount)
    {
        if (effectiveHeatSinkCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveHeatSinkCount),
                effectiveHeatSinkCount,
                "The effective heat-sink count cannot be negative.");
        }

        return BaseCriticalHeat + effectiveHeatSinkCount;
    }

    public void Add(double heat)
    {
        if (heat < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(heat), heat, "Generated heat cannot be negative.");
        }

        CurrentHeat = Math.Min(MaximumHeat, CurrentHeat + heat);
        m_generatedSinceLastAdvance += heat;
    }

    public void Advance(double seconds)
    {
        if (seconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Elapsed time cannot be negative.");
        }

        if (seconds == 0.0)
        {
            return;
        }

        var cooled = Math.Min(CurrentHeat, CoolingPerSecond * seconds);
        CurrentHeat -= cooled;
        var instantaneousRate = (m_generatedSinceLastAdvance - cooled) / seconds;
        var response = 1.0 - Math.Exp(-HeatRateResponsePerSecond * seconds);
        HeatRate += (instantaneousRate - HeatRate) * response;
        m_generatedSinceLastAdvance = 0.0;
    }
}
