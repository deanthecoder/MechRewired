// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>
/// Presents the HPG's original PULSE.WTB as a coherent energy packet with a short ionisation trail.
/// </summary>
/// <remarks>
/// The BWD path remains authoritative.  This replaces only the edge-on, zero-thickness rendering
/// of the original mesh and becomes visible while that authored path is travelling upward.
/// </remarks>
public partial class AuthoredPulseEffect : Node3D
{
    private const float MinimumLaunchSpeed = 1.0f;
    private const float MaximumContinuousStep = 100.0f;
    private static ImageTexture s_pulseTexture;

    private readonly MeshInstance3D m_core;
    private readonly MeshInstance3D m_halo;
    private readonly GpuParticles3D m_trail;
    private readonly OmniLight3D m_light;
    private Vector3 m_previousPosition;
    private bool m_hasPreviousPosition;

    public AuthoredPulseEffect(Mesh originalPulseMesh)
    {
        ArgumentNullException.ThrowIfNull(originalPulseMesh);
        Name = "AuthoredHpgPulse";
        var sourceSize = originalPulseMesh.GetAabb().Size;
        var sourceDiameter = Math.Max(Math.Max(sourceSize.X, sourceSize.Y), 1.0f);
        var pulseTexture = GetPulseTexture();

        var coreMaterial = CreatePulseMaterial(pulseTexture, 1.0f, 7.5f);
        m_core = new MeshInstance3D
        {
            Name = "OriginalPulseCore",
            Mesh = new QuadMesh { Size = Vector2.One * (sourceDiameter * 4.0f) },
            MaterialOverride = coreMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        AddChild(m_core);

        m_halo = new MeshInstance3D
        {
            Name = "HpgPulseHalo",
            Mesh = new QuadMesh { Size = Vector2.One * (sourceDiameter * 7.0f) },
            MaterialOverride = CreatePulseMaterial(pulseTexture, 0.22f, 2.8f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        AddChild(m_halo);

        m_trail = new GpuParticles3D
        {
            Name = "HpgIonisationTrail",
            Emitting = false,
            Amount = 56,
            Lifetime = 0.85f,
            Randomness = 0.24f,
            LocalCoords = false,
            FixedFps = 60,
            Interpolate = true,
            FractDelta = true,
            VisibilityAabb = new Aabb(
                new Vector3(-120.0f, -120.0f, -120.0f),
                new Vector3(240.0f, 240.0f, 240.0f)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Zero,
                Spread = 180.0f,
                InitialVelocityMin = 0.0f,
                InitialVelocityMax = 0.35f,
                Gravity = Vector3.Zero,
                DampingMin = 0.4f,
                DampingMax = 0.9f,
                ScaleMin = 0.35f,
                ScaleMax = 1.1f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.35f,
                ColorRamp = new GradientTexture1D
                {
                    Gradient = new Gradient
                    {
                        Colors =
                        [
                            new Color(1.0f, 1.0f, 1.0f, 0.72f),
                            new Color(1.0f, 1.0f, 1.0f, 0.38f),
                            new Color(1.0f, 1.0f, 1.0f, 0.0f)
                        ],
                        Offsets = [0.0f, 0.42f, 1.0f]
                    }
                }
            },
            DrawPass1 = new QuadMesh { Size = Vector2.One * sourceDiameter },
            MaterialOverride = CreatePulseMaterial(pulseTexture, 0.48f, 3.5f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(m_trail);

        m_light = new OmniLight3D
        {
            Name = "HpgPulseLight",
            LightColor = Color.FromHtml("ff9f42"),
            LightEnergy = 8.0f,
            OmniRange = 32.0f,
            ShadowEnabled = false,
            Visible = false
        };
        AddChild(m_light);
    }

    public override void _Ready()
    {
        m_previousPosition = GlobalPosition;
        m_hasPreviousPosition = true;
        m_core.AddToGroup(DebugCamera.SolidMeshGroup);
        m_halo.AddToGroup(DebugCamera.SolidMeshGroup);
    }

    public override void _PhysicsProcess(double delta)
    {
        var position = GlobalPosition;
        if (!m_hasPreviousPosition)
        {
            m_previousPosition = position;
            m_hasPreviousPosition = true;
            return;
        }

        var movement = position - m_previousPosition;
        var elapsed = Math.Max((float)delta, 0.0001f);
        var isLaunching = movement.Y > MinimumLaunchSpeed * elapsed &&
                          movement.Length() < MaximumContinuousStep;
        m_core.Visible = isLaunching;
        m_halo.Visible = isLaunching;
        m_light.Visible = isLaunching;
        m_trail.Emitting = isLaunching;
        m_previousPosition = position;
    }

    private static StandardMaterial3D CreatePulseMaterial(
        Texture2D texture,
        float alpha,
        float emissionEnergy) => new()
    {
        AlbedoColor = new Color(1.0f, 1.0f, 1.0f, alpha),
        AlbedoTexture = texture,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = true,
        Emission = Colors.White,
        EmissionTexture = texture,
        EmissionEnergyMultiplier = emissionEnergy,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
    };

    private static ImageTexture GetPulseTexture()
    {
        if (s_pulseTexture != null)
        {
            return s_pulseTexture;
        }

        const int size = 64;
        using var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var centre = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        var radius = size * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var normalizedRadius = new Vector2(x, y).DistanceTo(centre) / radius;
                var alpha = Mathf.Clamp(1.0f - normalizedRadius, 0.0f, 1.0f);
                alpha *= alpha;
                var hotCore = Mathf.Pow(alpha, 0.35f);
                var color = new Color(
                    1.0f,
                    Mathf.Lerp(0.24f, 0.96f, hotCore),
                    Mathf.Lerp(0.04f, 0.72f, hotCore),
                    alpha);
                image.SetPixel(x, y, color);
            }
        }

        s_pulseTexture = ImageTexture.CreateFromImage(image);
        return s_pulseTexture;
    }
}
