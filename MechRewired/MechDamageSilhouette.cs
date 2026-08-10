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
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>
/// Holds mutually exclusive section masks derived from one original MW2 monochrome damage outline.
/// </summary>
public sealed record MechDamageSilhouette(
    int Width,
    int Height,
    IReadOnlyDictionary<MechDamageSection, Texture2D> SectionMasks);
