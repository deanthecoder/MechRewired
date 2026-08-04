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
/// Owns battlefield fire, explosion, and smoke effects.
/// </summary>
/// <remarks>
/// All visuals are generated at runtime so the effects remain portable and do not require replacement assets.
/// </remarks>
public partial class BattlefieldEffects : Node3D
{
    public const float EffectPersistenceRadius = 800.0f;

    private static bool s_vfxTexturesLogged;

    private readonly IReadOnlyList<AudioStreamWav> m_explosionSounds;
    private readonly List<TunableEmitter> m_tunableEmitters = [];
    private readonly List<AmbientEffectState> m_ambientEffects = [];
    private readonly List<Node3D> m_distanceBoundEffects = [];
    private IReadOnlyList<DebugTriangle> m_terrainTriangles = Array.Empty<DebugTriangle>();
    private Node3D m_observer;
    private DebugVfxParameter m_selectedDebugParameter;
    private float m_fireDensity = 2.5f;
    private float m_fireSize = 4.75f;
    private float m_fireRise = 5.0f;
    private float m_fireBrightness = 5.0f;
    private float m_smokeDensity = 0.15f;
    private float m_smokeSize = 5.0f;
    private float m_smokeRise = 5.0f;
    private float m_smokeLifetime = 1.5f;

    public BattlefieldEffects(IReadOnlyList<AudioStreamWav> explosionSounds)
    {
        ArgumentNullException.ThrowIfNull(explosionSounds);
        m_explosionSounds = explosionSounds;
    }

