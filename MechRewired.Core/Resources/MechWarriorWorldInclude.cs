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
/// Describes one BWD file included by another BWD world resource.
/// </summary>
/// <remarks>
/// The resource index is local to the archive's BWD directory.
/// </remarks>
public sealed record MechWarriorWorldInclude(int ResourceIndex, string Name, MechWarriorWorldTransform Transform);
