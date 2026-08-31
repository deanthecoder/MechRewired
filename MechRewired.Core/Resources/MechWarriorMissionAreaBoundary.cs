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

/// <summary>Identifies one hidden archive-authored mission-area boundary circle.</summary>
/// <remarks>
/// LVE markers use ordinary BWD navigation-point geometry but trigger when the player exits their radius.
/// </remarks>
public sealed record MechWarriorMissionAreaBoundary(
    string ResourceName,
    MechWarriorWorldNavPoint Point);
