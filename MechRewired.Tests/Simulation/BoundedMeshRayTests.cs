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
public sealed class BoundedMeshRayTests
{
    [TestCase(0, 0, true)]
    [TestCase(1, 1, true)]
    [TestCase(2, 0, false)]
    public void ParallelAxesAndBoundaryRaysAreConservative(float x, float y, bool expected)
    {
        Assert.That(BoundedMeshRay.IntersectsBounds(new(x, y, 0), Vector3.UnitZ,
            new(-1, -1, 5), new(1, 1, 5), 20), Is.EqualTo(expected));
    }

    [Test]
    public void RejectsBehindOriginAndBeyondNearestHit()
    {
        Assert.That(BoundedMeshRay.IntersectsBounds(Vector3.Zero, Vector3.UnitZ, new(-1, -1, -5), new(1, 1, -4), 20), Is.False);
        Assert.That(BoundedMeshRay.IntersectsBounds(Vector3.Zero, Vector3.UnitZ, new(-1, -1, 5), new(1, 1, 6), 4), Is.False);
        Assert.That(BoundedMeshRay.IntersectsBounds(new(0, 0, 5.5f), Vector3.UnitZ, new(-1, -1, 5), new(1, 1, 6), 4), Is.True);
    }

    [TestCase(2.0f, 3.0f, 4.0f)]
    [TestCase(-2.0f, 0.5f, 3.0f)]
    [TestCase(0.001f, 100.0f, 0.1f)]
    public void LocalRayPreservesWorldHitParameterUnderRotationAndNonuniformScale(float x, float y, float z)
    {
        var transform = Matrix4x4.CreateScale(x, y, z) * Matrix4x4.CreateRotationY(0.73f) * Matrix4x4.CreateTranslation(20, -3, 10);
        Matrix4x4.Invert(transform, out var inverse);
        var a = new Vector3(-2, -2, 5);
        var b = new Vector3(2, -2, 5);
        var c = new Vector3(0, 2, 5);
        var origin = Vector3.Transform(Vector3.Zero, transform);
        var direction = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, transform));
        var localOrigin = Vector3.Transform(origin, inverse);
        var localDirection = Vector3.TransformNormal(direction, inverse);
        Assert.That(BoundedMeshRay.TryIntersectTriangle(origin, direction,
            Vector3.Transform(a, transform), Vector3.Transform(b, transform), Vector3.Transform(c, transform), out var worldParameter), Is.True);
        Assert.That(BoundedMeshRay.TryIntersectTriangle(localOrigin, localDirection, a, b, c, out var localParameter,
            0.000001f / MathF.Abs(transform.GetDeterminant())), Is.True);
        Assert.That(localParameter, Is.EqualTo(worldParameter).Within(0.001f));
        Assert.That(BoundedMeshRay.IntersectsBounds(localOrigin, localDirection, new(-2, -2, 5), new(2, 2, 5), worldParameter + 0.01f), Is.True);
    }

    [Test]
    public void TriangleKeepsTwoSidedExactHitAndRejectsMiss()
    {
        var a = new Vector3(-2, -2, 5);
        var b = new Vector3(2, -2, 5);
        var c = new Vector3(0, 2, 5);
        Assert.That(BoundedMeshRay.TryIntersectTriangle(Vector3.Zero, Vector3.UnitZ, c, b, a, out var parameter), Is.True);
        Assert.That(parameter, Is.EqualTo(5));
        Assert.That(BoundedMeshRay.TryIntersectTriangle(new(10, 0, 0), Vector3.UnitZ, a, b, c, out _), Is.False);
    }
}
