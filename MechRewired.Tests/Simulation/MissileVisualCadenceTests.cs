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
public sealed class MissileVisualCadenceTests
{
    [Test]
    public void FortyEightLaunchesSelectOneInThreeForSmokeAndOneInEightForLights()
    {
        var cadence = new MissileVisualCadence();
        var smokePositions = new List<int>();
        var lightPositions = new List<int>();

        for (var launch = 0; launch < 48; launch++)
        {
            var selected = cadence.Next();
            if (selected.Smoke)
                smokePositions.Add(launch);
            if (selected.Light)
                lightPositions.Add(launch);
        }

        Assert.Multiple(() =>
        {
            Assert.That(smokePositions, Has.Count.EqualTo(16));
            Assert.That(lightPositions, Has.Count.EqualTo(6));
            Assert.That(smokePositions, Is.EqualTo(Enumerable.Range(0, 16).Select(index => index * 3)));
            Assert.That(lightPositions, Is.EqualTo(Enumerable.Range(0, 6).Select(index => index * 8)));
        });
    }

    [Test]
    public void CadencesRemainIndependentAndContinueAcrossSmallSalvos()
    {
        var player = new MissileVisualCadence();
        var enemy = new MissileVisualCadence();

        var playerFirstSalvo = new[] { player.Next(), player.Next() };
        var enemyFirstLaunch = enemy.Next();
        var playerSecondSalvo = new[] { player.Next(), player.Next() };
        var enemyNextLaunch = enemy.Next();

        Assert.Multiple(() =>
        {
            Assert.That(playerFirstSalvo[0], Is.EqualTo((Smoke: true, Light: true)));
            Assert.That(playerFirstSalvo[1], Is.EqualTo((Smoke: false, Light: false)));
            Assert.That(playerSecondSalvo[0], Is.EqualTo((Smoke: false, Light: false)));
            Assert.That(playerSecondSalvo[1], Is.EqualTo((Smoke: true, Light: false)));
            Assert.That(enemyFirstLaunch, Is.EqualTo((Smoke: true, Light: true)));
            Assert.That(enemyNextLaunch, Is.EqualTo((Smoke: false, Light: false)));
        });
    }
}
