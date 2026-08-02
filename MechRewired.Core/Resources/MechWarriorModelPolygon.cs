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
/// Describes one polygon and its source material selectors from a MechWarrior 2 WTB model.
/// </summary>
/// <remarks>
/// The palette index supplies DOS flat shading. The material index selects textured-edition material data.
/// Vertex indices preserve the source winding; coordinate-system conversion belongs to the rendering host.
/// </remarks>
public sealed record MechWarriorModelPolygon(
    byte MaterialIndex,
    byte PaletteIndex,
    IReadOnlyList<int> VertexIndices);
