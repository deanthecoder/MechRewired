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

public enum EnemyCombatMovementMode
{
    Closing,
    Orbiting,
    CreatingSpace,
    Evading,
    Searching
}

/// <summary>
/// Describes a movement vector relative to the target: positive radial closes distance and positive lateral moves right.
/// </summary>
public sealed record EnemyCombatMovementStep(
    EnemyCombatMovementMode Mode,
    double Radial,
    double Lateral,
    double SpeedFraction);

/// <summary>
/// Chooses simple, deterministic BattleMech combat maneuvers without depending on rendering or physics.
/// </summary>
public sealed class EnemyCombatMovement
{
    private const double NormalRangeFactor = 0.56;
    private const double AggressiveRangeFactor = 0.40;
    private const double InnerBandFactor = 0.78;
    private const double OuterBandFactor = 1.22;
    private const double EvasionSeconds = 3.0;
    private const double AccelerationPerSecond = 0.65;
    private const double DecelerationPerSecond = 1.0;

    private readonly double m_weaponRange;
    private int m_strafeDirection;
    private double m_evasionRemaining;
    private double m_speedFraction;

    public EnemyCombatMovement(double weaponRange, int identity)
    {
        if (weaponRange <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(weaponRange));
        }

        m_weaponRange = weaponRange;
        m_strafeDirection = (identity & 1) == 0 ? 1 : -1;
    }

    public void NotifyDamage()
    {
        m_strafeDirection *= -1;
        m_evasionRemaining = EvasionSeconds;
    }

    public void ReverseStrafeDirection() => m_strafeDirection *= -1;

    public EnemyCombatMovementStep Advance(
        double seconds,
        double distance,
        bool hasLineOfSight,
        double targetHealthFraction)
    {
        if (seconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        if (distance < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(distance));
        }

        m_evasionRemaining = Math.Max(0.0, m_evasionRemaining - seconds);
        if (!hasLineOfSight)
        {
            return CreateStep(EnemyCombatMovementMode.Searching, 1.0, 0.0, 0.55, seconds);
        }

        var preferredRange = m_weaponRange *
                             (targetHealthFraction <= 0.25 ? AggressiveRangeFactor : NormalRangeFactor);
        var innerRange = preferredRange * InnerBandFactor;
        var outerRange = preferredRange * OuterBandFactor;
        if (m_evasionRemaining > 0.0)
        {
            var radial = distance < innerRange
                ? -0.18
                : distance > outerRange
                    ? 0.25
                    : 0.0;
            return CreateStep(
                EnemyCombatMovementMode.Evading,
                radial,
                m_strafeDirection,
                0.80,
                seconds);
        }

        if (distance > outerRange)
        {
            return CreateStep(
                EnemyCombatMovementMode.Closing,
                1.0,
                m_strafeDirection * 0.18,
                0.78,
                seconds);
        }

        if (distance < innerRange)
        {
            return CreateStep(
                EnemyCombatMovementMode.CreatingSpace,
                -0.20,
                m_strafeDirection * 0.85,
                0.58,
                seconds);
        }

        var rangeError = (distance - preferredRange) / Math.Max(outerRange - innerRange, 1.0);
        return CreateStep(
            EnemyCombatMovementMode.Orbiting,
            Math.Clamp(rangeError * 0.35, -0.25, 0.25),
            m_strafeDirection * 0.80,
            0.52,
            seconds);
    }

    private EnemyCombatMovementStep CreateStep(
        EnemyCombatMovementMode mode,
        double radial,
        double lateral,
        double targetSpeedFraction,
        double seconds)
    {
        var rate = targetSpeedFraction < m_speedFraction
            ? DecelerationPerSecond
            : AccelerationPerSecond;
        m_speedFraction = MoveTowards(m_speedFraction, targetSpeedFraction, rate * seconds);
        return new EnemyCombatMovementStep(mode, radial, lateral, m_speedFraction);
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
