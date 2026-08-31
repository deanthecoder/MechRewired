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
    public void ConnectedSharpFacesRelaxNormalsBeyondTheAuthoredDiagonal()
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
            subdivisions: 6,
            smoothingAngleDegrees: 30.0f,
            smoothingStrength: 0.70f);
        var edges = new HashSet<(int First, int Second)>();
        for (var index = 0; index < derived.Indices.Count; index += 3)
        {
            AddEdge(derived.Indices[index], derived.Indices[index + 1]);
            AddEdge(derived.Indices[index + 1], derived.Indices[index + 2]);
            AddEdge(derived.Indices[index + 2], derived.Indices[index]);
        }

        var minimumAdjacentNormalDot = edges.Min(edge => Vector3.Dot(
            derived.Normals[edge.First],
            derived.Normals[edge.Second]));

        Assert.That(
            minimumAdjacentNormalDot,
            Is.GreaterThan(0.97f),
            "Normal relaxation should prevent direct light from exposing the source diagonal.");

        void AddEdge(int first, int second) =>
            edges.Add(first < second ? (first, second) : (second, first));
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
            Assert.That(
                skirts.Any(triangle =>
                    HorizontalDistance(triangle.A, triangle.B) > 3.99f ||
                    HorizontalDistance(triangle.B, triangle.C) > 3.99f),
                Is.True,
                "The seal should be a sloped apron, not a vertical wall below the hill edge.");
        });
    }

    [Test]
    public void ElevatedExteriorEdgeFollowsVaryingGroundHeight()
    {
        var source = new TerrainSourceTriangle(
            new Vector3(-2.0f, 2.0f, -1.0f),
            new Vector3(0.0f, 4.0f, 2.0f),
            new Vector3(2.0f, 2.0f, -1.0f));

        var derived = TerrainMeshDeriver.Build(new[] { source }, subdivisions: 1);
        var skirts = TerrainMeshBoundarySealer.BuildSkirts(
            derived,
            position => position.X * 0.25f);

        var groundVertices = skirts
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Where(vertex => vertex.Y < 1.5f)
            .Distinct()
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(skirts, Has.Count.EqualTo(6));
            Assert.That(
                groundVertices,
                Has.All.Matches<Vector3>(vertex =>
                    Math.Abs(vertex.Y - (vertex.X * 0.25f - TerrainMeshBoundarySealer.GroundOverlapMetres)) < 0.0001f),
                "Every apron endpoint should overlap its ground height at the outward landing point.");
            Assert.That(
                groundVertices.Select(vertex => vertex.Y).Distinct().Count(),
                Is.GreaterThan(1),
                "The outward apron endpoints should sample their local, varying ground height.");
            Assert.That(
                groundVertices.Any(vertex => Math.Abs(vertex.X) > 2.0f),
                Is.True,
                "The apron should reach outward from the original hill boundary.");
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

    [Test]
    public void LowExteriorVerticesFollowVaryingGroundHeight()
    {
        var source = new TerrainSourceTriangle(
            new Vector3(-2.0f, 0.85f, -1.0f),
            new Vector3(0.0f, 4.0f, 2.0f),
            new Vector3(2.0f, 0.85f, -1.0f));

        var derived = TerrainMeshDeriver.Build(new[] { source }, subdivisions: 1);
        var snapped = TerrainMeshDeriver.SnapLowExteriorVertices(
            derived,
            position => position.X * 0.25f,
            maximumHeightAboveGround: 2.0f,
            out var snappedCount);

        Assert.Multiple(() =>
        {
            Assert.That(snappedCount, Is.EqualTo(2));
            Assert.That(snapped.Vertices.Any(vertex => Math.Abs(vertex.Y + 0.5f) < 0.0001f), Is.True);
            Assert.That(snapped.Vertices.Any(vertex => Math.Abs(vertex.Y - 0.5f) < 0.0001f), Is.True);
        });
    }

    [Test]
    public void ShadowHeightRelaxationMovesOnlyInteriorVertices()
    {
        var vertices = new[]
        {
            new Vector3(-1.0f, 0.0f, -1.0f),
            new Vector3(-1.0f, 0.0f, 1.0f),
            new Vector3(1.0f, 0.0f, 1.0f),
            new Vector3(1.0f, 0.0f, -1.0f),
            new Vector3(0.0f, 10.0f, 0.0f)
        };
        var terrain = new DerivedTerrainMesh(
            vertices,
            Enumerable.Repeat(Vector3.UnitY, vertices.Length).ToArray(),
            new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 },
            new[]
            {
                new DerivedTerrainBoundaryEdge(0, 1),
                new DerivedTerrainBoundaryEdge(1, 2),
                new DerivedTerrainBoundaryEdge(2, 3),
                new DerivedTerrainBoundaryEdge(3, 0)
            });

        var relaxed = TerrainMeshDeriver.RelaxInteriorHeights(
            terrain,
            iterations: 1,
            strength: 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(relaxed.Vertices[4].Y, Is.EqualTo(5.0f).Within(0.0001f));
            for (var index = 0; index < 4; index++)
            {
                Assert.That(relaxed.Vertices[index], Is.EqualTo(vertices[index]));
            }
        });
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second) =>
        new Vector2(first.X - second.X, first.Z - second.Z).Length();
}
