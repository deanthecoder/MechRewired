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
using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Simulation;

[TestFixture]
public sealed class TriangleSightSegmentTests
{
    [TestCase(-1.0f, false)]
    [TestCase(0.0f, true)]
    [TestCase(5.0f, true)]
    [TestCase(8.99f, true)]
    [TestCase(9.0f, false)]
    [TestCase(9.5f, false)]
    [TestCase(11.0f, false)]
    public void KeepsOriginalTargetMarginAndRejectsBehindRay(float wallZ, bool expected)
    {
        var sight = new TriangleSightSegment(Vector3.Zero, new Vector3(0, 0, 10), 1.0f);
        Assert.That(sight.IsBlockedBy(new(-2, -2, wallZ), new(2, -2, wallZ), new(0, 2, wallZ)), Is.EqualTo(expected));
    }

    [TestCase(0.0f)]
    [TestCase(0.001f)]
    [TestCase(0.5f)]
    [TestCase(1.0f)]
    public void SegmentInsideTargetMarginCannotBeBlocked(float length)
    {
        var sight = new TriangleSightSegment(Vector3.Zero, new Vector3(0, 0, length), 1.0f);
        Assert.That(sight.HasLength, Is.False);
        Assert.That(sight.IsBlockedBy(new(-2, -2, 0), new(2, -2, 0), new(0, 2, 0)), Is.False);
    }

    [Test]
    public void HugeTriangleWithAllVerticesFarAwayStillBlocksInBothDirections()
    {
        var a = new Vector3(-10000, -10000, 5);
        var b = new Vector3(10000, -10000, 5);
        var c = new Vector3(0, 10000, 5);
        var forward = new TriangleSightSegment(Vector3.Zero, new Vector3(0, 0, 10), 1.0f);
        var backward = new TriangleSightSegment(new Vector3(0, 0, 10), Vector3.Zero, 1.0f);
        Assert.Multiple(() =>
        {
            Assert.That(forward.IsBlockedBy(a, b, c), Is.True);
            Assert.That(forward.IsBlockedBy(c, b, a), Is.True);
            Assert.That(backward.IsBlockedBy(a, b, c), Is.True);
        });
    }

    [Test]
    public void CurrentVerticesDetermineOcclusionWhenSceneryMoves()
    {
        var sight = new TriangleSightSegment(Vector3.Zero, new Vector3(0, 0, 10), 1.0f);
        var a = new Vector3(-2, -2, 5);
        var b = new Vector3(2, -2, 5);
        var c = new Vector3(0, 2, 5);
        Assert.That(sight.IsBlockedBy(a, b, c), Is.True);
        var movement = new Vector3(20, 0, 0);
        Assert.That(sight.IsBlockedBy(a + movement, b + movement, c + movement), Is.False);
        Assert.That(sight.IsBlockedBy(a, b, c), Is.True);
    }

    [Test]
    public void DegenerateAndParallelTrianglesDoNotBlock()
    {
        var sight = new TriangleSightSegment(Vector3.Zero, new Vector3(0, 0, 10), 1.0f);
        Assert.Multiple(() =>
        {
            Assert.That(sight.IsBlockedBy(Vector3.Zero, Vector3.Zero, Vector3.Zero), Is.False);
            Assert.That(sight.IsBlockedBy(new(1, 0, 0), new(1, 1, 0), new(1, 0, 5)), Is.False);
        });
    }

    [Test]
    public void BroadRejectionAgreesWithUnfilteredNearestRayAcrossSeededScenes()
    {
        var random = new Random(71329);
        for (var scene = 0; scene < 200; scene++)
        {
            var start = Point(100);
            var end = Point(100);
            var sight = new TriangleSightSegment(start, end, 1.0f);
            var direction = Vector3.Normalize(end - start);
            var nearest = float.PositiveInfinity;
            var blocked = false;
            for (var triangle = 0; triangle < 100; triangle++)
            {
                var a = Point(200);
                var b = Point(200);
                var c = Point(200);
                var reference = OriginalRayDistance(start, direction, a, b, c);
                nearest = Math.Min(nearest, reference);
                var result = sight.IsBlockedBy(a, b, c);
                Assert.That(result, Is.EqualTo(reference < Vector3.Distance(start, end) - 1.0f),
                    $"Scene {scene}, triangle {triangle}");
                blocked |= result;
            }
            Assert.That(blocked, Is.EqualTo(nearest < Vector3.Distance(start, end) - 1.0f));
        }
        return;

        Vector3 Point(float scale) => new(
            ((float)random.NextDouble() - 0.5f) * scale,
            ((float)random.NextDouble() - 0.5f) * scale,
            ((float)random.NextDouble() - 0.5f) * scale);
    }

    // Frozen reference for the previous unfiltered, two-sided ray query.
    private static float OriginalRayDistance(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        var edge1 = b - a;
        var edge2 = c - a;
        var perpendicular = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, perpendicular);
        if (MathF.Abs(determinant) < 0.000001f)
            return float.PositiveInfinity;
        var inverseDeterminant = 1.0f / determinant;
        var originOffset = origin - a;
        var u = Vector3.Dot(originOffset, perpendicular) * inverseDeterminant;
        if (u is < 0.0f or > 1.0f)
            return float.PositiveInfinity;
        var cross = Vector3.Cross(originOffset, edge1);
        var v = Vector3.Dot(direction, cross) * inverseDeterminant;
        if (v < 0.0f || u + v > 1.0f)
            return float.PositiveInfinity;
        var distance = Vector3.Dot(edge2, cross) * inverseDeterminant;
        return distance >= 0.0f ? distance : float.PositiveInfinity;
    }
}
