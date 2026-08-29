// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Resources;

/// <summary>Identifies a syntactically valid BWD tag which is not yet decoded semantically.</summary>
public sealed record MechWarriorUnknownTag(string Name, int Offset, int Size);
