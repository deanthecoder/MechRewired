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
/// Simulates a player mech's jump-jet fuel and vertical motion.
/// </summary>
/// <remarks>
/// Keeping the trajectory independent of Godot makes the observed Wolf-mech timings deterministic
/// while the host remains responsible for terrain contact, input, sound and presentation.
/// </remarks>
public sealed class MechJumpJets
{
    public const double FuelBurnDurationSeconds = 7.0;
    public const double GroundSpoolUpSeconds = 0.75;
    public const double FuelRechargeDurationSeconds = 35.0;
    public const double LowFuelFraction = 0.10;
    public const double GravityMetersPerSecondSquared = 10.4;
    // Preserve the 155m peak with 6.25 seconds of lift after the fuel-consuming ground spool-up.
    public const double ThrustMetersPerSecondSquared = 15.667779;
    public const double WolfMaximumHeightMeters = 155.0;

    private const double GroundToleranceMeters = 0.001;
    private double m_groundSpoolElapsed;

    public double FuelFraction { get; private set; } = 1.0;

    public double VerticalVelocityMetersPerSecond { get; private set; }

    public double MaximumHeightMeters { get; private set; }

    public bool IsAirborne { get; private set; }

    public bool IsLowFuel => FuelFraction < LowFuelFraction;

    public JumpJetStep Advance(double deltaSeconds, bool thrustRequested, double heightAboveGroundMeters)
    {
        if (deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        if (heightAboveGroundMeters < -GroundToleranceMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(heightAboveGroundMeters));
        }

        var height = Math.Max(0.0, heightAboveGroundMeters);
        var wasLowFuel = IsLowFuel;
        var wasAirborne = IsAirborne || height > GroundToleranceMeters;
        if (!thrustRequested)
        {
            m_groundSpoolElapsed = 0.0;
        }

        if (!wasAirborne && !thrustRequested)
        {
            VerticalVelocityMetersPerSecond = 0.0;
            FuelFraction = Math.Min(
                1.0,
                FuelFraction + deltaSeconds / FuelRechargeDurationSeconds);
            return new JumpJetStep(0.0, false, false, false, false, 0.0, 0.0);
        }

        var remainingSeconds = deltaSeconds;
        var displacement = 0.0;
        var integratedHeight = height;
        MaximumHeightMeters = Math.Max(MaximumHeightMeters, height);
        var thrusting = thrustRequested && FuelFraction > 0.0;
        if (thrusting)
        {
            var thrustSeconds = Math.Min(
                remainingSeconds,
                FuelFraction * FuelBurnDurationSeconds);
            var liftSeconds = thrustSeconds;
            if (!wasAirborne)
            {
                // Jets already emit sound and dust during spool-up, but create no vertical motion.
                var spoolSeconds = Math.Min(thrustSeconds, Math.Max(0.0, GroundSpoolUpSeconds - m_groundSpoolElapsed));
                m_groundSpoolElapsed += spoolSeconds;
                liftSeconds -= spoolSeconds;
            }

            var thrustDisplacement = Integrate(
                liftSeconds,
                ThrustMetersPerSecondSquared - GravityMetersPerSecondSquared,
                integratedHeight);
            displacement += thrustDisplacement;
            integratedHeight += thrustDisplacement;
            FuelFraction = Math.Max(0.0, FuelFraction - thrustSeconds / FuelBurnDurationSeconds);
            remainingSeconds -= thrustSeconds;
        }
        else if (!thrustRequested)
        {
            FuelFraction = Math.Min(
                1.0,
                FuelFraction + deltaSeconds / FuelRechargeDurationSeconds);
        }

        if (remainingSeconds > 0.0)
        {
            displacement += Integrate(remainingSeconds, -GravityMetersPerSecondSquared, integratedHeight);
        }

        var resultingHeight = height + displacement;
        var lowFuelWarning = !wasLowFuel && IsLowFuel;
        MaximumHeightMeters = Math.Max(MaximumHeightMeters, resultingHeight);
        if (resultingHeight > GroundToleranceMeters || VerticalVelocityMetersPerSecond > 0.0)
        {
            IsAirborne = true;
            return new JumpJetStep(
                displacement,
                thrusting,
                lowFuelWarning,
                true,
                false,
                0.0,
                MaximumHeightMeters);
        }

        var impactSpeed = wasAirborne ? Math.Max(0.0, -VerticalVelocityMetersPerSecond) : 0.0;
        var maximumHeight = MaximumHeightMeters;
        VerticalVelocityMetersPerSecond = 0.0;
        MaximumHeightMeters = 0.0;
        IsAirborne = false;
        if (wasAirborne)
        {
            m_groundSpoolElapsed = 0.0;
        }

        return new JumpJetStep(
            -height,
            thrusting,
            lowFuelWarning,
            false,
            wasAirborne,
            impactSpeed,
            maximumHeight);
    }

    private double Integrate(double seconds, double acceleration, double startingHeight)
    {
        var startingVelocity = VerticalVelocityMetersPerSecond;
        var displacement = VerticalVelocityMetersPerSecond * seconds +
                           0.5 * acceleration * seconds * seconds;
        var secondsToPeak = acceleration < 0.0 && startingVelocity > 0.0
            ? Math.Min(seconds, startingVelocity / -acceleration)
            : seconds;
        var peakDisplacement = startingVelocity * secondsToPeak +
                               0.5 * acceleration * secondsToPeak * secondsToPeak;
        if (peakDisplacement > 0.0)
        {
            MaximumHeightMeters = Math.Max(MaximumHeightMeters, startingHeight + peakDisplacement);
        }

        VerticalVelocityMetersPerSecond += acceleration * seconds;
        return displacement;
    }
}

public readonly record struct JumpJetStep(
    double VerticalDisplacementMeters,
    bool IsThrusting,
    bool LowFuelWarning,
    bool IsAirborne,
    bool Landed,
    double ImpactSpeedMetersPerSecond,
    double MaximumHeightMeters);
