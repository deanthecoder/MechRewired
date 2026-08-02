// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Simulation;

[TestFixture]
public sealed class MechDriveTests
{
    private static readonly MechDriveProfile Profile = new(86.4);

    [TestCase(1, 0, 0.0)]
    [TestCase(5, 50, 43.2)]
    [TestCase(0, 100, 86.4)]
    public void NumberKeysSelectOriginalThrottleSettings(int key, int percent, double targetSpeed)
    {
        var drive = new MechDrive(Profile);

        drive.SetThrottleKey(key);

        Assert.That(drive.ThrottlePercent, Is.EqualTo(percent));
        Assert.That(drive.TargetSpeedKph, Is.EqualTo(targetSpeed).Within(0.001));
    }

    [Test]
    public void ReverseIsCappedAtHalfForwardSpeed()
    {
        var drive = new MechDrive(Profile);
        drive.SetThrottleKey(0);

        drive.ToggleDirection();

        Assert.That(drive.TargetSpeedKph, Is.EqualTo(-43.2).Within(0.001));
    }

    [Test]
    public void DirectionChangeBrakesBeforeAcceleratingInReverse()
    {
        var drive = new MechDrive(Profile);
        drive.SetThrottleKey(0);
        drive.Advance(1.0, 0.0);

        drive.ToggleDirection();
        var brakingStep = drive.Advance(0.5, 0.0);
        var reversingStep = drive.Advance(0.5, 0.0);

        Assert.That(brakingStep.DistanceMeters, Is.EqualTo(3.0 / 3.6 * 0.5).Within(0.001));
        Assert.That(reversingStep.DistanceMeters, Is.LessThan(0.0));
    }

    [Test]
    public void SteeringRateDropsAsSpeedIncreases()
    {
        var drive = new MechDrive(Profile);
        var stationaryTurn = drive.Advance(1.0, 1.0).HeadingChangeDegrees;
        drive.SetThrottleKey(0);
        for (var second = 0; second < 5; second++)
        {
            drive.Advance(1.0, 0.0);
        }

        var fullSpeedTurn = drive.Advance(1.0, 1.0).HeadingChangeDegrees;

        Assert.That(stationaryTurn, Is.EqualTo(45.0));
        Assert.That(fullSpeedTurn, Is.EqualTo(18.0).Within(0.001));
    }
}
