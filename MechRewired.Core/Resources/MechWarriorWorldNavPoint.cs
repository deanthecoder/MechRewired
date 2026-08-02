// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Numerics;

namespace MechRewired.Resources;

/// <summary>
/// Describes a mission navigation or deployment point stored in a BWD resource.
/// </summary>
public sealed record MechWarriorWorldNavPoint(
    Vector3 Position,
    int StartingAngle,
    bool Targetable,
    int GroupId,
    int Radius,
    int ActionFlags,
    string Description);
