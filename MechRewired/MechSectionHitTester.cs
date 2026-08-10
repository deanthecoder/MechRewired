// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
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
            if (!GodotObject.IsInstanceValid(mesh) || mesh.Mesh == null)
            {
                continue;
            }

            for (var surfaceIndex = 0; surfaceIndex < mesh.Mesh.GetSurfaceCount(); surfaceIndex++)
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(surfaceIndex);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                var triangleCount = indices.Length > 0 ? indices.Length / 3 : vertices.Length / 3;
                for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
                {
                    var aIndex = indices.Length > 0 ? indices[triangleIndex * 3] : triangleIndex * 3;
                    var bIndex = indices.Length > 0 ? indices[triangleIndex * 3 + 1] : triangleIndex * 3 + 1;
                    var cIndex = indices.Length > 0 ? indices[triangleIndex * 3 + 2] : triangleIndex * 3 + 2;
                    if (!TryIntersectRay(
                            origin,
                            direction,
                            mesh.GlobalTransform * vertices[aIndex],
                            mesh.GlobalTransform * vertices[bIndex],
                            mesh.GlobalTransform * vertices[cIndex],
                            out var distance) ||
                        distance >= nearestDistance)
                    {
                        continue;
                    }

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

    private static bool TryIntersectRay(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance)
    {
        const float epsilon = 0.000001f;
        var edge1 = b - a;
        var edge2 = c - a;
        var perpendicular = direction.Cross(edge2);
        var determinant = edge1.Dot(perpendicular);
        if (Mathf.Abs(determinant) < epsilon)
        {
            distance = 0.0f;
            return false;
        }

        var inverseDeterminant = 1.0f / determinant;
        var originOffset = origin - a;
        var u = originOffset.Dot(perpendicular) * inverseDeterminant;
        if (u is < 0.0f or > 1.0f)
        {
            distance = 0.0f;
            return false;
        }

        var cross = originOffset.Cross(edge1);
        var v = direction.Dot(cross) * inverseDeterminant;
        if (v < 0.0f || u + v > 1.0f)
        {
            distance = 0.0f;
            return false;
        }

        distance = edge2.Dot(cross) * inverseDeterminant;
        return distance >= 0.0f;
    }
}
