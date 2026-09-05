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
/// Selects the debug-only material view used to investigate cockpit-frame rendering.
/// </summary>
public enum CockpitFrameDiagnosticMode
{
    Lit,
    Albedo,
    GeometricNormal,
    NormalMap,
    Roughness,
    Metallic,
    DirectSun
}

/// <summary>
/// Loads the Blender cockpit model and applies its runtime materials, lighting, and pose.
/// </summary>
public partial class PlayerCockpit : Node3D
{
    public const uint RenderLayer = 1u << 2;

    private const string CockpitModelPath = "res://Assets/Models/Cockpit/cockpit.glb";
    private const float DefaultFrameTextureScale = 1.5f;
    private const float DefaultFrameMetallic = 0.75f;
    private const float DefaultFrameRoughness = 0.60f;
    private const float DefaultGlassVisibility = 0.01f;
    private const float DefaultGlassGrimeStrength = 1.0f;
    private const float DefaultGlassScratchStrength = 0.10f;
    private const string FrameAlbedoTexturePath =
        "res://Assets/Textures/Cockpit/Metal029/Metal029_1K-PNG_Color.png";
    private const string FrameMetalnessTexturePath =
        "res://Assets/Textures/Cockpit/Metal029/Metal029_1K-PNG_Metalness.png";
    private const string FrameNormalTexturePath =
        "res://Assets/Textures/Cockpit/Metal029/Metal029_1K-PNG_NormalGL.png";
    private const string FrameRoughnessTexturePath =
        "res://Assets/Textures/Cockpit/Metal029/Metal029_1K-PNG_Roughness.png";
    private const string GlassScratchTexturePath =
        "res://Assets/Textures/Cockpit/Scratches003/Scratches003_1K-PNG_Color.png";
    private const string GlassScratchNormalTexturePath =
        "res://Assets/Textures/Cockpit/Scratches003/Scratches003_1K-PNG_NormalGL.png";
    private const string SideArmorAlbedoTexturePath =
        "res://Assets/Textures/Cockpit/MetalPlates013/MetalPlates013_1K-PNG_Color.png";
    private const string SideArmorMetalnessTexturePath =
        "res://Assets/Textures/Cockpit/MetalPlates013/MetalPlates013_1K-PNG_Metalness.png";
    private const string SideArmorNormalTexturePath =
        "res://Assets/Textures/Cockpit/MetalPlates013/MetalPlates013_1K-PNG_NormalGL.png";
    private const string SideArmorRoughnessTexturePath =
        "res://Assets/Textures/Cockpit/MetalPlates013/MetalPlates013_1K-PNG_Roughness.png";

    private StandardMaterial3D m_frameMaterial;
    private MeshInstance3D m_frameMesh;
    private ShaderMaterial m_glassMaterial;
    private float m_frameTextureScale = DefaultFrameTextureScale;
    private float m_frameMetallic = DefaultFrameMetallic;
    private float m_frameRoughness = DefaultFrameRoughness;
    private float m_glassVisibility = DefaultGlassVisibility;
    private float m_glassGrimeStrength = DefaultGlassGrimeStrength;
    private float m_glassScratchStrength = DefaultGlassScratchStrength;
    private CockpitFrameDiagnosticMode m_frameDiagnosticMode;

    public PlayerCockpit()
    {
        Name = "CockpitInterior";
    }

    public CockpitFrameDiagnosticMode FrameDiagnosticMode
    {
        get => m_frameDiagnosticMode;
        set
        {
            m_frameDiagnosticMode = value;
            ApplyFrameDiagnosticMaterial();
        }
    }

    public string FrameDiagnosticModeName => ToDiagnosticName(FrameDiagnosticMode);

    /// <summary>
    /// Controls the number of Metal029 repetitions across each metre of cockpit structure.
    /// </summary>
    [Export]
    public float FrameTextureScale
    {
        get => m_frameTextureScale;
        set
        {
            m_frameTextureScale = Mathf.Clamp(value, 0.1f, 12.0f);
            ApplyFrameMaterialProperties();
        }
    }

