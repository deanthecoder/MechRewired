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

namespace MechRewired.Resources;

/// <summary>
/// Defines the highest-detail original Timber Wolf model assembly.
/// </summary>
/// <remarks>
/// The rest translations reproduce the component placement used by MechWarrior 2 model viewers.
/// Articulation pivots will be added when mech animation begins.
/// </remarks>
public static class TimberWolfModelDefinition
{
    private static readonly Vector3 TorsoTranslation = new(0.0f, 0.45f, 0.9f);

    public static IReadOnlyList<MechWarriorModelPartDefinition> Parts { get; } =
    [
        new("Hips", "POLY/TW1_HIPS.WTB", Vector3.Zero),
        new("Torso", "POLY/TW1_HEAD.WTB", TorsoTranslation),
        new("Windshield", "POLY/TW1WINSH.WTB", new Vector3(0.08f, 1.99f, 1.22f)),
        new(
            "RightDecal",
            "POLY/TW1DECLR.WTB",
            TorsoTranslation + new Vector3(3.23f, 3.69f, -0.39f)),
        new(
            "LeftDecal",
            "POLY/TW1DECLL.WTB",
            TorsoTranslation + new Vector3(-3.36f, 3.69f, -0.39f)),
        new("LeftArm", "POLY/TW1_LARM.WTB", new Vector3(-2.0f, 2.5f, 0.0f)),
        new("RightArm", "POLY/TW1_RARM.WTB", new Vector3(2.0f, 2.5f, 0.0f)),
        new("LeftUpperLeg", "POLY/TW1LULEG.WTB", new Vector3(-1.2f, -0.6f, -0.3f)),
        new("LeftLowerLeg", "POLY/TW1LLLEG.WTB", new Vector3(-1.9f, -2.7f, -2.0f)),
        new("LeftFrontToe", "POLY/TW1LFTOE.WTB", new Vector3(-2.15f, -6.02f, -0.5f)),
        new("LeftRearToe", "POLY/TW1LRTOE.WTB", new Vector3(-2.5f, -6.02f, 1.0f)),
        new("RightUpperLeg", "POLY/TW1RULEG.WTB", new Vector3(1.2f, -0.6f, -0.3f)),
        new("RightLowerLeg", "POLY/TW1RLLEG.WTB", new Vector3(1.9f, -2.7f, -2.0f)),
        new("RightFrontToe", "POLY/TW1RFTOE.WTB", new Vector3(2.1f, -6.02f, -0.5f)),
        new("RightRearToe", "POLY/TW1RRTOE.WTB", new Vector3(2.1f, -6.02f, 1.0f))
    ];
}
