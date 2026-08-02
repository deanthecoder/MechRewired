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
/// Associates a level object transform with its resolved POLY model resource.
/// </summary>
/// <remarks>
/// Keeping the archive entry intact makes model loading lazy and cacheable by the renderer.
/// </remarks>
public sealed record MechWarriorLevelObject(
    MechWarriorProjectEntry ModelEntry,
    MechWarriorWorldTransform Transform);
