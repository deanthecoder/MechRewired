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
public sealed class MechAirbornePoseTests
{
    [Test]
    public void GroundedPosePreservesWalkingPitch()
    {
        var pose = new MechAirbornePose();

        Assert.That(pose.GetToePitch(0.1f, 1.0f), Is.EqualTo(0.1f));
    }

    [Test]
    public void TakeoffBlendsIntoGeometrySelectedSag()
    {
        var pose = new MechAirbornePose();
        pose.Advance(0.6f, true);

        Assert.That(pose.Weight, Is.EqualTo(0.25f).Within(0.001f));

        pose.Advance(0.6f, true);
        Assert.Multiple(() =>
        {
            Assert.That(pose.GetToePitch(0.1f, 1.0f), Is.EqualTo(36.0f * MathF.PI / 180.0f).Within(0.001f));
            Assert.That(pose.GetToePitch(0.1f, -1.0f), Is.EqualTo(-36.0f * MathF.PI / 180.0f).Within(0.001f));
        });
    }

    [Test]
    public void TakeoffStartsGentlyWhileTheMechClearsTheGround()
    {
        var pose = new MechAirbornePose();
        pose.Advance(0.12f, true);

        Assert.That(pose.Weight, Is.EqualTo(0.01f).Within(0.0001f));
        Assert.That(pose.GetToePitch(0.0f, 1.0f), Is.EqualTo(0.36f * MathF.PI / 180.0f).Within(0.0001f));
    }

    [Test]
    public void LandingSmoothlyRestoresWalkingPitchWithoutAccumulatedRotation()
    {
        var pose = new MechAirbornePose();
        pose.Advance(1.2f, true);
        pose.Advance(0.09f, false);
        Assert.That(pose.Weight, Is.EqualTo(0.5f).Within(0.001f));

        pose.Advance(0.1f, false);
        Assert.That(pose.GetToePitch(0.1f, 1.0f), Is.EqualTo(0.1f));

        pose.Advance(1.2f, true);
        pose.Advance(1.0f, false);
        Assert.That(pose.GetToePitch(0.0f, -1.0f), Is.Zero);
    }

    [TestCase(-0.5f, 0.5f, 1.0f)]
    [TestCase(0.5f, -0.5f, -1.0f)]
    [TestCase(0.0f, 0.0f, 0.0f)]
    public void SagDirectionSelectsLowerGeometryRegardlessOfChassisNaming(
        float positivePitchHeight,
        float negativePitchHeight,
        float expectedDirection)
    {
        Assert.That(
            MechAirbornePose.ChooseSagDirection(positivePitchHeight, negativePitchHeight),
            Is.EqualTo(expectedDirection));
    }
}
