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
/// Reusable emissive missile projectile with restrained target-seeking steering.
/// </summary>
public partial class MissileEffect : Node3D
{
    private const float SpeedMetersPerSecond = 100.0f;
    private const float TurnRate = 4.5f;
    private const float CloseRangeTurnRate = 8.0f;
    private const float ImpactRadius = 4.0f;
    private const float SmokeLifetimeSeconds = 1.35f;
    private const int SmokeParticleCount = 144;
    // Every pooled missile keeps its own simulation state, while immutable GPU resources are shared.
    private static readonly ParticleProcessMaterial s_smokeProcessMaterial = CreateSmokeProcessMaterial();
    private static readonly QuadMesh s_smokeMesh = CreateSmokeMesh();
    private static readonly ShaderMaterial s_smokeVisualMaterial = CreateSmokeVisualMaterial();
    private readonly bool m_carriesLight;
    private readonly MeshInstance3D m_body;
    private readonly MeshInstance3D m_exhaust;
    private readonly OmniLight3D m_light;
    private readonly GpuParticles3D m_smokeTrail;
    private Vector3 m_direction;
    private float m_range;
    private float m_distanceTravelled;
    private float m_smokeFadeRemaining;
    private bool m_isFlying;
    private Vector3? m_previousTargetPosition;
    private Vector3 m_targetVelocity;
    private Func<Vector3?> m_targetPosition;
    private Action<Vector3> m_impact;
    private Action<Vector3> m_terrainImpact;
    private float m_guidanceArmingDistance;

    public MissileEffect(bool carriesLight)
    {
        Name = "PooledMissile";
        m_carriesLight = carriesLight;
        var bodyMaterial = new StandardMaterial3D
        {
            AlbedoColor = Color.FromHtml("565d63"),
            Metallic = 0.7f,
            Roughness = 0.35f
        };
        m_body = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.055f,
                BottomRadius = 0.07f,
                Height = 0.75f,
                RadialSegments = 8,
                Rings = 1
            },
            MaterialOverride = bodyMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(m_body);

