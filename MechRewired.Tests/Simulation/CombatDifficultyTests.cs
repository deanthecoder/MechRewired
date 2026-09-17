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
public sealed class CombatDifficultyTests
{
    [Test]
    public void MediumMatchesMechVmGunneryReference()
    {
        var profile = CombatDifficultyProfile.For(CombatDifficulty.Medium);

        Assert.That(profile.GetMaximumAimErrorDegrees(4, 1.0), Is.EqualTo(8.0));
    }

    [Test]
    public void HardPreservesPreviousMechRewiredCombatTune()
    {
        var profile = CombatDifficultyProfile.For(CombatDifficulty.Hard);

        Assert.Multiple(() =>
        {
            Assert.That(profile.FireDecisionIntervalSeconds, Is.EqualTo(0.60));
            Assert.That(profile.GetMaximumAimErrorDegrees(4, 1.0), Is.EqualTo(2.30));
        });
    }

    [Test]
    public void EasyToHardProgressivelyTightensAimAndFiringPressure()
    {
        var easy = CombatDifficultyProfile.For(CombatDifficulty.Easy);
        var medium = CombatDifficultyProfile.For(CombatDifficulty.Medium);
        var hard = CombatDifficultyProfile.For(CombatDifficulty.Hard);

        Assert.Multiple(() =>
        {
            Assert.That(easy.GetMaximumAimErrorDegrees(4, 1.0),
                Is.GreaterThan(medium.GetMaximumAimErrorDegrees(4, 1.0)));
            Assert.That(medium.GetMaximumAimErrorDegrees(4, 1.0),
                Is.GreaterThan(hard.GetMaximumAimErrorDegrees(4, 1.0)));
            Assert.That(easy.FireDecisionIntervalSeconds, Is.GreaterThan(medium.FireDecisionIntervalSeconds));
            Assert.That(medium.FireDecisionIntervalSeconds, Is.GreaterThan(hard.FireDecisionIntervalSeconds));
        });
    }

    [TestCase(-1, -1.0, 2.0)]
    [TestCase(0, 0.5, 2.75)]
    [TestCase(4, 2.0, 13.5)]
    public void AimErrorClampsSkillAndRange(int skill, double rangeFraction, double expected)
    {
        var profile = CombatDifficultyProfile.For(CombatDifficulty.Easy);

        Assert.That(profile.GetMaximumAimErrorDegrees(skill, rangeFraction), Is.EqualTo(expected));
    }
}
