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
/// Associates one rendered diagnostic triangle with its original level resource.
/// </summary>
/// <remarks>
/// Vertices use Godot world coordinates so camera rays can be queried without physics collision bodies.
/// </remarks>
public sealed record DebugTriangle(
    string ResourcePath,
    int ObjectId,
    int ModelIndex,
    int PolygonIndex,
    Vector3 A,
    Vector3 B,
    Vector3 C);
