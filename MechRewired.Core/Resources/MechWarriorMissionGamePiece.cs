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
/// Resolves one scenario game piece to its team, spawn point, chassis and MEK configuration.
/// </summary>
/// <remarks>
/// This joins original resources by group ID while leaving rendering and simulation independent of a specific mission.
/// </remarks>
public sealed record MechWarriorMissionGamePiece(
    MechWarriorProjectEntry SourceEntry,
    MechWarriorGamePieceSpecification Specification,
    MechWarriorMissionStar Star,
    MechWarriorWorldNavPoint SpawnPoint,
    MechWarriorProjectEntry ChassisEntry,
    MechWarriorProjectEntry ConfigurationEntry);
