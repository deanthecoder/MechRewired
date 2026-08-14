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
public sealed class MechHeatTests
{
    [Test]
    public void CriticalThresholdAddsEffectiveSinksToTheBaseHeatCushion()
    {
        Assert.That(MechHeat.GetCriticalHeatThreshold(24), Is.EqualTo(54.0));
    }

    [Test]
    public void CriticalThresholdRejectsNegativeSinkCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MechHeat.GetCriticalHeatThreshold(-1));
    }

    [Test]
    public void AddAndAdvanceTrackHeatGenerationAndSinkCooling()
    {
        var heat = new MechHeat(100.0, 2.4);

        heat.Add(10.0);
        heat.Advance(2.5);

        Assert.That(heat.CurrentHeat, Is.EqualTo(4.0).Within(0.001));
        Assert.That(heat.Fraction, Is.EqualTo(0.04).Within(0.001));
    }

    [Test]
    public void AddCapsAtTheConfiguredMaximum()
    {
        var heat = new MechHeat(25.0, 2.0);

        heat.Add(30.0);

        Assert.That(heat.CurrentHeat, Is.EqualTo(25.0));
    }
}
