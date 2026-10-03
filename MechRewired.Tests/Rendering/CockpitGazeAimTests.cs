// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Numerics;
using MechRewired.Rendering;
using NUnit.Framework;

namespace MechRewired.Tests.Rendering;

[TestFixture]
public sealed class CockpitGazeAimTests
{
    private static readonly Vector3[] Glass =
    [
        new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1)
    ];

    [TestCase(0, 0, 0, 0)]
    [TestCase(1, 0, 0.46f, 0)]
    [TestCase(-1, 0, -0.46f, 0)]
    [TestCase(0, 1, 0, 0.46f)]
    [TestCase(0, -1, 0, -0.46f)]
    [TestCase(1, 1, 0.46f, 0.46f)]
    public void ClampsToProjectedGlassWithReticleInset(float x, float y, float expectedX, float expectedY)
    {
        Assert.That(CockpitGazeAim.TryGetAimPoint(new Vector3(0, 0, 1), new Vector3(x, y, -1),
            Glass, Vector2.One, 0.04f, out var result), Is.True);
        AssertPoint(result, new Vector2(expectedX, expectedY));
    }

    [Test]
    public void HeadLeanMovesBothAimAndProjectedGlass()
    {
        Assert.That(CockpitGazeAim.TryGetAimPoint(new Vector3(0.4f, 0, 1), -Vector3.UnitZ,
            Glass, Vector2.One, 0.04f, out var straight), Is.True);
        AssertPoint(straight, new Vector2(0.4f, 0));
        Assert.That(CockpitGazeAim.TryGetAimPoint(new Vector3(0.4f, 0, 1), new Vector3(1, 0, -1),
            Glass, Vector2.One, 0.04f, out var edge), Is.True);
        AssertPoint(edge, new Vector2(0.66f, 0));
    }

    [Test]
    public void HudRectangleAlsoIncludesReticleInset()
    {
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, new Vector3(1, 1, -1),
            Glass, new Vector2(0.3f, 0.2f), 0.04f, out var result), Is.True);
        AssertPoint(result, new Vector2(0.26f, 0.16f));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void LookingSidewaysOrBehindStaysAtNearestSide(float z)
    {
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, new Vector3(1, 0, z),
            Glass, Vector2.One, 0.04f, out var result), Is.True);
        AssertPoint(result, new Vector2(0.46f, 0));
    }

    [Test]
    public void DiagonalGlassEdgeUsesPerpendicularInset()
    {
        Vector3[] diamond = [new(-2, 0, -1), new(0, -2, -1), new(2, 0, -1), new(0, 2, -1)];
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, new Vector3(1, 1, -1),
            diamond, Vector2.One, 0.04f, out var result), Is.True);
        var coordinate = (1 - 0.04f * MathF.Sqrt(2)) / 2;
        AssertPoint(result, new Vector2(coordinate, coordinate));
    }

    [Test]
    public void UnsortedDuplicateVerticesDoNotChangeOutline()
    {
        Vector3[] vertices = [Glass[2], Glass[0], Glass[2], Glass[3], Glass[1], Vector3.Zero];
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, new Vector3(1, 0, -1),
            vertices, Vector2.One, 0.04f, out var result), Is.True);
        AssertPoint(result, new Vector2(0.46f, 0));
    }

    [Test]
    public void ProjectionRespectsEyeDepth()
    {
        Assert.That(CockpitGazeAim.TryGetAimPoint(new Vector3(0, 0, 2), Vector3.UnitX,
            Glass, Vector2.One, 0, out var result), Is.True);
        AssertPoint(result, new Vector2(2.0f / 3, 0));
    }

    [Test]
    public void InvalidAndDegenerateInputsReturnFalse()
    {
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.Zero, -Vector3.UnitZ, Glass, Vector2.One, 0, out _), Is.False);
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, Vector3.UnitZ, Glass, Vector2.One, 0, out _), Is.False);
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, Vector3.Zero, Glass, Vector2.One, 0, out _), Is.False);
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, -Vector3.UnitZ, Glass, Vector2.One, 0.5f, out _), Is.False);
        Vector3[] line = [new(-1, 0, -1), new(0, 0, -1), new(1, 0, -1)];
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, -Vector3.UnitZ, line, Vector2.One, 0, out _), Is.False);
        Vector3[] behind = [new(-1, -1, 2), new(1, -1, 2), new(1, 1, 2)];
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, -Vector3.UnitZ, behind, Vector2.One, 0, out _), Is.False);
    }

    [Test]
    public void RepeatedCallsDoNotAllocate()
    {
        CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, -Vector3.UnitZ, Glass, Vector2.One, 0.04f, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
            CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, -Vector3.UnitZ, Glass, Vector2.One, 0.04f, out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void EdgeTurnIsZeroInCenterAndSmoothlyPointsTowardAllFourEdges()
    {
        Assert.That(TryAim(0, 0, out var center), Is.True);
        AssertPoint(center, Vector2.Zero);
        Assert.That(TryAim(0.9f, 0, out var right), Is.True);
        Assert.That(right.X, Is.EqualTo(1).Within(0.0001f));
        Assert.That(right.Y, Is.Zero);
        Assert.That(TryAim(-0.9f, 0, out var left), Is.True);
        Assert.That(left.X, Is.EqualTo(-1).Within(0.0001f));
        Assert.That(TryAim(0, 0.9f, out var up), Is.True);
        Assert.That(up.Y, Is.EqualTo(1).Within(0.0001f));
        Assert.That(TryAim(0, -0.9f, out var down), Is.True);
        Assert.That(down.Y, Is.EqualTo(-1).Within(0.0001f));
        Assert.That(TryAim(0.4f, 0, out var ramp), Is.True);
        Assert.That(ramp.X, Is.GreaterThan(0).And.LessThan(1));
    }

    [Test]
    public void EdgeTurnUsesNarrowTrapezoidCrossSectionAtAimHeight()
    {
        Vector3[] trapezoid = [new(-1, -1, -1), new(1, -1, -1), new(0.2f, 1, -1), new(-0.2f, 1, -1)];
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, new Vector3(0.4f, 0.4f, -1),
            trapezoid, Vector2.One, 0, out _, out var edgeTurn), Is.True);
        Assert.That(edgeTurn.X, Is.EqualTo(1).Within(0.0001f));
        Assert.That(edgeTurn.Y, Is.InRange(-1, 1));
    }

    [Test]
    public void InvalidGazeReturnsZeroEdgeTurn()
    {
        Assert.That(CockpitGazeAim.TryGetAimPoint(Vector3.Zero, -Vector3.UnitZ,
            Glass, Vector2.One, 0, out _, out var edgeTurn), Is.False);
        Assert.That(edgeTurn, Is.EqualTo(Vector2.Zero));
    }

    private static bool TryAim(float x, float y, out Vector2 edgeTurn) =>
        CockpitGazeAim.TryGetAimPoint(Vector3.UnitZ, new Vector3(x, y, -1),
            Glass, Vector2.One, 0.04f, out _, out edgeTurn);

    private static void AssertPoint(Vector2 actual, Vector2 expected) =>
        Assert.That(Vector2.Distance(actual, expected), Is.LessThan(0.0001f));
}