    public void ConfigureTerrain(IReadOnlyList<DebugTriangle> sceneTriangles)
    {
        ArgumentNullException.ThrowIfNull(sceneTriangles);
        m_terrainTriangles = sceneTriangles.Where(triangle =>
                triangle.ResourcePath == "IMPLICIT/GROUND" ||
                triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// Sets the player-position source used for one-way visual-effect cleanup.
    /// </summary>
    public void ConfigureObserver(Node3D observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        m_observer = observer;
        UpdateDistanceBoundEffects();
    }

    public void AddAmbientFire(
        Aabb authoredVolume,
        float authoredGroundHeight,
        string sourceName,
        AudioStreamWav ambientSound)
    {
        m_ambientEffects.Add(new AmbientEffectState(
            true,
            AlignToTerrain(authoredVolume, authoredGroundHeight),
            sourceName,
            ambientSound));
        UpdateDistanceBoundEffects();
    }

    public void AddAmbientSmoke(
        Aabb authoredVolume,
        float authoredGroundHeight,
        string sourceName,
        AudioStreamWav ambientSound)
    {
        m_ambientEffects.Add(new AmbientEffectState(
            false,
            AlignToTerrain(authoredVolume, authoredGroundHeight),
            sourceName,
            ambientSound));
        UpdateDistanceBoundEffects();
    }

    private Aabb AlignToTerrain(Aabb volume, float authoredGroundHeight)
    {
        var center = volume.GetCenter();
        var terrainHeight = FindTerrainHeight(center, authoredGroundHeight);
        volume.Position += Vector3.Up * (terrainHeight - authoredGroundHeight);
        return volume;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        UpdateDistanceBoundEffects();
    }

    private void UpdateDistanceBoundEffects()
    {
        if (!IsInstanceValid(m_observer))
        {
            return;
        }

        foreach (var ambientEffect in m_ambientEffects)
        {
            if (ambientEffect.IsCulled)
            {
                continue;
            }

            var isWithinRange = IsWithinEffectPersistenceRange(ambientEffect.Volume.GetCenter());
            if (ambientEffect.Instance == null && isWithinRange)
            {
                ambientEffect.Instance = CreateAmbientEffect(ambientEffect);
                AddChild(ambientEffect.Instance);
                GD.Print($"MechRewired: activated ambient {ambientEffect.KindName} '{ambientEffect.SourceName}' within {EffectPersistenceRadius:F0}m.");
            }
            else if (ambientEffect.Instance != null && !isWithinRange)
            {
                ambientEffect.Instance.QueueFree();
                ambientEffect.Instance = null;
                ambientEffect.IsCulled = true;
                GD.Print($"MechRewired: culled ambient {ambientEffect.KindName} '{ambientEffect.SourceName}' beyond {EffectPersistenceRadius:F0}m.");
            }
        }

        for (var index = m_distanceBoundEffects.Count - 1; index >= 0; index--)
        {
            var effect = m_distanceBoundEffects[index];
            if (!IsInstanceValid(effect))
            {
                m_distanceBoundEffects.RemoveAt(index);
                continue;
            }

            if (!IsWithinEffectPersistenceRange(effect.GlobalPosition))
            {
                effect.QueueFree();
                m_distanceBoundEffects.RemoveAt(index);
                GD.Print($"MechRewired: culled transient battlefield effect beyond {EffectPersistenceRadius:F0}m.");
            }
        }
    }

    private EffectInstance CreateAmbientEffect(AmbientEffectState definition)
    {
        var volume = definition.Volume;
        var position = new Vector3(volume.GetCenter().X, volume.Position.Y, volume.GetCenter().Z);
        var effect = new EffectInstance(true)
        {
            Name = $"Ambient{definition.KindName}-{definition.SourceName}",
            Position = position
        };
        if (definition.IsFire)
        {
            effect.AddChild(CreateAmbientFireParticles(volume.Size.X, volume.Size.Y));
            effect.AddChild(CreateAmbientSmokeParticles(volume.Size.X * 0.7f, volume.Size.Y * 1.3f));
            effect.AddChild(CreateFireLight(volume.Size.X * 0.65f, 14.0f + volume.Size.X * 0.12f));
        }
        else
        {
            effect.AddChild(CreateAmbientSmokeParticles(volume.Size.X, volume.Size.Y));
        }

        if (definition.AmbientSound != null)
        {
            effect.AddChild(CreatePositionalAudio("AmbientFireSound", definition.AmbientSound, 30.0f, 550.0f, -3.0f));
        }

        return effect;
    }

    private bool IsWithinEffectPersistenceRange(Vector3 position) =>
        IsInstanceValid(m_observer) &&
        m_observer.GlobalPosition.DistanceSquaredTo(position) <= EffectPersistenceRadius * EffectPersistenceRadius;

    public void SpawnDestruction(BattlefieldActor actor, Vector3 hitPosition)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!IsWithinEffectPersistenceRange(hitPosition))
        {
            GD.Print($"MechRewired: skipped distant destruction effect for {actor.Description} beyond {EffectPersistenceRadius:F0}m.");
            return;
        }

        var bounds = actor.DestructionBounds;
        var plumePosition = GetDestructionSmokeOrigin(actor, bounds);

        var effect = new EffectInstance(false)
        {
            Name = $"Destruction-{actor.Name}",
            Position = plumePosition
        };
        AddChild(effect);
        m_distanceBoundEffects.Add(effect);

        var localHit = hitPosition - plumePosition;
        var explosion = CreateFire(true, 40, 1.05f, Math.Clamp(bounds.Size.Length() * 0.12f, 2.5f, 7.0f));
        explosion.Position = localHit;
        effect.AddChild(explosion);
        var light = CreateFireLight(Math.Clamp(bounds.Size.Length() * 0.45f, 5.0f, 13.0f), 22.0f);
        light.Position = localHit;
        effect.AddChild(light);
        effect.ExplosionLight = light;
        if (m_explosionSounds.Count > 0)
        {
            var sound = m_explosionSounds[Math.Abs(actor.Definition.ObjectId) % m_explosionSounds.Count];
            var audio = CreatePositionalAudio("ExplosionSound", sound, 24.0f, 700.0f, 1.0f);
            audio.Position = localHit;
            effect.AddChild(audio);
        }

        var explosionSmoke = CreateSmoke(
            true,
            34,
            4.5f,
            Math.Clamp(bounds.Size.Length() * 0.1f, 1.8f, 4.0f));
        explosionSmoke.Position = localHit;
        effect.AddChild(explosionSmoke);
        var sparks = CreateSparks(Math.Clamp(bounds.Size.Length() * 0.03f, 0.12f, 0.3f));
        sparks.Position = localHit;
        effect.AddChild(sparks);

        var smoke = CreateSmoke(false, 76, 7.0f, Math.Clamp(bounds.Size.Length() * 0.08f, 1.5f, 3.5f));
        effect.AddChild(smoke);
        effect.LingeringSmoke = smoke;
    }

