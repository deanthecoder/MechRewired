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
    private const float SwingFootLiftMeters = 0.55f;

    private readonly List<RigPart> m_parts = [];
    private readonly MechGait m_gait = new();

    public float Phase => (float)m_gait.Phase;

    public float Weight => (float)m_gait.Weight;

    /// <summary>
    /// Registers one solid or wireframe visual using its original or semantic part name.
    /// </summary>
    public bool RegisterPart(Node3D node, string partName)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        if (!TryClassify(partName, out var kind))
        {
            return false;
        }

        m_parts.Add(new RigPart(node, node.Position, node.Rotation, kind));
        return true;
    }

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

        // Solid and wireframe copies share their rest position, so either gives us the authored joint pivot.
        var upperPivot = uppers[0].RestPosition;
        var lowerPivot = lowers.Length == 0 ? upperPivot : lowers[0].RestPosition;
        var upperPitch = Mathf.DegToRad(swing * UpperLegSwingDegrees) * poseWeight;
        var lowerPitch = Mathf.DegToRad(-lift * LowerLegBendDegrees) * poseWeight;
        var toePitch = Mathf.DegToRad(-swing * ToeCompensationDegrees) * poseWeight;

        foreach (var upper in uppers)
        {
            SetPose(upper, upper.RestPosition, upperPitch);
        }

        var articulatedLowerPivot = RotateAroundX(lowerPivot, upperPivot, upperPitch);
        var toePoses = toes
            .Select(toe =>
            {
                var upperPosition = RotateAroundX(toe.RestPosition, upperPivot, upperPitch);
                var position = RotateAroundX(upperPosition, articulatedLowerPivot, lowerPitch);
                return (Part: toe, Position: position);
            })
            .ToArray();
        var groundCorrection = toePoses.Length == 0
            ? 0.0f
            : toePoses.Max(pose => pose.Part.RestPosition.Y - pose.Position.Y);
        var verticalCorrection = Mathf.Max(0.0f, groundCorrection) +
                                 lift * SwingFootLiftMeters * poseWeight;
        var verticalOffset = Vector3.Up * verticalCorrection;

        foreach (var lower in lowers)
        {
            var position = RotateAroundX(lower.RestPosition, upperPivot, upperPitch) + verticalOffset;
            SetPose(lower, position, upperPitch + lowerPitch);
        }

        foreach (var toePose in toePoses)
        {
            SetPose(
                toePose.Part,
                toePose.Position + verticalOffset,
                upperPitch + lowerPitch + toePitch);
        }
    }

    private static Vector3 RotateAroundX(Vector3 position, Vector3 pivot, float radians) =>
        pivot + (position - pivot).Rotated(Vector3.Right, radians);

    private static void SetPose(RigPart part, Vector3 position, float pitch)
    {
        part.Node.Position = position;
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

    private sealed record RigPart(Node3D Node, Vector3 RestPosition, Vector3 RestRotation, PartKind Kind);

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
