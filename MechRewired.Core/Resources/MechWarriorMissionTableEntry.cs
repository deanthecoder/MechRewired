// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Resources;

/// <summary>Actions authored by MW2's mission editor for one objective record.</summary>
public enum MechWarriorMissionAction : ushort
{
    None = 0x0000,
    Destroy = 0x0002,
    Defend = 0x0004,
    Recon = 0x0008,
    Begin = 0x0010,
    Return = 0x0020,
    GoTo = 0x0100,
    Wait = 0x0200,
    Sleep = 0x0400,
    Shutdown = 0x0800,
    Leave = 0x2000
}

/// <summary>Mission-control operations stored beside the normal action field.</summary>
public enum MechWarriorMissionControlAction : ushort
{
    None = 0x0000,
    FailMission = 0x0002,
    ShowObjective = 0x0004,
    SucceedObjective = 0x0010
}

/// <summary>How multiple activation records combine.</summary>
public enum MechWarriorMissionConditionLogic
{
    Any = 0,
    All = 1
}

/// <summary>
/// Describes one fixed-size record from an MW2 BWD <c>MTBL</c> tag.
/// </summary>
/// <remarks>
/// The layout mirrors the 151-byte record emitted by MW2's original mission tooling.
/// </remarks>
public sealed record MechWarriorMissionTableEntry(
    int Index,
    MechWarriorMissionAction Action,
    MechWarriorMissionControlAction ControlAction,
    char VisibilityCode,
    MechWarriorMissionConditionLogic ConditionLogic,
    IReadOnlyList<MechWarriorMissionCondition> ActivationConditions,
    int TimeSeconds,
    char GoalClass,
    byte GoalFlags,
    byte MechsPerTarget,
    byte DoNotDisturb,
    MechWarriorMissionResourceReference SuccessReport,
    MechWarriorMissionResourceReference FailureReport,
    MechWarriorMissionResourceReference Target,
    short TargetObjectiveMarker,
    short? TargetObjectiveIndex,
    string Description);
