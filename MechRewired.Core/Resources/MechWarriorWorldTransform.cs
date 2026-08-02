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
/// Describes an MW2 world object's scale, rotation, and position.
/// </summary>
/// <remarks>
/// Translation is converted from source centimeters while rotation remains in degrees.
/// </remarks>
public sealed record MechWarriorWorldTransform(Vector3 Scale, Vector3 RotationDegrees, Vector3 Translation)
{
    public static MechWarriorWorldTransform Identity { get; } = new(Vector3.One, Vector3.Zero, Vector3.Zero);
}
