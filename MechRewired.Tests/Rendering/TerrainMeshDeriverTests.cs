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
public sealed class TerrainMeshDeriverTests
{
    [Test]
    public void FlatSourceRemainsOneTriangleRatherThanBeingRedundantlyTessellated()
    {
        var source = new TerrainSourceTriangle(
            new Vector3(-1.0f, 0.0f, -1.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(1.0f, 0.0f, -1.0f));

        var derived = TerrainMeshDeriver.Build(new[] { source }, subdivisions: 4);

        Assert.Multiple(() =>
        {
            Assert.That(derived.TriangleCount, Is.EqualTo(1));
            Assert.That(derived.Vertices, Has.All.Matches<Vector3>(vertex => Math.Abs(vertex.Y) < 0.0001f));
            Assert.That(derived.Normals, Has.All.Matches<Vector3>(normal => normal.Y > 0.999f));
        });
    }

    [Test]
    public void ConnectedSharpFacesProduceCurvedSharedEdge()
    {
        var left = new Vector3(-1.0f, 0.0f, 0.0f);
        var right = new Vector3(1.0f, 0.0f, 0.0f);
        var source = new[]
        {
            new TerrainSourceTriangle(left, right, new Vector3(0.0f, 0.0f, -1.0f)),
            new TerrainSourceTriangle(right, left, new Vector3(0.0f, 1.0f, 1.0f))
        };

        var derived = TerrainMeshDeriver.Build(
            source,
            subdivisions: 4,
            smoothingAngleDegrees: 30.0f,
            smoothingStrength: 1.0f);

        Assert.Multiple(() =>
        {
            Assert.That(derived.Vertices.Any(vertex => Vector3.Distance(vertex, left) < 0.0001f), Is.True);
            Assert.That(derived.Vertices.Any(vertex => Vector3.Distance(vertex, right) < 0.0001f), Is.True);
            Assert.That(
                derived.Vertices.Any(vertex => vertex.Z < -0.1f && Math.Abs(vertex.Y) > 0.01f),
                Is.True,
                "The originally flat side should bend toward the connected raised face.");
        });
    }

    [Test]
    public void DownwardAndVerticalSealingFacesAreExcluded()
    {
        var upward = new TerrainSourceTriangle(
            new Vector3(-1.0f, 0.0f, -1.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(1.0f, 0.0f, -1.0f));
        var downward = new TerrainSourceTriangle(upward.A, upward.C, upward.B);
        var vertical = new TerrainSourceTriangle(
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f));

        var derived = TerrainMeshDeriver.Build(new[] { upward, downward, vertical }, subdivisions: 1);

        Assert.That(derived.TriangleCount, Is.EqualTo(1));
    }

    [Test]
    public void SubMetreAuthoredTerrainSeamAcrossQuantizationBoundaryIsWelded()
    {
        var source = new[]
        {
            new TerrainSourceTriangle(
                new Vector3(-10.0f, 0.0f, -0.5001f),
                new Vector3(10.0f, 0.0f, -0.5001f),
                new Vector3(0.0f, 0.0f, -10.0f)),
            new TerrainSourceTriangle(
                new Vector3(10.0f, 0.0f, 0.0999f),
                new Vector3(-10.0f, 0.0f, 0.0999f),
                new Vector3(0.0f, 10.0f, 10.0f))
        };

        var derived = TerrainMeshDeriver.Build(
            source,
            subdivisions: 2,
            smoothingAngleDegrees: 30.0f,
            smoothingStrength: 1.0f);

        Assert.That(
            derived.TriangleCount,
            Is.EqualTo(8),
            "The nearly identical shared edge should be welded and curved as one connected surface.");
    }

    [Test]
    public void CoarseCollisionSamplingRetainsTheAuthoredControlVertices()
    {
        var first = new Vector3(-2.0f, 0.0f, 0.0f);
        var second = new Vector3(2.0f, 0.0f, 0.0f);
        var third = new Vector3(0.0f, 0.0f, -2.0f);
        var raised = new Vector3(0.0f, 2.0f, 2.0f);
        var source = new[]
        {
            new TerrainSourceTriangle(first, second, third),
            new TerrainSourceTriangle(second, first, raised)
        };

        var render = TerrainMeshDeriver.Build(source, subdivisions: 8, smoothingAngleDegrees: 30.0f);
        var collision = TerrainMeshDeriver.Build(source, subdivisions: 3, smoothingAngleDegrees: 30.0f);

        Assert.Multiple(() =>
        {
            Assert.That(collision.TriangleCount, Is.LessThan(render.TriangleCount));
            foreach (var authoredVertex in new[] { first, second, third, raised })
            {
                Assert.That(
                    collision.Vertices.Any(vertex => Vector3.Distance(vertex, authoredVertex) < 0.0001f),
                    Is.True);
            }
        });
    }

    [Test]
    public void ElevatedExteriorEdgeProducesGroundSealingSkirt()
    {
        var source = new TerrainSourceTriangle(
            new Vector3(-2.0f, 2.0f, -1.0f),
            new Vector3(0.0f, 4.0f, 2.0f),
            new Vector3(2.0f, 2.0f, -1.0f));

        var derived = TerrainMeshDeriver.Build(new[] { source }, subdivisions: 1);
        var skirts = TerrainMeshBoundarySealer.BuildSkirts(derived, groundHeight: 0.0f);

        Assert.Multiple(() =>
        {
            Assert.That(derived.BoundaryEdges, Has.Count.EqualTo(3));
            Assert.That(skirts, Has.Count.EqualTo(6));
            Assert.That(
                skirts.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
                    .Any(vertex => Math.Abs(
                        vertex.Y + TerrainMeshBoundarySealer.GroundOverlapMetres) < 0.0001f),
                Is.True,
                "Each elevated exterior edge should overlap below the ground plane.");
        });
    }

    [Test]
    public void LowExteriorVerticesAreSnappedToTheSharedGroundPlane()
    {
        var source = new TerrainSourceTriangle(
            new Vector3(-2.0f, 0.85f, -1.0f),
            new Vector3(0.0f, 4.0f, 2.0f),
            new Vector3(2.0f, 0.85f, -1.0f));

        var derived = TerrainMeshDeriver.Build(new[] { source }, subdivisions: 1);
        var snapped = TerrainMeshDeriver.SnapLowExteriorVertices(
            derived,
            groundHeight: 0.0f,
            maximumHeightAboveGround: 2.0f,
            out var snappedCount);

        Assert.Multiple(() =>
        {
            Assert.That(snappedCount, Is.EqualTo(2));
            Assert.That(snapped.Vertices.Count(vertex => Math.Abs(vertex.Y) < 0.0001f), Is.EqualTo(2));
            Assert.That(snapped.Vertices.Any(vertex => Math.Abs(vertex.Y - 4.0f) < 0.0001f), Is.True);
        });
    }
}
