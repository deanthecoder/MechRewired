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

/// <summary>
/// Describes one fixed-size record from an MW2 BWD <c>MTBL</c> tag.
/// </summary>
/// <remarks>
/// Known resource references and display fields are named while undecoded control values remain explicit.
/// </remarks>
public sealed record MechWarriorMissionTableEntry(
    int Index,
    int TriggerFlags,
    char VisibilityCode,
    MechWarriorMissionCondition Trigger,
    IReadOnlyList<MechWarriorMissionCondition> Conditions,
    char GoalClass,
    byte GoalFlags,
    ushort UnknownGoalValue,
    MechWarriorMissionResourceReference SuccessReport,
    MechWarriorMissionResourceReference FailureReport,
    MechWarriorMissionResourceReference Target,
    int UnknownTailValue,
    string Description);
