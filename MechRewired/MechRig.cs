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

/// <summary>
/// Drives the shared procedural gait used by player and enemy BattleMechs.
/// </summary>
/// <remarks>
/// Original chassis part names identify the articulated pieces. Phase advances from actual distance travelled,
/// keeping animation, cockpit motion and footfall events synchronized even when terrain changes speed.
/// </remarks>
public partial class MechRig : Node
{
    private const float UpperLegSwingDegrees = 28.0f;
    private const float LowerLegBendDegrees = 34.0f;
    private const float ToeCompensationDegrees = 14.0f;

    private readonly List<RigPart> m_parts = [];
    private readonly List<FootSupport> m_footSupports = [];
    private readonly MechGait m_gait = new();

    public float Phase => (float)m_gait.Phase;

    public float Weight => (float)m_gait.Weight;

    /// <summary>
    /// Registers an authored BWD joint node using its original or semantic mesh name.
    /// </summary>
    public bool RegisterPart(Node3D node, string partName)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        if (!TryClassify(partName, out var kind))
        {
            return false;
        }

        m_parts.Add(new RigPart(node, node.Rotation, kind));
        return true;
    }

    /// <summary>
    /// Registers the actual support geometry of a toe mesh for terrain-contact evaluation.
    /// </summary>
    public void RegisterFootMesh(MeshInstance3D mesh, string partName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        if (!TryClassify(partName, out var kind) ||
            kind is not (PartKind.LeftToe or PartKind.RightToe) ||
            mesh.Mesh == null)
        {
            return;
        }

        var vertices = new List<Vector3>();
        for (var surfaceIndex = 0; surfaceIndex < mesh.Mesh.GetSurfaceCount(); surfaceIndex++)
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(surfaceIndex);
            vertices.AddRange(arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array());
        }

        if (vertices.Count > 0)
        {
            m_footSupports.Add(new FootSupport(mesh, kind, vertices.ToArray()));
        }
    }

    /// <summary>
    /// Calculates the chassis elevation needed to keep all registered toe support vertices within
    /// the requested intentional terrain penetration.
    /// </summary>
    public float CalculateRequiredChassisElevation(
        Func<Vector3, float?> terrainHeightProvider,
        float currentElevation,
        float allowedPenetrationMeters)
    {
        ArgumentNullException.ThrowIfNull(terrainHeightProvider);
        ArgumentOutOfRangeException.ThrowIfNegative(currentElevation);
        ArgumentOutOfRangeException.ThrowIfNegative(allowedPenetrationMeters);

        var requiredElevation = 0.0f;
        var poseWeight = Mathf.Clamp(Weight / (float)MechGait.MaximumPoseSpeedFraction, 0.0f, 1.0f);
        foreach (var support in m_footSupports)
        {
            if (!GodotObject.IsInstanceValid(support.Mesh))
            {
                continue;
            }

            // The raised foot must be free to travel. Only the planted foot can determine
            // chassis elevation; otherwise a long toe assembly makes the entire mech hover.
            if (GetLift(support.Kind) * poseWeight > 0.02f)
            {
                continue;
            }

            foreach (var vertex in support.Vertices)
            {
                var worldPosition = support.Mesh.GlobalTransform * vertex;
                var terrainHeight = terrainHeightProvider(worldPosition);
                if (!terrainHeight.HasValue)
                {
                    continue;
                }

                requiredElevation = Mathf.Max(
                    requiredElevation,
                    currentElevation + terrainHeight.Value - worldPosition.Y - allowedPenetrationMeters);
            }
        }

        return Mathf.Max(0.0f, requiredElevation);
    }

    private float GetLift(PartKind kind) => kind switch
    {
        PartKind.LeftToe => Mathf.Max(0.0f, Mathf.Cos(Phase)),
        PartKind.RightToe => Mathf.Max(0.0f, -Mathf.Cos(Phase)),
        _ => 0.0f
    };

    /// <summary>
    /// Advances and applies a gait frame, returning true when a foot plants.
    /// </summary>
    public bool Advance(
        float signedDistanceMeters,
        float headingChangeRadians,
        float speedFraction,
        float delta)
    {
        var planted = m_gait.Advance(
            signedDistanceMeters,
            headingChangeRadians,
            speedFraction,
            delta);
        ApplyPose();
        return planted;
    }

    private void ApplyPose()
    {
        var poseWeight = Mathf.Clamp(Weight / (float)MechGait.MaximumPoseSpeedFraction, 0.0f, 1.0f);
        var leftSwing = Mathf.Sin(Phase);
        var rightSwing = -leftSwing;
        var leftLift = Mathf.Max(0.0f, Mathf.Cos(Phase));
        var rightLift = Mathf.Max(0.0f, -Mathf.Cos(Phase));
        ApplyLeg(
            PartKind.LeftUpperLeg,
            PartKind.LeftLowerLeg,
            PartKind.LeftToe,
            leftSwing,
            leftLift,
            poseWeight);
        ApplyLeg(
            PartKind.RightUpperLeg,
            PartKind.RightLowerLeg,
            PartKind.RightToe,
            rightSwing,
            rightLift,
            poseWeight);
    }

    private void ApplyLeg(
        PartKind upperKind,
        PartKind lowerKind,
        PartKind toeKind,
        float swing,
        float lift,
        float poseWeight)
    {
        var uppers = m_parts.Where(part => part.Kind == upperKind).ToArray();
        var lowers = m_parts.Where(part => part.Kind == lowerKind).ToArray();
        var toes = m_parts.Where(part => part.Kind == toeKind).ToArray();
        if (uppers.Length == 0)
        {
            return;
        }

        var upperPitch = Mathf.DegToRad(swing * UpperLegSwingDegrees) * poseWeight;
        var lowerPitch = Mathf.DegToRad(-lift * LowerLegBendDegrees) * poseWeight;
        var toePitch = Mathf.DegToRad(-swing * ToeCompensationDegrees) * poseWeight;

        foreach (var upper in uppers)
        {
            SetPose(upper, upperPitch);
        }

        foreach (var lower in lowers)
        {
            SetPose(lower, lowerPitch);
        }

        foreach (var toe in toes)
        {
            SetPose(toe, toePitch);
        }
    }

    private void SetPose(RigPart part, float pitch)
    {
        if (!GodotObject.IsInstanceValid(part.Node) ||
            !GodotObject.IsInstanceValid(GetParent()) ||
            !GetParent().IsAncestorOf(part.Node))
        {
            return;
        }

        part.Node.Rotation = part.RestRotation + new Vector3(pitch, 0.0f, 0.0f);
    }

    private static bool TryClassify(string name, out PartKind kind)
    {
        var baseName = name.EndsWith(".WTB", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
        var normalized = new string(baseName.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (normalized.Contains("LEFTUPPERLEG") || normalized.EndsWith("LULEG", StringComparison.Ordinal))
        {
            kind = PartKind.LeftUpperLeg;
            return true;
        }

        if (normalized.Contains("RIGHTUPPERLEG") || normalized.EndsWith("RULEG", StringComparison.Ordinal))
        {
            kind = PartKind.RightUpperLeg;
            return true;
        }

        if (normalized.Contains("LEFTLOWERLEG") || normalized.EndsWith("LLLEG", StringComparison.Ordinal) ||
            normalized.EndsWith("LKNEE", StringComparison.Ordinal))
        {
            kind = PartKind.LeftLowerLeg;
            return true;
        }

        if (normalized.Contains("RIGHTLOWERLEG") || normalized.EndsWith("RLLEG", StringComparison.Ordinal) ||
            normalized.EndsWith("RKNEE", StringComparison.Ordinal))
        {
            kind = PartKind.RightLowerLeg;
            return true;
        }

        if (normalized.Contains("LEFTFRONTTOE") || normalized.Contains("LEFTREARTOE") ||
            normalized.EndsWith("LFTOE", StringComparison.Ordinal) || normalized.EndsWith("LRTOE", StringComparison.Ordinal))
        {
            kind = PartKind.LeftToe;
            return true;
        }

        if (normalized.EndsWith("LLTOE", StringComparison.Ordinal) ||
            normalized.EndsWith("LFOOT", StringComparison.Ordinal))
        {
            kind = PartKind.LeftToe;
            return true;
        }

        if (normalized.Contains("RIGHTFRONTTOE") || normalized.Contains("RIGHTREARTOE") ||
            normalized.EndsWith("RFTOE", StringComparison.Ordinal) || normalized.EndsWith("RRTOE", StringComparison.Ordinal))
        {
            kind = PartKind.RightToe;
            return true;
        }

        if (normalized.EndsWith("RLTOE", StringComparison.Ordinal) ||
            normalized.EndsWith("RFOOT", StringComparison.Ordinal))
        {
            kind = PartKind.RightToe;
            return true;
        }

        kind = default;
        return false;
    }

    private sealed record RigPart(Node3D Node, Vector3 RestRotation, PartKind Kind);

    private sealed record FootSupport(MeshInstance3D Mesh, PartKind Kind, Vector3[] Vertices);

    private enum PartKind
    {
        LeftUpperLeg,
        RightUpperLeg,
        LeftLowerLeg,
        RightLowerLeg,
        LeftToe,
        RightToe
    }
}
