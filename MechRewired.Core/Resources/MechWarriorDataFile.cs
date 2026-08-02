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
/// Defines original MechWarrior 2 data files recognized by the engine.
/// </summary>
/// <remarks>
/// The DOS project archive is the reference dataset for the first playable milestone.
/// </remarks>
public static class MechWarriorDataFile
{
    public const string ProjectArchive = "MW2.PRJ";

    public static IReadOnlyList<string> RequiredDosFiles { get; } =
    [
        ProjectArchive
    ];
}
