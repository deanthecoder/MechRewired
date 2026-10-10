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
public sealed class TerrainRayBoundsIndexTests
{
    [Test]
    public void VerticalRayVisitsOnlyNearbyGroupsOnLargeGrid()
    {
        var bounds = new List<TerrainRayBoundsIndex.Bounds>();
        for (var x = 0; x < 128; x++)
        for (var z = 0; z < 128; z++)
            bounds.Add(new TerrainRayBoundsIndex.Bounds(new(x * 10, z * 10), new(x * 10 + 5, z * 10 + 5)));
        var index = new TerrainRayBoundsIndex(bounds);
        var visited = new List<int>();
        index.Visit(new(1, 1), Vector2.Zero, 100, i => { visited.Add(i); return false; });
        Assert.That(visited, Does.Contain(0));
        Assert.That(visited.Count, Is.LessThanOrEqualTo(32));
    }

    [Test]
    public void FiniteRayAndHugeSpanningBoundsRetainEveryExactCandidate()
    {
        var random = new Random(1234);
        var bounds = Enumerable.Range(0, 1000).Select(_ =>
        {
            var minimum = new Vector2(random.Next(-500, 500), random.Next(-500, 500));
            return new TerrainRayBoundsIndex.Bounds(minimum, minimum + new Vector2(random.Next(1, 100), random.Next(1, 100)));
        }).ToList();
        bounds.Add(new TerrainRayBoundsIndex.Bounds(new(-10000, -10000), new(10000, 10000)));
        var index = new TerrainRayBoundsIndex(bounds);
        foreach (var direction in new[] {Vector2.UnitX, Vector2.UnitY, Vector2.Normalize(new Vector2(-1, -2)), Vector2.Zero})
        {
            var found = new HashSet<int>();
            index.Visit(Vector2.Zero, direction, 300, i => { found.Add(i); return false; });
            for (var i = 0; i < bounds.Count; i++)
            {
                var b = bounds[i];
                var exactBoundsHit = BoundedMeshRay.IntersectsBounds(Vector3.Zero, new(direction.X, 0, direction.Y),
                    new(b.Minimum.X, -1, b.Minimum.Y), new(b.Maximum.X, 1, b.Maximum.Y), 300);
                if (exactBoundsHit)
                    Assert.That(found, Does.Contain(i));
            }
        }
    }

    [Test]
    public void VisitorStopsImmediatelyAndEmptyIndexNeverVisits()
    {
        var bounds = Enumerable.Range(0, 100).Select(_ => new TerrainRayBoundsIndex.Bounds(new(-1,-1), new(1,1))).ToArray();
        var count = 0;
        Assert.That(new TerrainRayBoundsIndex(bounds).Visit(Vector2.Zero, Vector2.UnitX, 10, _ => { count++; return true; }), Is.True);
        Assert.That(count, Is.EqualTo(1));
        Assert.That(new TerrainRayBoundsIndex(Array.Empty<TerrainRayBoundsIndex.Bounds>()).Visit(Vector2.Zero, Vector2.UnitX, 10, _ => throw new Exception()), Is.False);
    }
}
