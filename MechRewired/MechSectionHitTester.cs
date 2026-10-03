// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Runtime.CompilerServices;
using Godot;
using NumericsVector3 = System.Numerics.Vector3;
using MechRewired.Simulation;

namespace MechRewired;

public sealed record MechSectionHit(
    MechDamageSection Section,
    Vector3 Position,
    float Distance,
    bool FromRear);

/// <summary>
/// Ray-tests the original low-poly mesh triangles and maps the nearest surface to a damage section.
/// </summary>
public static class MechSectionHitTester
{
    // Keys are weak: unloading mission mesh resources also releases their copied geometry.
    // A resource edit invalidates its arrays; animated node transforms are sampled every query.
    private static readonly ConditionalWeakTable<Mesh, CachedGeometry> s_geometry = new();

    private sealed class CachedGeometry
    {
        public bool Dirty = true;
        public SurfaceGeometry[] Surfaces = Array.Empty<SurfaceGeometry>();
        public NumericsVector3 Minimum;
        public NumericsVector3 Maximum;
    }

    private sealed record SurfaceGeometry(NumericsVector3[] Vertices, int[] Indices);

    /// <summary>Warm copied collision geometry during part registration, before combat begins.</summary>
    public static void PrepareMesh(Mesh resource)
    {
        if (resource != null && GodotObject.IsInstanceValid(resource))
            GetGeometry(resource);
    }

    private static CachedGeometry GetGeometry(Mesh resource)
    {
        var geometry = s_geometry.GetValue(resource, mesh =>
        {
            var entry = new CachedGeometry();
            mesh.Changed += () => entry.Dirty = true;
            return entry;
        });
        if (!geometry.Dirty)
            return geometry;
        geometry.Minimum = new NumericsVector3(float.PositiveInfinity);
        geometry.Maximum = new NumericsVector3(float.NegativeInfinity);
        geometry.Surfaces = new SurfaceGeometry[resource.GetSurfaceCount()];
        for (var surface = 0; surface < geometry.Surfaces.Length; surface++)
        {
            var arrays = resource.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var localVertices = new NumericsVector3[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                localVertices[i] = ToNumerics(vertices[i]);
                geometry.Minimum = NumericsVector3.Min(geometry.Minimum, localVertices[i]);
                geometry.Maximum = NumericsVector3.Max(geometry.Maximum, localVertices[i]);
            }
            geometry.Surfaces[surface] = new SurfaceGeometry(localVertices, arrays[(int)Mesh.ArrayType.Index].AsInt32Array());
        }
        // Roundoff in affine transforms must never reject an exact triangle hit at an edge.
        geometry.Minimum -= new NumericsVector3(0.00001f);
        geometry.Maximum += new NumericsVector3(0.00001f);
        geometry.Dirty = false;
        return geometry;
    }

    private static NumericsVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
    private static Vector3 ToGodot(NumericsVector3 value) => new(value.X, value.Y, value.Z);

    public static bool TryFindNearest(
        Node3D mechRoot,
        IEnumerable<(MeshInstance3D Mesh, string PartName)> parts,
        Vector3 origin,
        Vector3 direction,
        out MechSectionHit hit)
    {
        hit = null;
        var nearestDistance = float.PositiveInfinity;
        MeshInstance3D nearestMesh = null;
        string nearestPartName = null;
        foreach (var (mesh, partName) in parts)
        {
            if (!GodotObject.IsInstanceValid(mesh))
                continue;
            var resource = mesh.Mesh;
            if (resource == null)
                continue;
            var geometry = GetGeometry(resource);
            var transform = mesh.GlobalTransform;
            var determinant = Mathf.Abs(transform.Basis.Determinant());
            // Singular transforms cannot be inverted. Preserve the old world-space test for them.
            var useLocal = determinant > 0.000000000001f;
            var inverse = useLocal ? transform.AffineInverse() : Transform3D.Identity;
            var rayOrigin = ToNumerics(useLocal ? inverse * origin : origin);
            var rayDirection = ToNumerics(useLocal ? inverse.Basis * direction : direction);
            if (useLocal && !BoundedMeshRay.IntersectsBounds(rayOrigin, rayDirection,
                    geometry.Minimum, geometry.Maximum, nearestDistance))
                continue;

            var determinantEpsilon = useLocal ? 0.000001f / determinant : 0.000001f;
            foreach (var surface in geometry.Surfaces)
            {
                var vertices = surface.Vertices;
                var indices = surface.Indices;
                var triangleCount = indices.Length > 0 ? indices.Length / 3 : vertices.Length / 3;
                for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
                {
                    var aIndex = indices.Length > 0 ? indices[triangleIndex * 3] : triangleIndex * 3;
                    var bIndex = indices.Length > 0 ? indices[triangleIndex * 3 + 1] : triangleIndex * 3 + 1;
                    var cIndex = indices.Length > 0 ? indices[triangleIndex * 3 + 2] : triangleIndex * 3 + 2;
                    var a = vertices[aIndex];
                    var b = vertices[bIndex];
                    var c = vertices[cIndex];
                    if (!useLocal)
                    {
                        a = ToNumerics(transform * ToGodot(a));
                        b = ToNumerics(transform * ToGodot(b));
                        c = ToNumerics(transform * ToGodot(c));
                    }
                    if (!BoundedMeshRay.TryIntersectTriangle(rayOrigin, rayDirection, a, b, c, out var distance,
                            determinantEpsilon) || distance >= nearestDistance)
                        continue;
                    nearestDistance = distance;
                    nearestMesh = mesh;
                    nearestPartName = partName;
                }
            }
        }

        if (nearestMesh == null)
        {
            return false;
        }

        var position = origin + direction * nearestDistance;
        var section = ResolveDamageSection(nearestMesh, nearestPartName, position);
        var forward = -mechRoot.GlobalBasis.Z.Normalized();
        hit = new MechSectionHit(section, position, nearestDistance, direction.Dot(forward) > 0.0f);
        return true;
    }

    private static MechDamageSection ResolveDamageSection(
        MeshInstance3D mesh,
        string partName,
        Vector3 worldPosition)
    {
        switch (MechBodySectionClassifier.Classify(partName))
        {
            case MechBodySection.LeftArm:
                return MechDamageSection.LeftArm;
            case MechBodySection.RightArm:
                return MechDamageSection.RightArm;
            case MechBodySection.LeftUpperLeg:
            case MechBodySection.LeftLowerLeg:
            case MechBodySection.LeftFoot:
                return MechDamageSection.LeftLeg;
            case MechBodySection.RightUpperLeg:
            case MechBodySection.RightLowerLeg:
            case MechBodySection.RightFoot:
                return MechDamageSection.RightLeg;
            case MechBodySection.Hips:
                return MechDamageSection.CenterTorso;
        }

        var bounds = mesh.GetAabb();
        var local = mesh.ToLocal(worldPosition);
        var horizontal = bounds.Size.X <= 0.001f
            ? 0.0f
            : (local.X - bounds.GetCenter().X) / bounds.Size.X;
        var vertical = bounds.Size.Y <= 0.001f
            ? 0.0f
            : (local.Y - bounds.Position.Y) / bounds.Size.Y;
        if (vertical >= 0.78f && Mathf.Abs(horizontal) <= 0.2f)
        {
            return MechDamageSection.Head;
        }

        return horizontal switch
        {
            < -0.2f => MechDamageSection.LeftTorso,
            > 0.2f => MechDamageSection.RightTorso,
            _ => MechDamageSection.CenterTorso
        };
    }

}
