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
public sealed class TerrainHeightIndexTests
{
    [Test]
    public void InterpolatesHeightOnSlopedTriangle()
    {
        var index = new TerrainHeightIndex([
            new TerrainHeightTriangle(
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(10.0f, 10.0f, 0.0f),
                new Vector3(0.0f, 0.0f, 10.0f))
        ]);

        var found = index.TryGetHeight(new Vector2(5.0f, 2.0f), out var height, out _);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(height, Is.EqualTo(5.0f).Within(0.0001f));
        });
    }

    [Test]
    public void HighestOverlappingSurfaceWins()
    {
        var index = new TerrainHeightIndex([
            FlatTriangle(0.0f),
            FlatTriangle(8.0f)
        ]);

        var found = index.TryGetHeight(Vector2.Zero, out var height, out var triangleIndex);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(height, Is.EqualTo(8.0f));
            Assert.That(triangleIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void QueryOutsideTrianglesReturnsFalse()
    {
        var index = new TerrainHeightIndex([FlatTriangle(0.0f)]);

        Assert.That(index.TryGetHeight(new Vector2(20.0f, 20.0f), out _, out _), Is.False);
    }

    [Test]
    public void NegativeWorldCoordinatesUseTheCorrectGridCell()
    {
        var index = new TerrainHeightIndex([
            new TerrainHeightTriangle(
                new Vector3(-105.0f, 3.0f, -105.0f),
                new Vector3(-95.0f, 3.0f, -105.0f),
                new Vector3(-100.0f, 3.0f, -95.0f))
        ], cellSize: 16.0f);

        Assert.That(
            index.TryGetHeight(new Vector2(-100.0f, -100.0f), out var height, out _),
            Is.True);
        Assert.That(height, Is.EqualTo(3.0f));
    }

    private static TerrainHeightTriangle FlatTriangle(float height) => new(
        new Vector3(-5.0f, height, -5.0f),
        new Vector3(5.0f, height, -5.0f),
        new Vector3(0.0f, height, 5.0f));
}
