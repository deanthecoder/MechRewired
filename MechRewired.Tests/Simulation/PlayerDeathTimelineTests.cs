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
public sealed class PlayerDeathTimelineTests
{
    [Test]
    public void CameraOrbitsAndAscendsBeforeFade()
    {
        var timeline = new PlayerDeathTimeline();

        var frame = timeline.Advance(2.0);

        Assert.Multiple(() =>
        {
            Assert.That(frame.OrbitRadians, Is.GreaterThan(0.0));
            Assert.That(frame.AscentMeters, Is.EqualTo(12.0));
            Assert.That(frame.FadeOpacity, Is.Zero);
            Assert.That(frame.ShouldRestart, Is.False);
        });
    }

    [Test]
    public void FadeCompletesBeforeRestart()
    {
        var timeline = new PlayerDeathTimeline();

        var faded = timeline.Advance(5.0);
        var restart = timeline.Advance(0.5);

        Assert.Multiple(() =>
        {
            Assert.That(faded.FadeOpacity, Is.EqualTo(1.0));
            Assert.That(faded.ShouldRestart, Is.False);
            Assert.That(restart.ShouldRestart, Is.True);
        });
    }

    [Test]
    public void NegativeElapsedTimeIsRejected()
    {
        var timeline = new PlayerDeathTimeline();

        Assert.That(
            () => timeline.Advance(-0.1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
