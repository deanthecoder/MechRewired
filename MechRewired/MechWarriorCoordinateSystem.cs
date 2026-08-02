// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>
/// Converts MW2's source coordinates to Godot's opposite-handed coordinate system.
/// </summary>
public static class MechWarriorCoordinateSystem
{
    public static Vector3 ToGodotPosition(System.Numerics.Vector3 vector) =>
        new(vector.X, vector.Y, -vector.Z);

    public static Vector3 ToGodotRotation(System.Numerics.Vector3 rotationDegrees) =>
        new(-rotationDegrees.X, -rotationDegrees.Y, rotationDegrees.Z);

    public static Vector3 ToGodotScale(System.Numerics.Vector3 scale) =>
        new(scale.X, scale.Y, scale.Z);

    public static Vector3 ToSourcePosition(Vector3 vector) =>
        new(vector.X, vector.Y, -vector.Z);

    public static Vector3 ToSourceRotation(Vector3 rotationDegrees) =>
        new(-rotationDegrees.X, -rotationDegrees.Y, rotationDegrees.Z);
}
