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
/// Owns the shared material path for MW2 terrain chunks and the implicit ground fill.
/// The first pass deliberately reproduces their existing palette color and roughness;
/// later terrain detail can therefore be added in one place without changing source identity.
/// </summary>
public static class TerrainSurfaceMaterial
{
    public const float Roughness = 0.9f;

    public static ShaderMaterial Create() => new()
    {
        Shader = new Shader
        {
            Code =
                $$"""
                shader_type spatial;
                render_mode cull_back;

                void fragment() {
                    ALBEDO = COLOR.rgb;
                    METALLIC = 0.0;
                    ROUGHNESS = {{Roughness.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}};
                }
                """
        }
    };
}
