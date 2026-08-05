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
/// Describes one mission group from a scenario BWD <c>STAR</c> tag.
/// </summary>
/// <remarks>
/// Group IDs are implicit in source order and link the star to GPS and NAVP records.
/// </remarks>
public sealed record MechWarriorMissionStar(
    int GroupId,
    int AllianceId,
    MechWarriorMissionDisposition Disposition,
    string FormationName);

/// <summary>
/// Identifies how a mission star relates to the player's star.
/// </summary>
public enum MechWarriorMissionDisposition
{
    Friendly = 0,
    Hostile = 1,
    Neutral = 2
}
