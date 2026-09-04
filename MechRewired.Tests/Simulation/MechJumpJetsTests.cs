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
public sealed class MechJumpJetsTests
{
    [Test]
    public void FullBurnReachesWolfReferenceHeight()
    {
        var jumpJets = new MechJumpJets();
        var height = 0.0;

        for (var frame = 0; frame < 900 && (!jumpJets.IsAirborne || jumpJets.VerticalVelocityMetersPerSecond > 0.0); frame++)
        {
            var step = jumpJets.Advance(1.0 / 60.0, true, height);
            height += step.VerticalDisplacementMeters;
        }

        Assert.That(jumpJets.FuelFraction, Is.EqualTo(0.0).Within(0.0001));
        Assert.That(height, Is.EqualTo(MechJumpJets.WolfMaximumHeightMeters).Within(0.75));
    }

    [Test]
    public void EmptyTankRechargesInThirtyFiveSeconds()
    {
        var jumpJets = new MechJumpJets();
        var height = 0.0;
        for (var frame = 0; frame < 420; frame++)
        {
            var step = jumpJets.Advance(1.0 / 60.0, true, height);
            height += step.VerticalDisplacementMeters;
        }

        Assert.That(jumpJets.FuelFraction, Is.EqualTo(0.0).Within(0.0001));
        jumpJets.Advance(MechJumpJets.FuelRechargeDurationSeconds, false, height);

        Assert.That(jumpJets.FuelFraction, Is.EqualTo(1.0).Within(0.0001));
    }

    [Test]
    public void FullTankProvidesSevenSecondsOfThrust()
    {
        var jumpJets = new MechJumpJets();

        var firstStep = jumpJets.Advance(6.99, true, 0.0);
        var finalStep = jumpJets.Advance(0.01, true, firstStep.VerticalDisplacementMeters);

        Assert.Multiple(() =>
        {
            Assert.That(finalStep.IsThrusting, Is.True);
            Assert.That(jumpJets.FuelFraction, Is.EqualTo(0.0).Within(0.0001));
        });
    }

    [Test]
    public void FallingOneHundredThirtyMetersTakesAboutFiveSeconds()
    {
        var jumpJets = new MechJumpJets();
        var height = 130.0;
        var elapsed = 0.0;
        JumpJetStep step;
        do
        {
            step = jumpJets.Advance(1.0 / 60.0, false, height);
            height += step.VerticalDisplacementMeters;
            elapsed += 1.0 / 60.0;
        } while (!step.Landed);

        Assert.That(elapsed, Is.EqualTo(5.0).Within(0.05));
        Assert.That(height, Is.EqualTo(0.0).Within(0.001));
    }

    [Test]
    public void LowFuelThresholdActivatesAtTenPercent()
    {
        var jumpJets = new MechJumpJets();

        var aboveLimit = jumpJets.Advance(
            MechJumpJets.FuelBurnDurationSeconds * 0.899,
            true,
            0.0);
        var crossing = jumpJets.Advance(0.01, true, aboveLimit.VerticalDisplacementMeters);
        var whileLow = jumpJets.Advance(
            0.1,
            true,
            aboveLimit.VerticalDisplacementMeters + crossing.VerticalDisplacementMeters);

        Assert.Multiple(() =>
        {
            Assert.That(jumpJets.IsLowFuel, Is.True);
            Assert.That(aboveLimit.LowFuelWarning, Is.False);
            Assert.That(crossing.LowFuelWarning, Is.True);
            Assert.That(whileLow.LowFuelWarning, Is.False);
        });
    }

    [Test]
    public void GroundSpoolUpEmitsThrustAndBurnsFuelWithoutLifting()
    {
        var jumpJets = new MechJumpJets();
        var spool = jumpJets.Advance(0.75, true, 0.0);

        Assert.Multiple(() =>
        {
            Assert.That(spool.IsThrusting, Is.True);
            Assert.That(spool.IsAirborne, Is.False);
            Assert.That(spool.Landed, Is.False);
            Assert.That(spool.VerticalDisplacementMeters, Is.Zero);
            Assert.That(jumpJets.VerticalVelocityMetersPerSecond, Is.Zero);
            Assert.That(jumpJets.FuelFraction, Is.EqualTo(1.0 - 0.75 / 7.0).Within(0.0001));
        });

        var lift = jumpJets.Advance(0.01, true, 0.0);
        Assert.That(lift.IsAirborne, Is.True);
        Assert.That(lift.VerticalDisplacementMeters, Is.GreaterThan(0.0));
    }

    [Test]
    public void FrameCrossingSpoolCompletionOnlyIntegratesRemainingLiftTime()
    {
        var jumpJets = new MechJumpJets();
        var step = jumpJets.Advance(0.8, true, 0.0);
        var acceleration = MechJumpJets.ThrustMetersPerSecondSquared - MechJumpJets.GravityMetersPerSecondSquared;

        Assert.That(step.VerticalDisplacementMeters, Is.EqualTo(0.5 * acceleration * 0.05 * 0.05).Within(0.000001));
    }

    [Test]
    public void ReleasingTheKeyCancelsGroundSpoolUp()
    {
        var jumpJets = new MechJumpJets();
        jumpJets.Advance(0.5, true, 0.0);
        var released = jumpJets.Advance(0.1, false, 0.0);
        var restarted = jumpJets.Advance(0.5, true, 0.0);

        Assert.That(released.IsThrusting, Is.False);
        Assert.That(restarted.IsThrusting, Is.True);
        Assert.That(restarted.IsAirborne, Is.False);
        Assert.That(restarted.VerticalDisplacementMeters, Is.Zero);
    }

    [Test]
    public void AirborneThrustDoesNotWaitForGroundSpoolUp()
    {
        var jumpJets = new MechJumpJets();
        var step = jumpJets.Advance(0.1, true, 10.0);

        Assert.That(step.IsThrusting, Is.True);
        Assert.That(step.VerticalDisplacementMeters, Is.GreaterThan(0.0));
    }

    [Test]
    public void TouchdownIsReportedOnceAndNotRepeatedWhileGrounded()
    {
        var jumpJets = new MechJumpJets();
        var touchdown = jumpJets.Advance(0.5, false, 1.0);
        var grounded = jumpJets.Advance(0.5, false, 0.0);

        Assert.Multiple(() =>
        {
            Assert.That(touchdown.Landed, Is.True);
            Assert.That(touchdown.ImpactSpeedMetersPerSecond, Is.GreaterThan(0.0));
            Assert.That(grounded.Landed, Is.False);
            Assert.That(grounded.ImpactSpeedMetersPerSecond, Is.Zero);
        });
    }
}
