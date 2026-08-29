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
/// Streams a restrained layer of warm atmospheric haze through rocky valleys.
/// </summary>
/// <remarks>
/// The original mountain terrain is sparse and strongly silhouetted. This shallow, broken volume
/// supplies near-field aerial perspective without replacing the level-authored distance fog.
/// </remarks>
public partial class GroundMountainHaze : Node3D
{
    private const int RadiusInCells = 1;
    private const float CellSpacing = 108.0f;
    private const float VolumeWidth = 122.0f;
    private const float VolumeHeight = 18.0f;
    private readonly Node3D m_observer;
    private readonly TerrainSurfaceIndex m_terrainSurface;
    private readonly Color m_tint;
    private readonly List<FogVolume> m_volumes = [];

    /// <summary>
    /// Creates terrain-hugging valley haze around the supplied observer.
    /// </summary>
    public GroundMountainHaze(Node3D observer, TerrainSurfaceIndex terrainSurface, Color tint)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(terrainSurface);
        m_observer = observer;
        m_terrainSurface = terrainSurface;
        m_tint = tint;
    }

    public override void _Ready()
    {
        var material = CreateHazeMaterial(m_tint);
        for (var z = -RadiusInCells; z <= RadiusInCells; z++)
        {
            for (var x = -RadiusInCells; x <= RadiusInCells; x++)
            {
                var volume = new FogVolume
                {
                    Name = $"ValleyHaze_{x}_{z}",
                    Shape = RenderingServer.FogVolumeShape.Box,
                    Size = new Vector3(VolumeWidth, VolumeHeight, VolumeWidth),
                    Material = material
                };
                AddChild(volume);
                m_volumes.Add(volume);
            }
        }

        UpdateCells();
    }

    public override void _Process(double delta) => UpdateCells();

    private void UpdateCells()
    {
        if (!IsInstanceValid(m_observer))
        {
            return;
        }

        var observerPosition = m_observer.GlobalPosition;
        var index = 0;
        for (var z = -RadiusInCells; z <= RadiusInCells; z++)
        {
            for (var x = -RadiusInCells; x <= RadiusInCells; x++)
            {
                var position = new Vector3(
                    observerPosition.X + x * CellSpacing,
                    observerPosition.Y,
                    observerPosition.Z + z * CellSpacing);
                if (m_terrainSurface.TryGetHeight(position, out var terrainHeight))
                {
                    position.Y = terrainHeight + VolumeHeight * 0.30f;
                }

                m_volumes[index++].GlobalPosition = position;
            }
        }
    }

    private static ShaderMaterial CreateHazeMaterial(Color tint)
    {
        var shader = new Shader
        {
            Code = """
                shader_type fog;

                uniform vec3 haze_albedo = vec3(0.48, 0.24, 0.16);
                uniform vec2 wind_direction = vec2(0.74, 0.67);
                uniform float wind_speed = 1.8;
                uniform float haze_density = 0.042;

                float hash(vec2 point) {
                    return fract(sin(dot(point, vec2(127.1, 311.7))) * 43758.5453123);
                }

                float noise(vec2 point) {
                    vec2 cell = floor(point);
                    vec2 fraction = fract(point);
                    fraction = fraction * fraction * (3.0 - 2.0 * fraction);
                    return mix(
                        mix(hash(cell), hash(cell + vec2(1.0, 0.0)), fraction.x),
                        mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), fraction.x),
                        fraction.y);
                }

                void fog() {
                    vec2 drift = wind_direction * TIME * wind_speed;
                    vec2 world_sample = WORLD_POSITION.xz * 0.018 + drift * 0.018;
                    float broad_patch = noise(world_sample);
                    float breakup = noise(world_sample * 2.7 + vec2(17.4, -8.6));
                    float patch = smoothstep(0.34, 0.72, broad_patch) * mix(0.40, 1.0, breakup);
                    float bottom_fade = smoothstep(0.02, 0.16, UVW.y);
                    float top_fade = 1.0 - smoothstep(0.32, 0.98, UVW.y);

                    DENSITY = haze_density * patch * bottom_fade * top_fade;
                    ALBEDO = haze_albedo;
                    EMISSION = haze_albedo * 0.035;
                }
                """
        };
        var material = new ShaderMaterial { Shader = shader };
        var hazeTint = tint.Lerp(new Color(0.42f, 0.24f, 0.17f), 0.58f);
        material.SetShaderParameter("haze_albedo", new Vector3(hazeTint.R, hazeTint.G, hazeTint.B));
        return material;
    }
}