    /// <summary>
    /// Controls how strongly the cockpit frame behaves as painted metal.
    /// </summary>
    [Export]
    public float FrameMetallic
    {
        get => m_frameMetallic;
        set
        {
            m_frameMetallic = Mathf.Clamp(value, 0.0f, 1.0f);
            ApplyFrameMaterialProperties();
        }
    }

    /// <summary>
    /// Controls the cockpit frame's reflected-light blur.
    /// </summary>
    [Export]
    public float FrameRoughness
    {
        get => m_frameRoughness;
        set
        {
            m_frameRoughness = Mathf.Clamp(value, 0.0f, 1.0f);
            ApplyFrameMaterialProperties();
        }
    }

    /// <summary>
    /// Controls the clean glazing's baseline visibility while leaving edge reflections intact.
    /// </summary>
    [Export]
    public float GlassVisibility
    {
        get => m_glassVisibility;
        set
        {
            m_glassVisibility = Mathf.Clamp(value, 0.0f, 0.20f);
            ApplyGlassMaterialProperties();
        }
    }

    /// <summary>
    /// Controls dust and dried residue, with the strongest accumulation at pane edges and below.
    /// </summary>
    [Export]
    public float GlassGrimeStrength
    {
        get => m_glassGrimeStrength;
        set
        {
            m_glassGrimeStrength = Mathf.Clamp(value, 0.0f, 2.0f);
            ApplyGlassMaterialProperties();
        }
    }

    /// <summary>
    /// Controls the visibility of fine surface scratches without changing the clear glass.
    /// </summary>
    [Export]
    public float GlassScratchStrength
    {
        get => m_glassScratchStrength;
        set
        {
            m_glassScratchStrength = Mathf.Clamp(value, 0.0f, 0.30f);
            ApplyGlassMaterialProperties();
        }
    }

    public override void _Ready()
    {
        LoadCockpitModel();
    }