        var exhaustMaterial = new StandardMaterial3D
        {
            AlbedoColor = Color.FromHtml("fff070"),
            EmissionEnabled = true,
            Emission = Color.FromHtml("ffb020"),
            EmissionEnergyMultiplier = 10.0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        m_exhaust = new MeshInstance3D
        {
            Position = Vector3.Down * 0.52f,
            Mesh = new CylinderMesh
            {
                TopRadius = 0.025f,
                BottomRadius = 0.14f,
                Height = 0.4f,
                RadialSegments = 8,
                Rings = 1
            },
            MaterialOverride = exhaustMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(m_exhaust);

        m_light = new OmniLight3D
        {
            Position = Vector3.Down * 0.35f,
            LightColor = Color.FromHtml("ffb020"),
            LightEnergy = 3.5f,
            OmniRange = 6.0f,
            ShadowEnabled = false,
            Visible = carriesLight
        };
        AddChild(m_light);

        m_smokeTrail = new GpuParticles3D
        {
            Name = "RocketSmokeTrail",
            Position = Vector3.Down * 0.78f,
            Emitting = false,
            Amount = SmokeParticleCount,
            Lifetime = SmokeLifetimeSeconds,
            Randomness = 0.18f,
            LocalCoords = false,
            FixedFps = 60,
            Interpolate = true,
            FractDelta = true,
            VisibilityAabb = new Aabb(
                new Vector3(-150.0f, -150.0f, -150.0f),
                new Vector3(300.0f, 300.0f, 300.0f)),
            ProcessMaterial = s_smokeProcessMaterial,
            DrawPass1 = s_smokeMesh,
            MaterialOverride = s_smokeVisualMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(m_smokeTrail);
        ResetImmediately();
    }

    public bool IsActive { get; private set; }

    public float Age { get; private set; }

    public void Launch(
        Vector3 position,
        Vector3 direction,
        float range,
        Func<Vector3?> targetPosition,
        Action<Vector3> impact,
        float guidanceArmingDistance = 0.0f,
        Action<Vector3> terrainImpact = null)
    {
        GlobalPosition = position;
        m_direction = direction.Normalized();
        m_range = range;
        m_distanceTravelled = 0.0f;
        m_previousTargetPosition = null;
        m_targetVelocity = Vector3.Zero;
        m_targetPosition = targetPosition;
        m_impact = impact;
        m_guidanceArmingDistance = Math.Max(guidanceArmingDistance, 0.0f);
        m_terrainImpact = terrainImpact;
        Age = 0.0f;
        IsActive = true;
        m_isFlying = true;
        Visible = true;
        m_body.Visible = true;
        m_exhaust.Visible = true;
        m_light.Visible = m_carriesLight;
        m_smokeTrail.Visible = true;
        m_smokeTrail.Emitting = true;
        SetProcess(true);
        OrientToDirection();
        m_smokeTrail.Restart();
    }

    public override void _Process(double delta)
    {
        if (!m_isFlying)
        {
            Age += (float)delta;
            m_smokeFadeRemaining -= (float)delta;
            if (m_smokeFadeRemaining <= 0.0f)
            {
                IsActive = false;
                m_smokeTrail.Visible = false;
                SetProcess(false);
            }

            return;
        }

        var elapsed = (float)delta;
        Age += elapsed;
        var target = m_distanceTravelled >= m_guidanceArmingDistance
            ? m_targetPosition?.Invoke()
            : null;
        if (target.HasValue)
        {
            if (m_previousTargetPosition.HasValue && elapsed > 0.0001f)
            {
                var measuredVelocity = (target.Value - m_previousTargetPosition.Value) / elapsed;
                m_targetVelocity = m_targetVelocity.Lerp(
                    measuredVelocity,
                    Math.Min(elapsed * 8.0f, 1.0f));
            }

            m_previousTargetPosition = target.Value;
            var targetDistance = GlobalPosition.DistanceTo(target.Value);
            var leadSeconds = Math.Min(targetDistance / SpeedMetersPerSecond, 1.25f);
            var aimPosition = target.Value + m_targetVelocity * leadSeconds * 0.75f;
            var toTarget = GlobalPosition.DirectionTo(aimPosition);
            if (!toTarget.IsZeroApprox())
            {
                var angle = m_direction.AngleTo(toTarget);
                var turnRate = Mathf.Lerp(
                    CloseRangeTurnRate,
                    TurnRate,
                    Math.Min(targetDistance / 120.0f, 1.0f));
                var maximumTurn = turnRate * elapsed;
                m_direction = angle <= maximumTurn
                    ? toTarget
                    : m_direction.Slerp(toTarget, maximumTurn / angle).Normalized();
            }
        }

        var distance = SpeedMetersPerSecond * elapsed;
        var nextPosition = GlobalPosition + m_direction * distance;
        if (TryFindTerrainImpact(GlobalPosition, nextPosition, out var terrainImpact))
        {
            m_terrainImpact?.Invoke(terrainImpact);
            Deactivate();
            return;
        }

        if (target.HasValue && DistanceToSegment(target.Value, GlobalPosition, nextPosition) <= ImpactRadius)
        {
            var impact = target.Value;
            m_impact?.Invoke(impact);
            Deactivate();
            return;
        }

        GlobalPosition = nextPosition;
        m_distanceTravelled += distance;
        OrientToDirection();
        if (m_distanceTravelled >= m_range)
        {
            Deactivate();
        }
    }

    private void OrientToDirection()
    {
        if (!m_direction.IsZeroApprox())
        {
            GlobalBasis = new Basis(new Quaternion(Vector3.Up, m_direction));
        }
    }

    private void Deactivate()
    {
        m_isFlying = false;
        m_smokeFadeRemaining = SmokeLifetimeSeconds;
        m_body.Visible = false;
        m_exhaust.Visible = false;
        m_light.Visible = false;
        m_smokeTrail.Emitting = false;
        m_targetPosition = null;
        m_impact = null;
        m_terrainImpact = null;
        m_guidanceArmingDistance = 0.0f;
    }

    private void ResetImmediately()
    {
        IsActive = false;
        m_isFlying = false;
        m_body.Visible = false;
        m_exhaust.Visible = false;
        m_light.Visible = false;
        m_smokeTrail.Visible = false;
        SetProcess(false);
    }

    private static ParticleProcessMaterial CreateSmokeProcessMaterial() =>
        new()
        {
            Direction = Vector3.Down,
            Spread = 8.0f,
            InitialVelocityMin = 2.0f,
            InitialVelocityMax = 5.0f,
            Gravity = new Vector3(0.08f, 0.42f, 0.04f),
            DampingMin = 0.65f,
            DampingMax = 1.25f,
            ScaleMin = 0.82f,
            ScaleMax = 1.28f,
            ColorRamp = new GradientTexture1D
            {
                Gradient = new Gradient
                {
                    Colors =
                    [
                        new Color(0.90f, 0.89f, 0.84f, 0.50f),
                        new Color(0.73f, 0.75f, 0.74f, 0.38f),
                        new Color(0.57f, 0.60f, 0.60f, 0.18f),
                        new Color(0.49f, 0.52f, 0.53f, 0.0f)
                    ],
                    Offsets = [0.0f, 0.16f, 0.62f, 1.0f]
                }
            },
            // Fill the distance crossed between render frames so the exhaust reads as a continuous plume.
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.09f, 0.72f, 0.09f)
        };

    private static QuadMesh CreateSmokeMesh() =>
        new()
        {
            Size = Vector2.One
        };

    private static ShaderMaterial CreateSmokeVisualMaterial()
    {
        var material = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode blend_mix, depth_draw_never, cull_disabled, unshaded;

                    uniform sampler2D smoke_texture : source_color;

                    void vertex() {
                        mat4 mat_world = mat4(
                            normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
                            normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[0]),
                            normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]),
                            MODEL_MATRIX[3]);
                        MODELVIEW_MATRIX = VIEW_MATRIX * mat_world;

