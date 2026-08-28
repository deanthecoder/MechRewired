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

/// <summary>Describes one point in an authored MW2 world path.</summary>
public sealed record MechWarriorWorldPathPoint(
    Vector3 Position,
    Vector3 RotationDegrees,
    int TravelTicks)
{
    /// <summary>The original simulation advances authored path timing at 182 ticks per second.</summary>
    public const float SourceTicksPerSecond = 182.0f;

    public float TravelSeconds => TravelTicks / SourceTicksPerSecond;
}
