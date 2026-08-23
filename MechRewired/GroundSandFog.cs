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
/// Streams a small, terrain-hugging field of broken volumetric sand sheets around the player.
/// </summary>
/// <remarks>
/// The shader moves its noise in world space while the overlapping volume field follows the
/// player continuously. This keeps the near sand available without visible cell-boundary pops.
/// </remarks>
public partial class GroundSandFog : Node3D
{
    private const int RadiusInCells = 1;
    private const float CellSpacing = 52.0f;
    private const float VolumeWidth = 58.0f;
    private const float DefaultVolumeHeight = 3.0f;
    private readonly Node3D m_observer;
    private readonly TerrainSurfaceIndex m_terrainSurface;
    private readonly List<FogVolume> m_volumes = [];
    private ShaderMaterial m_material;
    private float m_density = 0.20f;
    private float m_coverage = 0.50f;
    private float m_windSpeed = 10.0f;
    private float m_height = DefaultVolumeHeight;
    private float m_fill = 0.25f;
    private bool m_enabled = true;

    public GroundSandFog(Node3D observer, TerrainSurfaceIndex terrainSurface)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(terrainSurface);
        m_observer = observer;
        m_terrainSurface = terrainSurface;
    }

    public float Density
    {
        get => m_density;
        set
        {
            m_density = Mathf.Clamp(value, 0.0f, 0.50f);
            ApplyMaterialParameters();
        }
    }

    public float Coverage
    {
        get => m_coverage;
        set
        {
            m_coverage = Mathf.Clamp(value, 0.0f, 1.0f);
            ApplyMaterialParameters();
        }
    }

    public float WindSpeed
    {
        get => m_windSpeed;
        set
        {
            m_windSpeed = Mathf.Clamp(value, 0.0f, 20.0f);
            ApplyMaterialParameters();
        }
    }

    public float Height
    {
        get => m_height;
        set
        {
            m_height = Mathf.Clamp(value, 1.0f, 20.0f);
            foreach (var volume in m_volumes)
            {
                volume.Size = new Vector3(VolumeWidth, m_height, VolumeWidth);
            }

            UpdateCells();
        }
    }

    public float Fill
    {
        get => m_fill;
        set
        {
            m_fill = Mathf.Clamp(value, 0.0f, 0.50f);
            ApplyMaterialParameters();
        }
    }

    public bool Enabled
    {
        get => m_enabled;
        set
        {
            m_enabled = value;
            foreach (var volume in m_volumes)
            {
                volume.Visible = m_enabled;
            }
        }
    }

    public override void _Ready()
    {
        m_material = CreateSandMaterial();
        for (var z = -RadiusInCells; z <= RadiusInCells; z++)
        {
            for (var x = -RadiusInCells; x <= RadiusInCells; x++)
            {
                var volume = new FogVolume
                {
                    Name = $"SandSheet_{x}_{z}",
                    Shape = RenderingServer.FogVolumeShape.Box,
                    Size = new Vector3(VolumeWidth, m_height, VolumeWidth),
                    Material = m_material
                };
                AddChild(volume);
                m_volumes.Add(volume);
            }
        }

        ApplyMaterialParameters();
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
                    // Let the box extend slightly below the surface, keeping the dense lower
                    // sheet planted on uneven terrain instead of looking like a lifted cloud.
                    position.Y = terrainHeight + m_height * 0.24f;
                }

                m_volumes[index++].GlobalPosition = position;
            }
        }
    }

    private static ShaderMaterial CreateSandMaterial()
    {
        var shader = new Shader
        {
            Code = """
                shader_type fog;

                uniform vec3 wind_direction = vec3(0.82, 0.0, 0.57);
                uniform float wind_speed = 10.0;
                uniform float sand_density = 0.20;
                uniform float sand_coverage = 0.50;
                uniform float sand_fill = 0.25;
                uniform vec3 sand_albedo = vec3(0.72, 0.49, 0.27);

                float hash(vec2 p) {
                    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
                }

                float noise(vec2 p) {
                    vec2 cell = floor(p);
                    vec2 fraction = fract(p);
                    fraction = fraction * fraction * (3.0 - 2.0 * fraction);
                    return mix(
                        mix(hash(cell), hash(cell + vec2(1.0, 0.0)), fraction.x),
                        mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), fraction.x),
                        fraction.y);
                }

                void fog() {
                    vec2 drift = wind_direction.xz * TIME * wind_speed;
                    vec2 sample_position = WORLD_POSITION.xz * 0.055 + drift * 0.055;
                    float broad_patch = noise(sample_position * 0.52);
                    float fine_breakup = noise(sample_position * 1.9 + vec2(19.4, 7.1));
                    float coverage_threshold = mix(0.78, 0.26, sand_coverage);
                    float patch = smoothstep(coverage_threshold, coverage_threshold + 0.26, broad_patch);
                    patch *= mix(0.32, 1.0, fine_breakup);
                    float height_fade = 1.0 - smoothstep(0.10, 0.96, UVW.y);

                    DENSITY = sand_density * patch * height_fade;
                    ALBEDO = sand_albedo;
                    EMISSION = sand_albedo * sand_fill;
                }
                """
        };
        return new ShaderMaterial { Shader = shader };
    }

    private void ApplyMaterialParameters()
    {
        if (m_material == null)
        {
            return;
        }

        m_material.SetShaderParameter("sand_density", m_density);
        m_material.SetShaderParameter("sand_coverage", m_coverage);
        m_material.SetShaderParameter("wind_speed", m_windSpeed);
        m_material.SetShaderParameter("sand_fill", m_fill);
    }
}
