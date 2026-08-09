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
public sealed class EnemyAwarenessTests
{
    private static readonly double MinimumForwardAlignment = Math.Cos(70.0 * Math.PI / 180.0);

    [Test]
    public void DirectionalSensorsAcquireVisibleTargetAtRange()
    {
        Assert.That(
            EnemyAwareness.CanAcquire(500.0, 600.0, 0.8, MinimumForwardAlignment, true),
            Is.True);
    }

    [Test]
    public void SideApproachRemainsHiddenAtRange()
    {
        Assert.That(
            EnemyAwareness.CanAcquire(300.0, 600.0, 0.0, MinimumForwardAlignment, true),
            Is.False);
    }

    [Test]
    public void CloseApproachIsDetectedFromAnyDirection()
    {
        Assert.That(
            EnemyAwareness.CanAcquire(100.0, 600.0, -1.0, MinimumForwardAlignment, true),
            Is.True);
    }

    [Test]
    public void TerrainStillBlocksCloseAwareness()
    {
        Assert.That(
            EnemyAwareness.CanAcquire(50.0, 600.0, 1.0, MinimumForwardAlignment, false),
            Is.False);
    }

    [TestCase(200.0, 75.0)]
    [TestCase(500.0, 100.0)]
    [TestCase(1000.0, 125.0)]
    public void CloseAwarenessRangeIsBounded(double acquisitionRange, double expected)
    {
        Assert.That(EnemyAwareness.GetCloseAwarenessRange(acquisitionRange), Is.EqualTo(expected));
    }
}
