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
/// Links a decoded navigation point to its original BWD resource name.
/// </summary>
/// <remarks>
/// The resource name lets navigation events satisfy mission-table objectives without matching display text.
/// </remarks>
public sealed record MechWarriorMissionNavigationPoint(
    string ResourceName,
    MechWarriorWorldNavPoint Point);
