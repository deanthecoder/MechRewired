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
public sealed class MechGaitTests
{
    [Test]
    public void ActualDistanceControlsGaitPhaseAndFootPlants()
    {
        var gait = new MechGait();

        var firstPlant = gait.Advance(4.5, 0.0, 0.4, 0.1);
        var secondPlant = gait.Advance(4.5, 0.0, 0.4, 0.1);

        Assert.Multiple(() =>
        {
            Assert.That(firstPlant, Is.True);
            Assert.That(secondPlant, Is.True);
            Assert.That(gait.Phase, Is.EqualTo(Math.PI).Within(0.0001));
        });
    }

    [Test]
    public void FullSpeedUsesLongerStridesInsteadOfFasterFootfalls()
    {
        var walking = new MechGait();
        var running = new MechGait();

        walking.Advance(4.5, 0.0, 0.4, 0.1);
        running.Advance(
            MechGait.CycleDistanceMeters * MechGait.MaximumStrideScale / 4.0,
            0.0,
            1.0,
            0.1);

        Assert.That(running.Phase, Is.EqualTo(walking.Phase).Within(0.0001));
    }

    [Test]
    public void ReverseMovementRunsGaitBackwards()
    {
        var gait = new MechGait();

        gait.Advance(-4.5, 0.0, 0.4, 0.1);

        Assert.That(gait.Phase, Is.EqualTo(Math.Tau * 0.75).Within(0.0001));
    }

    [Test]
    public void TurningInPlaceAnimatesFeetThenSettlesAtRest()
    {
        var gait = new MechGait();

        gait.Advance(0.0, Math.PI / 4.0, 0.0, 0.1);
        var activeWeight = gait.Weight;
        gait.Advance(0.0, 0.0, 0.0, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(activeWeight, Is.EqualTo(MechGait.PivotPoseWeight).Within(0.0001));
            Assert.That(gait.Weight, Is.Zero);
        });
    }

    [Test]
    public void NegativeElapsedTimeIsRejected()
    {
        var gait = new MechGait();

        Assert.That(
            () => gait.Advance(0.0, 0.0, 0.0, -0.1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