    /// <summary>
    /// Spawns a small, short-lived fire burst at a successful weapon impact.
    /// </summary>
    public void SpawnWeaponImpact(Vector3 hitPosition)
    {
        if (!IsWithinEffectPersistenceRange(hitPosition))
        {
            return;
        }

        var effect = new ImpactEffect
        {
            Name = "WeaponImpact",
            Position = hitPosition
        };
        AddChild(effect);
        m_distanceBoundEffects.Add(effect);

        var size = Math.Clamp(0.24f * m_fireSize, 0.55f, 1.8f);
        var amount = Math.Clamp((int)MathF.Round(7.0f * m_fireDensity), 8, 28);
        var burst = CreateFire(true, amount, 0.52f, size);
        var process = (ParticleProcessMaterial)burst.ProcessMaterial;
        process.InitialVelocityMin *= Math.Clamp(m_fireRise * 0.3f, 0.7f, 1.7f);
        process.InitialVelocityMax *= Math.Clamp(m_fireRise * 0.3f, 0.7f, 1.7f);
        if (burst.MaterialOverride is ShaderMaterial material)
        {
            material.SetShaderParameter("emission_strength", 0.55f * m_fireBrightness);
        }

        effect.AddChild(burst);
        var light = CreateFireLight(4.5f * size, 4.0f * m_fireBrightness);
        effect.AddChild(light);
    }

    private Vector3 GetDestructionSmokeOrigin(BattlefieldActor actor, Aabb originalBounds)
    {
        var position = originalBounds.GetCenter();
        var wreckBounds = actor.WorldBounds;
        if (actor.Definition.DestroyedComponents.Count > 0 && wreckBounds.Size.LengthSquared() > 0.01f)
        {
            position = wreckBounds.GetCenter();
            position.Y = wreckBounds.End.Y + 0.05f;
            return position;
        }

        position.Y = FindTerrainHeight(position, originalBounds.Position.Y);
        return position;
    }

    private float FindTerrainHeight(Vector3 position, float fallback)
    {
        const float rayHeight = 10000.0f;
        var origin = new Vector3(position.X, rayHeight, position.Z);
        return DebugTriangleRaycaster.TryFindNearest(
            m_terrainTriangles,
            origin,
            Vector3.Down,
            out _,
            out var distance)
            ? origin.Y - distance + 0.05f
            : fallback;
    }

