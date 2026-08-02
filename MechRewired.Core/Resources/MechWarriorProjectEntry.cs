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
/// Describes one named resource inside an original MechWarrior 2 project archive.
/// </summary>
/// <remarks>
/// Offsets and sizes refer only to the resource payload; the archive's local file header is excluded.
/// </remarks>
public sealed record MechWarriorProjectEntry(string DirectoryName, string Name, long Offset, int Size)
{
    public string Path => $"{DirectoryName}/{Name}";
}