    /// <summary>
    /// Selects a named material view for a cockpit-frame investigation.
    /// </summary>
    public bool TrySetFrameDiagnosticMode(string name)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "lit":
            case "default":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.Lit;
                return true;
            case "albedo":
            case "basecolor":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.Albedo;
                return true;
            case "normal":
            case "normals":
            case "geometricnormal":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.GeometricNormal;
                return true;
            case "normalmap":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.NormalMap;
                return true;
            case "roughness":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.Roughness;
                return true;
            case "metallic":
            case "metalness":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.Metallic;
                return true;
            case "directsun":
            case "sun":
                FrameDiagnosticMode = CockpitFrameDiagnosticMode.DirectSun;
                return true;
            default:
                return false;
        }
    }

    public void SetPose(float pitchDegrees, float yaw, Vector3 gaitPosition, float gaitRoll)
    {
        Position = gaitPosition;
        Rotation = new Vector3(Mathf.DegToRad(pitchDegrees), yaw, gaitRoll);
    }

    /// <summary>
    /// Loads the editable Blender enclosure while retaining the game's material controls and glazing.
    /// </summary>
    private void LoadCockpitModel()
    {
        var scene = GD.Load<PackedScene>(CockpitModelPath)
            ?? throw new InvalidOperationException($"Unable to load cockpit model: {CockpitModelPath}");

        var model = scene.Instantiate<Node3D>();
        var frame = model.FindChild("CockpitFrame", true, false) as MeshInstance3D;
        var glass = model.FindChild("CockpitGlass", true, false) as MeshInstance3D;
        var armor = model.FindChild("CockpitArmor", true, false) as MeshInstance3D;
        if (frame == null || glass == null || armor == null)
        {
            model.Free();
            throw new InvalidOperationException(
                "Cockpit model requires CockpitFrame, CockpitGlass, and CockpitArmor meshes.");
        }

        m_frameMaterial = CreateFrameMaterial();
        m_frameMesh = frame;
        for (var surface = 0; surface < frame.Mesh.GetSurfaceCount(); surface++)
        {
            frame.SetSurfaceOverrideMaterial(surface, m_frameMaterial);
        }

        m_glassMaterial = CreateGlassMaterial();
        glass.MaterialOverride = m_glassMaterial;
        glass.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        armor.MaterialOverride = CreateSideArmorMaterial();
        foreach (var node in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            ((MeshInstance3D)node).Layers = RenderLayer;
        }

        model.Name = "AuthoredCockpit";
        AddChild(model);
        model.AddChild(new OmniLight3D
        {
            Name = "PortRailLamp",
            Position = new Vector3(-0.30f, -0.016f, -0.42f),
            LightColor = new Color(1.0f, 0.40f, 0.12f),
            LightEnergy = 0.035f,
            OmniRange = 0.45f,
            LightCullMask = RenderLayer,
            ShadowEnabled = false
        });
        model.AddChild(new OmniLight3D
        {
            Name = "RearCanopyLamp",
            Position = new Vector3(0.0f, 0.24f, 0.70f),
            LightColor = new Color(1.0f, 0.68f, 0.40f),
            LightEnergy = 0.065f,
            OmniRange = 1.6f,
            LightCullMask = RenderLayer,
            ShadowEnabled = false
        });
        model.AddChild(new OmniLight3D
        {
            Name = "PhaseModuleGlow",
            Position = new Vector3(0.285f, -0.168f, -0.0555f),
            LightColor = new Color(0.38f, 0.73f, 1.0f),
            LightEnergy = 0.016f,
            OmniRange = 0.38f,
            LightCullMask = RenderLayer,
            ShadowEnabled = false
        });
        ApplyFrameDiagnosticMaterial();
    }

    private StandardMaterial3D CreateFrameMaterial()
    {
        var material = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>(FrameAlbedoTexturePath),
            NormalEnabled = true,
            NormalTexture = GD.Load<Texture2D>(FrameNormalTexturePath),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Triplanar = true,
            Uv1TriplanarSharpness = 12.0f,
            Uv1WorldTriplanar = false
        };
        // Metal029's metalness map is uniformly white and its roughness averages about 0.30.
        // Multiplying those by the previous controls produced a near-black, mirror-like frame
        // whose sky reflections looked like light arriving from the wrong direction. Keep the
        // useful colour and normal detail, but model the cockpit as dark painted steel with
        // restrained metal response and a broad, stable reflection.
        m_frameMaterial = material;
        ApplyFrameMaterialProperties();
        return material;
    }

    private static StandardMaterial3D CreateSideArmorMaterial() => new()
    {
        AlbedoColor = new Color(0.58f, 0.55f, 0.52f),
        AlbedoTexture = GD.Load<Texture2D>(SideArmorAlbedoTexturePath),
        Metallic = 0.72f,
        MetallicTexture = GD.Load<Texture2D>(SideArmorMetalnessTexturePath),
        NormalEnabled = true,
        NormalScale = 0.72f,
        NormalTexture = GD.Load<Texture2D>(SideArmorNormalTexturePath),
        Roughness = 0.88f,
        RoughnessTexture = GD.Load<Texture2D>(SideArmorRoughnessTexturePath),
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic
    };

    private void ApplyFrameMaterialProperties()
    {
        if (m_frameMaterial == null)
        {
            return;
        }

        m_frameMaterial.Metallic = m_frameMetallic;
        m_frameMaterial.Roughness = m_frameRoughness;
        m_frameMaterial.Uv1Scale = Vector3.One * m_frameTextureScale;
        if (FrameDiagnosticMode is CockpitFrameDiagnosticMode.Albedo or
            CockpitFrameDiagnosticMode.NormalMap or
            CockpitFrameDiagnosticMode.Roughness or
            CockpitFrameDiagnosticMode.Metallic)
        {
            ApplyFrameDiagnosticMaterial();
        }
    }

    private ShaderMaterial CreateGlassMaterial()
    {
        var material = new ShaderMaterial
        {
            RenderPriority = 1,
            Shader = new Shader
            {
                Code =
                    """
                    shader_type spatial;
                    render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

                    uniform sampler2D scratch_texture : source_color, repeat_enable, filter_linear_mipmap_anisotropic;
                    uniform sampler2D scratch_normal_texture : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
                    uniform float glass_visibility : hint_range(0.0, 0.2) = 0.01;
                    uniform float grime_strength : hint_range(0.0, 2.0) = 1.0;
                    uniform float scratch_strength : hint_range(0.0, 0.3) = 0.10;

                    float random_value(vec2 position) {
                        return fract(sin(dot(position, vec2(127.1, 311.7))) * 43758.5453);
                    }

                    float value_noise(vec2 position) {
                        vec2 cell = floor(position);
                        vec2 blend = smoothstep(vec2(0.0), vec2(1.0), fract(position));
                        float a = random_value(cell);
                        float b = random_value(cell + vec2(1.0, 0.0));
                        float c = random_value(cell + vec2(0.0, 1.0));
                        float d = random_value(cell + vec2(1.0, 1.0));
                        return mix(mix(a, b, blend.x), mix(c, d, blend.x), blend.y);
                    }

                    float residue_noise(vec2 position) {
                        float result = 0.0;
                        float amplitude = 0.55;
                        for (int octave = 0; octave < 3; octave++) {
                            result += value_noise(position) * amplitude;
                            position = position * 2.07 + vec2(7.1, 3.4);
                            amplitude *= 0.48;
                        }
                        return result;
                    }

                    void fragment() {
                        vec2 scratch_uv = UV * vec2(1.25, 1.8) + vec2(COLOR.r * 0.37, COLOR.r * 0.19);
                        vec2 second_scratch_uv = vec2(-scratch_uv.y, scratch_uv.x) + vec2(0.43, 0.71);
                        float first_scratch_sample = texture(scratch_texture, scratch_uv).r;
                        float second_scratch_sample = texture(scratch_texture, second_scratch_uv).r;
                        float first_scratches = smoothstep(0.02, 0.30, first_scratch_sample);
                        float second_scratches = smoothstep(0.02, 0.30, second_scratch_sample);
                        float scratches = max(first_scratches, second_scratches);

                        vec3 first_scratch_normal = texture(scratch_normal_texture, scratch_uv).rgb * 2.0 - 1.0;
                        vec3 second_scratch_normal = texture(scratch_normal_texture, second_scratch_uv).rgb * 2.0 - 1.0;
                        vec2 rotated_second_normal = vec2(second_scratch_normal.y, -second_scratch_normal.x);
                        vec2 scratch_normal_xy = first_scratch_normal.xy * first_scratches +
                            rotated_second_normal * second_scratches;
                        vec3 scratch_normal = normalize(vec3(scratch_normal_xy * 1.65, 1.0));

                        float edge_distance = min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y));
                        float edge_mask = 1.0 - smoothstep(0.015, 0.14, edge_distance);
                        float residue = residue_noise(UV * vec2(5.0, 7.0) + vec2(COLOR.r * 8.3, 1.7));
                        float lower_pane = COLOR.r;
                        float edge_grime = edge_mask * mix(0.42, 1.0, residue);
                        float settled_dust = lower_pane * (0.10 + 0.25 * residue);
                        float grime = clamp((edge_grime * mix(0.65, 1.35, lower_pane) + settled_dust) *
                            grime_strength, 0.0, 1.0);

                        // Clear glass is almost invisible head-on. Fresnel reflection, dirt and scratches
                        // reveal the surface at grazing angles without obscuring the world or HUD.
                        float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
                        float fresnel = pow(1.0 - facing, 4.0);
                        vec3 glass_tint = vec3(0.82, 0.88, 0.86);
                        vec3 dust_tint = vec3(0.34, 0.27, 0.17);
                        vec3 scratch_tint = vec3(0.86, 0.88, 0.84);
                        ALBEDO = mix(
                            mix(glass_tint, dust_tint, clamp(grime * 0.78, 0.0, 0.88)),
                            scratch_tint,
                            scratches * 0.45);
                        NORMAL_MAP = scratch_normal * 0.5 + 0.5;
                        NORMAL_MAP_DEPTH = 0.90;
                        METALLIC = 0.0;
                        SPECULAR = 0.5;
                        float surface_roughness = mix(0.08, 0.56, clamp(grime * 0.85, 0.0, 1.0));
                        ROUGHNESS = mix(surface_roughness, 0.08, scratches * 0.70);
                        EMISSION = ALBEDO * (0.10 + scratches * 0.45);
                        ALPHA = clamp(
                            glass_visibility + fresnel * 0.035 + grime * 0.13 + scratches * scratch_strength,
                            0.0,
                            0.26);
                    }
                    """
            }
        };
        material.SetShaderParameter(
            "scratch_texture",
            GD.Load<Texture2D>(GlassScratchTexturePath));
        material.SetShaderParameter(
            "scratch_normal_texture",
            GD.Load<Texture2D>(GlassScratchNormalTexturePath));
        m_glassMaterial = material;
        ApplyGlassMaterialProperties();
        return material;
    }

    private void ApplyGlassMaterialProperties()
    {
        if (m_glassMaterial == null)
        {
            return;
        }

        m_glassMaterial.SetShaderParameter("glass_visibility", GlassVisibility);
        m_glassMaterial.SetShaderParameter("grime_strength", GlassGrimeStrength);
        m_glassMaterial.SetShaderParameter("scratch_strength", GlassScratchStrength);
    }

    private void ApplyFrameDiagnosticMaterial()
    {
        if (m_frameMesh == null)
        {
            return;
        }

        m_frameMesh.MaterialOverride = FrameDiagnosticMode switch
        {
            CockpitFrameDiagnosticMode.Lit => null,
            CockpitFrameDiagnosticMode.Albedo => CreateTextureDiagnosticMaterial(FrameAlbedoTexturePath),
            CockpitFrameDiagnosticMode.NormalMap => CreateTextureDiagnosticMaterial(FrameNormalTexturePath),
            CockpitFrameDiagnosticMode.Roughness => CreateTextureDiagnosticMaterial(FrameRoughnessTexturePath),
            CockpitFrameDiagnosticMode.Metallic => CreateTextureDiagnosticMaterial(FrameMetalnessTexturePath),
            CockpitFrameDiagnosticMode.GeometricNormal => CreateGeometricNormalDiagnosticMaterial(),
            CockpitFrameDiagnosticMode.DirectSun => CreateDirectSunDiagnosticMaterial(),
            _ => null
        };
    }

    private StandardMaterial3D CreateTextureDiagnosticMaterial(string texturePath) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoTexture = GD.Load<Texture2D>(texturePath),
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        Uv1Triplanar = true,
        Uv1TriplanarSharpness = 12.0f,
        Uv1WorldTriplanar = false,
        Uv1Scale = Vector3.One * FrameTextureScale
    };

    private static ShaderMaterial CreateGeometricNormalDiagnosticMaterial() =>
        CreateUnshadedDiagnosticMaterial(
            """
            varying vec3 world_geometric_normal;

            void vertex() {
                world_geometric_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
            }

            void fragment() {
                ALBEDO = world_geometric_normal * 0.5 + 0.5;
            }
            """);

    private static ShaderMaterial CreateDirectSunDiagnosticMaterial() => new()
    {
        Shader = new Shader
        {
            Code =
                """
                shader_type spatial;
                render_mode ambient_light_disabled, shadows_disabled, specular_disabled;

                void fragment() {
                    ALBEDO = vec3(1.0);
                    METALLIC = 0.0;
                    ROUGHNESS = 1.0;
                }

                void light() {
                    // LIGHT and NORMAL are both supplied by Godot in the same space. This
                    // intentionally tests the actual SunLight direction, not a C# duplicate.
                    float direct = max(dot(normalize(NORMAL), normalize(LIGHT)), 0.0);
                    DIFFUSE_LIGHT = vec3(direct);
                }
                """
        }
    };

    private static ShaderMaterial CreateUnshadedDiagnosticMaterial(string body) => new()
    {
        Shader = new Shader
        {
            Code =
                """
                shader_type spatial;
                render_mode unshaded, cull_back;
                """ + body
        }
    };

    private static string ToDiagnosticName(CockpitFrameDiagnosticMode mode) => mode switch
    {
        CockpitFrameDiagnosticMode.GeometricNormal => "geometric normal",
        CockpitFrameDiagnosticMode.NormalMap => "normal map",
        CockpitFrameDiagnosticMode.DirectSun => "direct sun",
        _ => mode.ToString().ToLowerInvariant()
    };
}