    private GpuParticles3D CreateFire(bool oneShot, int amount, float lifetime, float size)
    {
        var material = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = oneShot ? 65.0f : 25.0f,
            InitialVelocityMin = oneShot ? 5.0f : 1.2f,
            InitialVelocityMax = oneShot ? 13.0f : 2.8f,
            Gravity = new Vector3(0.0f, oneShot ? -3.0f : 0.8f, 0.0f),
            DampingMin = 0.2f,
            DampingMax = 1.0f,
            ScaleMin = size * 0.45f,
            ScaleMax = size,
            ColorRamp = CreateColorRamp(
                (0.0f, new Color(1.0f, 0.98f, 0.72f, 1.0f)),
                (0.22f, new Color(1.0f, 0.62f, 0.08f, 0.95f)),
                (0.68f, new Color(0.9f, 0.08f, 0.005f, 0.7f)),
                (1.0f, new Color(0.12f, 0.01f, 0.0f, 0.0f))),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = oneShot ? size * 0.45f : 0.35f
        };
        return CreateParticles("Fire", oneShot, amount, lifetime, oneShot ? 0.95f : 0.25f, material, true);
    }

    private GpuParticles3D CreateAmbientFireParticles(float width, float height)
    {
        width = Math.Max(width, 1.0f);
        height = Math.Max(height, 2.0f);
        var lifetime = Math.Clamp(height / 8.0f, 4.0f, 12.0f);
        var riseSpeed = height / lifetime;
        var material = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 34.0f,
            InitialVelocityMin = riseSpeed * 0.7f,
            InitialVelocityMax = riseSpeed * 1.35f,
            Gravity = new Vector3(0.0f, riseSpeed * 0.08f, 0.0f),
            DampingMin = 0.1f,
            DampingMax = 0.45f,
            ScaleMin = width * 0.035f,
            ScaleMax = width * 0.11f,
            ColorRamp = CreateColorRamp(
                (0.0f, new Color(1.0f, 0.98f, 0.7f, 1.0f)),
                (0.25f, new Color(1.0f, 0.52f, 0.03f, 0.98f)),
                (0.72f, new Color(0.85f, 0.04f, 0.002f, 0.78f)),
                (1.0f, new Color(0.08f, 0.0f, 0.0f, 0.0f))),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 3.0f,
            TurbulenceNoiseScale = 2.0f,
            TurbulenceNoiseSpeed = new Vector3(0.3f, 0.5f, 0.24f),
            TurbulenceInfluenceMin = 0.18f,
            TurbulenceInfluenceMax = 0.5f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(width * 0.48f, 0.6f, width * 0.2f)
        };
        // The reference scene is only 25 particles in a short burst.  A
        // sustained source needs more, but hundreds of overlapping quads turn
        // its carefully painted frames into a flat, pale tile stack.
        var amount = Math.Clamp((int)(width * 1.8f), 36, 100);
        var particles = CreateParticles("Fire", false, amount, lifetime, 0.08f, material, true);
        particles.Preprocess = lifetime;
        particles.VisibilityAabb = new Aabb(
            new Vector3(-width, -5.0f, -width),
            new Vector3(width * 2.0f, height * 1.4f + 10.0f, width * 2.0f));
        RegisterAmbientEmitter(particles, ParticleGeometry.Fire);
        return particles;
    }

    private GpuParticles3D CreateAmbientSmokeParticles(float width, float height)
    {
        width = Math.Max(width, 1.0f);
        height = Math.Max(height, 2.0f);
        var lifetime = Math.Clamp(height / 4.5f, 8.0f, 20.0f);
        var riseSpeed = height / lifetime;
        var material = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 24.0f,
            InitialVelocityMin = riseSpeed * 0.7f,
            InitialVelocityMax = riseSpeed * 1.25f,
            Gravity = new Vector3(0.18f, riseSpeed * 0.06f, 0.1f),
            DampingMin = 0.05f,
            DampingMax = 0.35f,
            ScaleMin = width * 0.06f,
            ScaleMax = width * 0.18f,
            ColorRamp = CreateColorRamp(
                (0.0f, new Color(0.12f, 0.11f, 0.1f, 0.0f)),
                (0.1f, new Color(0.14f, 0.13f, 0.12f, 0.82f)),
                (0.6f, new Color(0.35f, 0.34f, 0.32f, 0.62f)),
                (1.0f, new Color(0.62f, 0.61f, 0.58f, 0.0f))),
            ColorInitialRamp = CreateColorRamp(
                (0.0f, new Color(0.65f, 0.62f, 0.58f, 1.0f)),
                (1.0f, Colors.White)),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 4.0f,
            TurbulenceNoiseScale = 2.4f,
            TurbulenceNoiseSpeed = new Vector3(0.18f, 0.28f, 0.14f),
            TurbulenceInfluenceMin = 0.22f,
            TurbulenceInfluenceMax = 0.62f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(width * 0.32f, 0.7f, width * 0.18f)
        };
        var amount = Math.Clamp((int)(width * 2.2f), 50, 140);
        var particles = CreateParticles("Smoke", false, amount, lifetime, 0.04f, material, false);
        particles.Preprocess = lifetime;
        particles.VisibilityAabb = new Aabb(
            new Vector3(-width, -5.0f, -width),
            new Vector3(width * 2.0f, height * 1.5f + 10.0f, width * 2.0f));
        RegisterAmbientEmitter(particles, ParticleGeometry.Smoke);
        return particles;
    }

    private GpuParticles3D CreateSmoke(bool oneShot, int amount, float lifetime, float size)
    {
        var material = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = oneShot ? 58.0f : 30.0f,
            InitialVelocityMin = oneShot ? 2.5f : 1.0f,
            InitialVelocityMax = oneShot ? 7.5f : 2.6f,
            Gravity = new Vector3(0.12f, oneShot ? 0.2f : 0.62f, 0.07f),
            DampingMin = 0.15f,
            DampingMax = 0.55f,
            ScaleMin = size * 0.35f,
            ScaleMax = size * 1.65f,
            ColorRamp = CreateColorRamp(
                (0.0f, new Color(0.08f, 0.07f, 0.06f, 0.0f)),
                (0.12f, new Color(0.1f, 0.09f, 0.08f, 0.88f)),
                (0.62f, new Color(0.25f, 0.24f, 0.22f, 0.58f)),
                (1.0f, new Color(0.42f, 0.41f, 0.39f, 0.0f))),
            ColorInitialRamp = CreateColorRamp(
                (0.0f, new Color(0.65f, 0.58f, 0.5f, 1.0f)),
                (1.0f, new Color(1.0f, 1.0f, 1.0f, 1.0f))),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 2.2f,
            TurbulenceNoiseScale = 3.5f,
            TurbulenceNoiseSpeed = new Vector3(0.22f, 0.35f, 0.16f),
            TurbulenceInfluenceMin = 0.1f,
            TurbulenceInfluenceMax = 0.32f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = Math.Max(0.4f, size * (oneShot ? 0.55f : 0.3f))
        };
        return CreateParticles("Smoke", oneShot, amount, lifetime, oneShot ? 0.9f : 0.16f, material, false);
    }

    private GpuParticles3D CreateSparks(float size)
    {
        var material = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = 88.0f,
            InitialVelocityMin = 8.0f,
            InitialVelocityMax = 22.0f,
            Gravity = new Vector3(0.0f, -9.0f, 0.0f),
            DampingMin = 0.0f,
            DampingMax = 0.4f,
            ScaleMin = size * 0.35f,
            ScaleMax = size,
            ColorRamp = CreateColorRamp(
                (0.0f, new Color(1.0f, 1.0f, 0.7f, 1.0f)),
                (0.4f, new Color(1.0f, 0.35f, 0.02f, 1.0f)),
                (1.0f, new Color(0.5f, 0.02f, 0.0f, 0.0f))),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.7f
        };
        return CreateParticles(
            "Sparks",
            true,
            46,
            1.6f,
            0.96f,
            material,
            true,
            ParticleGeometry.Spark);
    }

    /// <summary>
    /// Handles the debug-only live battlefield-effects tuner.
    /// </summary>
    /// <remarks>
    /// F5 selects a parameter, F6/F7 reduce/increase it, F8 restores the
    /// authored defaults, and F9 writes the current preset to the log. Hold
    /// Shift while changing a value for a larger adjustment.
    /// </remarks>
    public bool TryHandleDebugInput(InputEventKey keyEvent)
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(keyEvent);
        switch (keyEvent.Keycode)
        {
            case Key.F5:
                m_selectedDebugParameter = (DebugVfxParameter)(((int)m_selectedDebugParameter + 1) %
                    Enum.GetValues<DebugVfxParameter>().Length);
                LogDebugTuning();
                return true;
            case Key.F6:
                AdjustDebugParameter(keyEvent.ShiftPressed ? -0.5f : -0.1f);
                return true;
            case Key.F7:
                AdjustDebugParameter(keyEvent.ShiftPressed ? 0.5f : 0.1f);
                return true;
            case Key.F8:
                ResetDebugTuning();
                return true;
            case Key.F9:
                LogDebugTuning();
                return true;
            default:
                return false;
        }
