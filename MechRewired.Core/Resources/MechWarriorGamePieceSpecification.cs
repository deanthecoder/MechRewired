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
/// Describes one original MW2 game-piece specification decoded from a BWD <c>GPS</c> tag.
/// </summary>
/// <remarks>
/// The record retains the authored AI ranges and identities so progressively richer behavior can replace the initial combat slice.
/// </remarks>
public sealed record MechWarriorGamePieceSpecification(
    int MechResourceIndex,
    int ChassisResourceIndex,
    int GroupId,
    int Authority,
    int Control,
    int GunnerySkill,
    int RubberbandRange,
    int SleepRange,
    int TargetRange,
    int PilotSkill,
    int ActionFlags,
    int TargetingMode,
    string ChassisName,
    string ConfigurationName,
    string DisplayName,
    string PilotName);
