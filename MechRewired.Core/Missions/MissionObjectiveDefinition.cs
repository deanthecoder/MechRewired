// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Resources;

namespace MechRewired.Missions;

/// <summary>
/// Defines one data-driven objective and the action needed to satisfy it.
/// </summary>
/// <remarks>
/// Definitions contain original resource links but no Godot state.
/// </remarks>
public sealed record MissionObjectiveDefinition(
    string Id,
    string Description,
    MissionObjectiveKind Kind,
    string TargetResourceName,
    bool IsOptional,
    IReadOnlyList<string> PrerequisiteIds,
    MechWarriorMissionResourceReference SuccessReport);
