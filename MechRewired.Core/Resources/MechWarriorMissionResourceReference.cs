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
/// Identifies a directory-local resource referenced by an MW2 mission-table record.
/// </summary>
/// <remarks>
/// Both the index and stored name are retained so format discoveries can be validated against the archive.
/// </remarks>
public sealed record MechWarriorMissionResourceReference(int? ResourceIndex, string Name);
