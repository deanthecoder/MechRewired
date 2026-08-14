// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Extensions;

namespace MechRewired.Simulation;

/// <summary>
/// Simulates MW2's latched throttle, direction and speed-dependent leg steering.
/// </summary>
public sealed class MechDrive
{
    public MechDrive(MechDriveProfile profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        if (profile.MaximumForwardSpeedKph <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Maximum forward speed must be positive.");
        }
    }

    public MechDriveProfile Profile { get; }

    public int ThrottleKey { get; private set; } = 1;

    public int ThrottlePercent => ThrottleKey switch
    {
        0 => 100,
        1 => 0,
        _ => ThrottleKey * 10
    };

    public bool IsReversing { get; private set; }

    public double CurrentSpeedKph { get; private set; }

    public double TargetSpeedKph
    {
        get
        {
            var directionMultiplier = IsReversing ? -Profile.ReverseSpeedFactor : 1.0;
            return Profile.MaximumForwardSpeedKph * ThrottlePercent / 100.0 * directionMultiplier;
        }
    }

    public double SpeedFraction =>
        Math.Clamp(Math.Abs(CurrentSpeedKph) / Profile.MaximumForwardSpeedKph, 0.0, 1.0);

    public void SetThrottleKey(int numberKey)
    {
        if (numberKey is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(numberKey), "Throttle keys range from 0 to 9.");
        }

        ThrottleKey = numberKey;
    }

    public void IncreaseThrottle()
    {
        var percent = Math.Min(100, ThrottlePercent + 10);
        ThrottleKey = percent == 100 ? 0 : Math.Max(1, percent / 10);
    }

    public void DecreaseThrottle()
    {
        var percent = Math.Max(0, ThrottlePercent - 10);
        ThrottleKey = percent == 0 ? 1 : percent / 10;
    }

    public void ToggleDirection() => IsReversing = !IsReversing;

    /// <summary>
    /// Selects zero throttle while retaining current momentum for normal braking.
    /// </summary>
    public void SelectStop()
    {
        ThrottleKey = 1;
        IsReversing = false;
    }

    /// <summary>Stops the chassis immediately, as when the reactor powers down.</summary>
    public void StopImmediately()
    {
        SelectStop();
        CurrentSpeedKph = 0.0;
    }

    public MechDriveStep Advance(double seconds, double steering)
    {
        if (seconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        steering = Math.Clamp(steering, -1.0, 1.0);
        var targetSpeed = TargetSpeedKph;
        var isChangingDirection = Math.Abs(CurrentSpeedKph) > 0.001 &&
                                  Math.Sign(targetSpeed) != Math.Sign(CurrentSpeedKph);
        var isBraking = isChangingDirection ||
                        Math.Abs(targetSpeed) < Math.Abs(CurrentSpeedKph);
        var speedChange = (isBraking ? Profile.BrakingKphPerSecond : Profile.AccelerationKphPerSecond) * seconds;
        CurrentSpeedKph = MoveTowards(CurrentSpeedKph, targetSpeed, speedChange);

        var turnRate = SpeedFraction.Lerp(
            Profile.StationaryTurnRateDegreesPerSecond,
            Profile.FullSpeedTurnRateDegreesPerSecond);
        var headingChange = steering * turnRate * seconds;
        var distanceMeters = CurrentSpeedKph / 3.6 * seconds;
        return new MechDriveStep(distanceMeters, headingChange);
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
