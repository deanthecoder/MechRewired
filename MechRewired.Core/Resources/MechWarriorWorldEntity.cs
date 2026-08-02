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
/// Describes gameplay metadata attached to an object assembly by a BWD GT tag.
/// </summary>
/// <remarks>
/// Destroyed object IDs identify an alternative representation within the same BWD resource.
/// </remarks>
public sealed record MechWarriorWorldEntity(
    int ObjectId,
    int? DestroyedObjectId,
    int Health,
    string Description);