                        float frame = clamp(floor(INSTANCE_CUSTOM.y * 64.0), 0.0, 63.0);
                        UV /= vec2(8.0);
                        UV += vec2(mod(frame, 8.0), floor(frame / 8.0)) / vec2(8.0);
                    }

                    void fragment() {
                        vec4 sprite = texture(smoke_texture, UV);
                        float density = max(sprite.r, max(sprite.g, sprite.b));
                        if (sprite.a < 0.008) {
                            discard;
                        }

                        ALBEDO = COLOR.rgb * mix(0.78, 1.0, density);
                        ALPHA = sprite.a * COLOR.a;
                    }
                    """
            }
        };
        var texture = ResourceLoader.Load<Texture2D>("res://Assets/Vfx/smokesprite.png");
        if (texture == null)
        {
            GD.PushWarning("MechRewired: the smoke atlas was not imported; rocket trails will be transparent.");
        }
        else
        {
            material.SetShaderParameter("smoke_texture", texture);
        }

        return material;
    }

    private bool TryFindTerrainImpact(Vector3 start, Vector3 end, out Vector3 impactPosition)
    {
        impactPosition = Vector3.Zero;
        if (start.IsEqualApprox(end))
        {
            return false;
        }

        var query = PhysicsRayQueryParameters3D.Create(start, end, BattlefieldPhysics.TerrainLayer);
        query.HitBackFaces = true;
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            return false;
        }

        impactPosition = result["position"].AsVector3();
        return true;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.000001f)
        {
            return point.DistanceTo(start);
        }

        var position = Math.Clamp((point - start).Dot(segment) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + segment * position);
    }
}