#else
        return false;
#endif
    }

    private void RegisterAmbientEmitter(GpuParticles3D particles, ParticleGeometry geometry)
    {
        var process = particles.ProcessMaterial as ParticleProcessMaterial ??
                      throw new InvalidOperationException("An ambient particle emitter requires ParticleProcessMaterial.");
        m_tunableEmitters.Add(new TunableEmitter(
            particles,
            geometry,
            particles.Amount,
            particles.Lifetime,
            process.InitialVelocityMin,
            process.InitialVelocityMax,
            process.Gravity,
            process.ScaleMin,
            process.ScaleMax,
            process.Spread));
        ApplyDebugTuning();
    }

    private void AdjustDebugParameter(float adjustment)
    {
        ref var selected = ref GetSelectedDebugParameter();
        selected = Math.Clamp(selected + adjustment, 0.1f, 5.0f);
        ApplyDebugTuning();
        LogDebugTuning();
    }

    private ref float GetSelectedDebugParameter()
    {
        switch (m_selectedDebugParameter)
        {
            case DebugVfxParameter.FireDensity:
                return ref m_fireDensity;
            case DebugVfxParameter.FireSize:
                return ref m_fireSize;
            case DebugVfxParameter.FireRise:
                return ref m_fireRise;
            case DebugVfxParameter.FireBrightness:
                return ref m_fireBrightness;
            case DebugVfxParameter.SmokeDensity:
                return ref m_smokeDensity;
            case DebugVfxParameter.SmokeSize:
                return ref m_smokeSize;
            case DebugVfxParameter.SmokeRise:
                return ref m_smokeRise;
            case DebugVfxParameter.SmokeLifetime:
                return ref m_smokeLifetime;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ResetDebugTuning()
    {
        m_fireDensity = 2.5f;
        m_fireSize = 4.75f;
        m_fireRise = 5.0f;
        m_fireBrightness = 5.0f;
        m_smokeDensity = 0.15f;
        m_smokeSize = 5.0f;
        m_smokeRise = 5.0f;
        m_smokeLifetime = 1.5f;
        ApplyDebugTuning();
        LogDebugTuning();
    }

    private void ApplyDebugTuning()
    {
        for (var index = m_tunableEmitters.Count - 1; index >= 0; index--)
        {
            var emitter = m_tunableEmitters[index];
            if (!GodotObject.IsInstanceValid(emitter.Particles))
            {
                m_tunableEmitters.RemoveAt(index);
                continue;
            }

            var process = (ParticleProcessMaterial)emitter.Particles.ProcessMaterial;
            var isFire = emitter.Geometry == ParticleGeometry.Fire;
            var density = isFire ? m_fireDensity : m_smokeDensity;
            var size = isFire ? m_fireSize : m_smokeSize;
            var rise = isFire ? m_fireRise : m_smokeRise;
            var lifetime = isFire ? 1.0f : m_smokeLifetime;

            emitter.Particles.Amount = Math.Max(1, (int)MathF.Round(emitter.BaseAmount * density));
            emitter.Particles.Lifetime = emitter.BaseLifetime * lifetime;
            process.InitialVelocityMin = emitter.BaseVelocityMin * rise;
            process.InitialVelocityMax = emitter.BaseVelocityMax * rise;
            process.Gravity = new Vector3(
                emitter.BaseGravity.X,
                emitter.BaseGravity.Y * rise,
                emitter.BaseGravity.Z);
            process.ScaleMin = emitter.BaseScaleMin * size;
            process.ScaleMax = emitter.BaseScaleMax * size;
            process.Spread = emitter.BaseSpread;

            if (emitter.Particles.MaterialOverride is ShaderMaterial material)
            {
                material.SetShaderParameter("emission_strength", isFire ? 0.55f * m_fireBrightness : 0.04f);
            }

            emitter.Particles.Restart();
        }
    }

    private void LogDebugTuning() => GD.Print(
        $"MechRewired: VFX tuner [{GetDebugParameterName(m_selectedDebugParameter)}]; " +
        $"fire density {m_fireDensity:F2}, size {m_fireSize:F2}, rise {m_fireRise:F2}, brightness {m_fireBrightness:F2}; " +
        $"smoke density {m_smokeDensity:F2}, size {m_smokeSize:F2}, rise {m_smokeRise:F2}, lifetime {m_smokeLifetime:F2}. " +
        "F5 select; F6/F7 adjust (Shift x5); F8 reset; F9 log.");

    private static string GetDebugParameterName(DebugVfxParameter parameter) => parameter switch
    {
        DebugVfxParameter.FireDensity => "fire density",
        DebugVfxParameter.FireSize => "fire size",
        DebugVfxParameter.FireRise => "fire rise",
        DebugVfxParameter.FireBrightness => "fire brightness",
        DebugVfxParameter.SmokeDensity => "smoke density",
        DebugVfxParameter.SmokeSize => "smoke size",
        DebugVfxParameter.SmokeRise => "smoke rise",
        DebugVfxParameter.SmokeLifetime => "smoke lifetime",
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };

    private GpuParticles3D CreateParticles(
        string name,
        bool oneShot,
        int amount,
        float lifetime,
        float explosiveness,
        ParticleProcessMaterial processMaterial,
        bool additive,
        ParticleGeometry? geometry = null)
    {
        var particleGeometry = geometry ?? (additive ? ParticleGeometry.Fire : ParticleGeometry.Smoke);
        Godot.Material visualMaterial = particleGeometry == ParticleGeometry.Spark
            ? new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true
            }
            : CreateParticleShaderMaterial(particleGeometry == ParticleGeometry.Smoke);
        PrimitiveMesh mesh = particleGeometry switch
        {
            // The linked GodotExplosionVFX atlas is a high-resolution 8x8
            // flipbook.  Use it on camera-facing quads rather than stacking
            // opaque spheres: the sprite carries the fine smoke breakup and
            // the shader supplies the source project's ramp/normal treatment.
            ParticleGeometry.Fire or ParticleGeometry.Smoke => new QuadMesh
            {
                Size = new Vector2(1.0f, 1.0f)
            },
            ParticleGeometry.Spark => new BoxMesh
            {
                Size = new Vector3(0.18f, 1.0f, 0.18f)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(geometry))
        };
        if (particleGeometry == ParticleGeometry.Spark)
        {
            mesh.Material = visualMaterial;
        }

        var particles = new GpuParticles3D
        {
            Name = name,
            Emitting = true,
            Amount = amount,
            Lifetime = lifetime,
            OneShot = oneShot,
            Explosiveness = explosiveness,
            Randomness = 0.65f,
            LocalCoords = true,
            VisibilityAabb = new Aabb(new Vector3(-35.0f, -10.0f, -35.0f), new Vector3(70.0f, 70.0f, 70.0f)),
            ProcessMaterial = processMaterial,
            DrawPass1 = mesh
        };

        if (particleGeometry != ParticleGeometry.Spark)
        {
            // GPUParticles3D's material override is what the source project
            // uses; it also ensures the shader sees INSTANCE_CUSTOM for frame
            // animation when the draw pass is a QuadMesh.
            particles.MaterialOverride = visualMaterial;
        }

        return particles;
    }

    private static ShaderMaterial CreateParticleShaderMaterial(bool smoke)
    {
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode blend_mix, cull_disabled, diffuse_lambert, specular_schlick_ggx;

                // Adapted from memo1918/GodotExplosionVFX.  The source uses
                // an 8x8 flipbook, particle phase, and three colour ramps.
                uniform float particle_h_frames = 8.0;
                uniform float particle_v_frames = 8.0;
                uniform sampler2D smoke_texture : source_color;
                uniform sampler2D base_ramp : source_color;
                uniform sampler2D phase_ramp : source_color;
                uniform sampler2D emission_ramp : source_color;
                uniform sampler2D normal_plus : hint_normal;
                uniform sampler2D normal_minus : hint_normal;
                uniform float emission_strength = 1.0;

                void vertex() {
                    // Camera-facing particle quad, matching the source
                    // project's custom billboard transform.
                    mat4 mat_world = mat4(
                        normalize(INV_VIEW_MATRIX[0]) * length(MODEL_MATRIX[0]),
                        normalize(INV_VIEW_MATRIX[1]) * length(MODEL_MATRIX[0]),
                        normalize(INV_VIEW_MATRIX[2]) * length(MODEL_MATRIX[2]),
                        MODEL_MATRIX[3]);
                    MODELVIEW_MATRIX = VIEW_MATRIX * mat_world;

                    float total_frames = particle_h_frames * particle_v_frames;
                    float particle_frame = clamp(
                        floor(INSTANCE_CUSTOM.y * total_frames),
                        0.0,
                        total_frames - 1.0);
                    UV /= vec2(particle_h_frames, particle_v_frames);
                    UV += vec2(
                        mod(particle_frame, particle_h_frames) / particle_h_frames,
                        floor(particle_frame / particle_h_frames) / particle_v_frames);
                    COLOR.rgb = INSTANCE_CUSTOM.xyz;
                }

                void fragment() {
                    vec4 sprite = texture(smoke_texture, UV);
                    float density = clamp(max(sprite.r, max(sprite.g, sprite.b)), 0.0, 1.0);
                    float phase = clamp(COLOR.g, 0.0, 1.0);
                    float phase_emission = texture(phase_ramp, vec2(phase, 0.5)).r;
                    vec3 colour = texture(base_ramp, vec2(density, 0.5)).rgb;
                    vec3 emission = texture(
                        emission_ramp,
                        vec2(clamp(density + phase_emission * 0.5, 0.0, 1.0), 0.5)).rgb;
                    float alpha = sprite.a;
                    if (alpha < 0.008) {
                        discard;
                    }

                    ALBEDO = colour;
                    EMISSION = emission * emission_strength;
                    NORMAL = normalize(texture(normal_plus, UV).xyz - texture(normal_minus, UV).xyz);
                    ALPHA = alpha;
                }
                """
        };
        var material = new ShaderMaterial
        {
            Shader = shader
        };
        var smokeTexture = ResourceLoader.Load<Texture2D>("res://Assets/Vfx/smokesprite.png");
        var normalPlus = ResourceLoader.Load<Texture2D>("res://Assets/Vfx/normal-plus.png");
        var normalMinus = ResourceLoader.Load<Texture2D>("res://Assets/Vfx/normal-minus.png");
        if (smokeTexture == null || normalPlus == null || normalMinus == null)
        {
            GD.PushWarning("MechRewired: GodotExplosionVFX textures were not imported; using a transparent fallback.");
        }
        else
        {
            material.SetShaderParameter("smoke_texture", smokeTexture);
            material.SetShaderParameter("normal_plus", normalPlus);
            material.SetShaderParameter("normal_minus", normalMinus);
            if (!s_vfxTexturesLogged)
            {
                GD.Print("MechRewired: loaded GodotExplosionVFX 8x8 smoke flipbook and normal maps.");
                s_vfxTexturesLogged = true;
            }
        }

        material.SetShaderParameter(
            "base_ramp",
            smoke
                ? CreateColorRamp(
                    (0.0f, new Color(0.03f, 0.025f, 0.02f, 1.0f)),
                    (0.18f, new Color(0.10f, 0.095f, 0.09f, 1.0f)),
                    (0.60f, new Color(0.32f, 0.30f, 0.27f, 1.0f)),
                    (1.0f, new Color(0.58f, 0.56f, 0.52f, 1.0f)))
                : CreateColorRamp(
                    (0.0f, new Color(0.0f, 0.0f, 0.0f, 1.0f)),
                    (0.50f, new Color(0.18f, 0.025f, 0.002f, 1.0f)),
                    (1.0f, new Color(0.78f, 0.20f, 0.01f, 1.0f))));
        material.SetShaderParameter(
            "phase_ramp",
            CreateColorRamp(
                (0.0f, Colors.Black),
                (0.043f, new Color(0.44f, 0.44f, 0.44f, 1.0f)),
                (0.32f, Colors.White)));
        material.SetShaderParameter(
            "emission_ramp",
            smoke
                ? CreateColorRamp(
                    (0.0f, new Color(0.0f, 0.0f, 0.0f, 1.0f)),
                    (1.0f, new Color(0.035f, 0.025f, 0.018f, 1.0f)))
                : CreateColorRamp(
                    // This is the source material's sequence: a short white
                    // flash, orange flame, then red/black as the atlas fades.
                    (0.0f, new Color(0.0f, 0.0f, 0.0f, 1.0f)),
                    (0.38f, new Color(0.965f, 0.965f, 0.965f, 1.0f)),
                    (0.461f, new Color(1.0f, 0.8f, 0.502f, 1.0f)),
                    (0.55f, new Color(1.0f, 0.0f, 0.0f, 1.0f)),
                    (0.602f, new Color(0.0f, 0.0f, 0.0f, 1.0f))));
        material.SetShaderParameter("emission_strength", smoke ? 0.04f : 0.55f);
        return material;
    }

    private static GradientTexture1D CreateColorRamp(params (float Offset, Color Color)[] stops) =>
        new()
        {
            Gradient = new Gradient
            {
                Colors = stops.Select(stop => stop.Color).ToArray(),
                Offsets = stops.Select(stop => stop.Offset).ToArray()
            }
        };

    private static OmniLight3D CreateFireLight(float range, float energy) =>
        new()
        {
            Name = "FireLight",
            LightColor = new Color(1.0f, 0.28f, 0.04f),
            LightEnergy = energy,
            OmniRange = range,
            ShadowEnabled = false
        };

    private static AudioStreamPlayer3D CreatePositionalAudio(
        string name,
        AudioStreamWav stream,
        float unitSize,
        float maximumDistance,
        float volumeDb) =>
        new()
        {
            Name = name,
            Stream = stream,
            Autoplay = true,
            UnitSize = unitSize,
            MaxDistance = maximumDistance,
            VolumeDb = volumeDb,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance
        };

    private sealed class AmbientEffectState
    {
        public AmbientEffectState(bool isFire, Aabb volume, string sourceName, AudioStreamWav ambientSound)
        {
            IsFire = isFire;
            Volume = volume;
            SourceName = sourceName;
            AmbientSound = ambientSound;
        }

        public bool IsFire { get; }

        public Aabb Volume { get; }

        public string SourceName { get; }

        public AudioStreamWav AmbientSound { get; }

        public EffectInstance Instance { get; set; }

        public bool IsCulled { get; set; }

        public string KindName => IsFire ? "fire" : "smoke";
    }

    private enum ParticleGeometry
    {
        Fire,
        Smoke,
        Spark
    }

    private enum DebugVfxParameter
    {
        FireDensity,
        FireSize,
        FireRise,
        FireBrightness,
        SmokeDensity,
        SmokeSize,
        SmokeRise,
        SmokeLifetime
    }

    private sealed record TunableEmitter(
        GpuParticles3D Particles,
        ParticleGeometry Geometry,
        int BaseAmount,
        double BaseLifetime,
        float BaseVelocityMin,
        float BaseVelocityMax,
        Vector3 BaseGravity,
        float BaseScaleMin,
        float BaseScaleMax,
        float BaseSpread);

    private sealed partial class ImpactEffect : Node3D
    {
        private float m_age;

        public override void _Process(double delta)
        {
            m_age += (float)delta;
            if (m_age >= 0.9f)
            {
                QueueFree();
            }
        }
    }

    private sealed partial class EffectInstance : Node3D
    {
        private readonly bool m_ambient;
        private float m_age;

        public EffectInstance(bool ambient)
        {
            m_ambient = ambient;
        }

        public GpuParticles3D LingeringSmoke { get; set; }

        public OmniLight3D ExplosionLight { get; set; }

        public override void _Process(double delta)
        {
            m_age += (float)delta;
            if (m_ambient)
            {
                var fireLight = GetNodeOrNull<OmniLight3D>("FireLight");
                if (fireLight != null)
                {
                    fireLight.LightEnergy = 9.0f + MathF.Sin(m_age * 11.0f) * 1.4f;
                }

                return;
            }

            if (ExplosionLight != null)
            {
                ExplosionLight.LightEnergy = Math.Max(0.0f, 22.0f * (1.0f - m_age / 0.9f));
            }

        }
    }
}
