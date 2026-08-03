// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Missions;

/// <summary>
/// Identifies a reusable action that can satisfy a mission objective.
/// </summary>
/// <remarks>
/// Objective kinds remain independent of any particular battlefield or host engine.
/// </remarks>
public enum MissionObjectiveKind
{
    Destroy,
    Inspect,
    ReachNavigationPoint,
    Extract
}
