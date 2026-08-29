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
    private const float FullDetailEffectDistance = 250.0f;
    private const float MinimumAmbientAmountRatio = 0.12f;
    private const int WeaponImpactPoolSize = 24;
    private const int DestructionPoolSize = 12;
    private const int DustPoolSize = 64;
    private const float ExplosionFogLifetimeSeconds = 4.8f;
    private static readonly Vector3 DustWindDirection = new(0.82f, 0.0f, 0.57f);

    private static bool s_vfxTexturesLogged;

    private readonly IReadOnlyList<AudioStreamWav> m_explosionSounds;
    private readonly List<TunableEmitter> m_tunableEmitters = [];
    private readonly List<AmbientEffectState> m_ambientEffects = [];
    private readonly List<Node3D> m_distanceBoundEffects = [];
    private readonly List<ImpactEffect> m_weaponImpactPool = [];
    private readonly List<EffectInstance> m_destructionPool = [];
    private readonly List<DustEffect> m_dustPool = [];
    private ShaderMaterial m_fireVisualMaterial;
    private ShaderMaterial m_smokeVisualMaterial;
    private ShaderMaterial m_dustVisualMaterial;
    private StandardMaterial3D m_sparkVisualMaterial;
    private QuadMesh m_particleQuadMesh;
    private BoxMesh m_sparkMesh;
    private GradientTexture1D m_fireColorRamp;
    private GradientTexture1D m_ambientFireColorRamp;
    private GradientTexture1D m_ambientSmokeColorRamp;
    private GradientTexture1D m_ambientSmokeInitialColorRamp;
    private GradientTexture1D m_smokeColorRamp;
    private GradientTexture1D m_smokeInitialColorRamp;
    private TerrainSurfaceIndex m_terrainSurface;
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
    private float m_dustBrightness = 1.0f;
    private float m_dustWind = 1.0f;
    private float m_dustLifetime = 1.0f;
    private float m_dustSpread = 68.0f;
    private float m_explosionFogDensity = 0.40f;

    public BattlefieldEffects(IReadOnlyList<AudioStreamWav> explosionSounds)
    {
        ArgumentNullException.ThrowIfNull(explosionSounds);
        m_explosionSounds = explosionSounds;
    }

    public override void _Ready()
    {
        CreateEffectPools();
    }

    public void ConfigureTerrain(TerrainSurfaceIndex terrainSurface)
    {
        ArgumentNullException.ThrowIfNull(terrainSurface);
        m_terrainSurface = terrainSurface;
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

    /// <summary>Controls the initial density of the short-lived volumetric smoke at major explosions.</summary>
    public float ExplosionFogDensity
    {
        get => m_explosionFogDensity;
        set => m_explosionFogDensity = Mathf.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>Spawns only the major-explosion fog volume nearby so it can be tuned in isolation.</summary>
    public void SpawnExplosionFogTest()
    {
        if (!IsInstanceValid(m_observer))
        {
            return;
        }

        var forward = -m_observer.GlobalBasis.Z;
        if (forward.IsZeroApprox())
        {
            forward = Vector3.Forward;
        }

        var hitPosition = m_observer.GlobalPosition + forward.Normalized() * 18.0f;
        hitPosition.Y = FindTerrainHeight(hitPosition, hitPosition.Y);
        const float testBoundsLength = 52.0f;
        var fog = CreateExplosionFog();
        ConfigureExplosionFog(fog, testBoundsLength, m_explosionFogDensity);
        var effect = new FogTestEffect(fog, m_explosionFogDensity)
        {
            Name = "ExplosionFogTest",
            Position = hitPosition
        };
        effect.AddChild(fog);
        AddChild(effect);
        GD.Print($"MechRewired: spawning fog-only test at density {m_explosionFogDensity:F2}.");
    }

    private void CreateEffectPools()
    {
        for (var index = 0; index < WeaponImpactPoolSize; index++)
        {
            var particles = CreateFire(true, 28, 0.52f, 1.0f);
            particles.Emitting = false;
            var sparks = CreateSparks(0.075f);
            sparks.Emitting = false;
            var light = CreateFireLight(4.5f, 4.0f);
            light.Visible = false;
            var effect = new ImpactEffect(particles, sparks, light)
            {
                Name = $"WeaponImpactPool{index}",
                Visible = false,
                ProcessMode = ProcessModeEnum.Disabled
            };
            effect.AddChild(particles);
            effect.AddChild(sparks);
            effect.AddChild(light);
            AddChild(effect);
            m_weaponImpactPool.Add(effect);
        }

        for (var index = 0; index < DestructionPoolSize; index++)
        {
            var effect = new EffectInstance(false, true)
            {
                Name = $"DestructionPool{index}",
                Visible = false,
                ProcessMode = ProcessModeEnum.Disabled,
                ExplosionFire = CreateFire(true, 40, 1.05f, 3.0f),
                ExplosionSmoke = CreateSmoke(true, 34, 4.5f, 2.5f),
                Sparks = CreateSparks(0.2f),
                LingeringSmoke = CreateSmoke(false, 76, 7.0f, 2.2f),
                ExplosionFog = CreateExplosionFog(),
                ExplosionLight = CreateFireLight(7.0f, 22.0f)
            };
            effect.ExplosionFire.Emitting = false;
            effect.ExplosionSmoke.Emitting = false;
            effect.Sparks.Emitting = false;
            effect.LingeringSmoke.Emitting = false;
            effect.ExplosionFog.Visible = false;
            effect.ExplosionLight.Visible = false;
            effect.AddChild(effect.ExplosionFire);
            effect.AddChild(effect.ExplosionSmoke);
            effect.AddChild(effect.Sparks);
            effect.AddChild(effect.LingeringSmoke);
            effect.AddChild(effect.ExplosionFog);
            effect.AddChild(effect.ExplosionLight);
            if (m_explosionSounds.Count > 0)
            {
                effect.ExplosionAudio = CreatePositionalAudio(
                    "ExplosionSound",
                    m_explosionSounds[0],
                    24.0f,
                    1000.0f,
                    1.0f);
                effect.ExplosionAudio.Autoplay = false;
                effect.AddChild(effect.ExplosionAudio);
            }

            AddChild(effect);
            m_destructionPool.Add(effect);
        }

        for (var index = 0; index < DustPoolSize; index++)
        {
            var particles = CreateDust(48, 1.1f, 1.0f, 2.0f);
            particles.Emitting = false;
            var effect = new DustEffect(particles)
            {
                Name = $"DustPool{index}",
                Visible = false,
                ProcessMode = ProcessModeEnum.Disabled
            };
            effect.AddChild(particles);
            AddChild(effect);
            m_dustPool.Add(effect);
        }
    }

    public void AddAmbientFire(
        Aabb authoredFireVolume,
        Aabb authoredPlumeVolume,
        float authoredGroundHeight,
        string sourceName,
        AudioStreamWav ambientSound)
    {
        var fireVolume = AlignToTerrain(authoredFireVolume, authoredGroundHeight);
        var plumeVolume = AlignToTerrain(authoredPlumeVolume, authoredGroundHeight);
        AddAmbientEffect(new AmbientEffectState(
            true,
            MergeBounds(fireVolume, plumeVolume),
            sourceName,
            ambientSound,
            fireVolume,
            plumeVolume));
    }

    public void AddAmbientSmoke(
        Aabb authoredVolume,
        float authoredGroundHeight,
        string sourceName,
        AudioStreamWav ambientSound)
    {
        AddAmbientEffect(new AmbientEffectState(
            false,
            AlignToTerrain(authoredVolume, authoredGroundHeight),
            sourceName,
            ambientSound));
    }

    private void AddAmbientEffect(AmbientEffectState definition)
    {
        definition.Instance = CreateAmbientEffect(definition);
        definition.Instance.Visible = false;
        definition.Instance.ProcessMode = ProcessModeEnum.Disabled;
        foreach (var particles in definition.Instance.GetChildren().OfType<GpuParticles3D>())
        {
            particles.Emitting = false;
        }

        foreach (var audio in definition.Instance.GetChildren().OfType<AudioStreamPlayer3D>())
        {
            audio.Autoplay = false;
            audio.Stop();
        }

        AddChild(definition.Instance);
        m_ambientEffects.Add(definition);
        UpdateDistanceBoundEffects();
    }

    private Aabb AlignToTerrain(Aabb volume, float authoredGroundHeight)
    {
        var center = volume.GetCenter();
        var terrainHeight = FindTerrainHeight(center, authoredGroundHeight);
        volume.Position += Vector3.Up * (terrainHeight - authoredGroundHeight);
        return volume;
    }

    private static Aabb MergeBounds(Aabb first, Aabb second)
    {
        var minimum = first.Position.Min(second.Position);
        var maximum = first.End.Max(second.End);
        return new Aabb(minimum, maximum - minimum);
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

        var activatedAmbientEffect = false;
        foreach (var ambientEffect in m_ambientEffects)
        {
            if (ambientEffect.IsCulled)
            {
                continue;
            }

            var isWithinRange = IsWithinEffectPersistenceRange(ambientEffect.Volume.GetCenter());
            if (!ambientEffect.IsActive && isWithinRange && !activatedAmbientEffect)
            {
                ActivateAmbientEffect(ambientEffect);
                UpdateAmbientDetail(ambientEffect.Instance, ambientEffect.Volume.GetCenter());
                activatedAmbientEffect = true;
                GD.Print($"MechRewired: activated ambient {ambientEffect.KindName} '{ambientEffect.SourceName}' within {EffectPersistenceRadius:F0}m.");
            }
            else if (ambientEffect.IsActive && !isWithinRange)
            {
                DeactivateAmbientEffect(ambientEffect);
                ambientEffect.IsCulled = true;
                GD.Print($"MechRewired: culled ambient {ambientEffect.KindName} '{ambientEffect.SourceName}' beyond {EffectPersistenceRadius:F0}m.");
            }
            else if (ambientEffect.IsActive)
            {
                UpdateAmbientDetail(ambientEffect.Instance, ambientEffect.Volume.GetCenter());
            }
        }

        for (var index = m_distanceBoundEffects.Count - 1; index >= 0; index--)
        {
            var effect = m_distanceBoundEffects[index];
            if (effect is IPooledEffect { IsActive: false })
            {
                m_distanceBoundEffects.RemoveAt(index);
                continue;
            }

            if (!IsInstanceValid(effect))
            {
                m_distanceBoundEffects.RemoveAt(index);
                continue;
            }

            if (!IsWithinEffectPersistenceRange(effect.GlobalPosition))
            {
                if (effect is IPooledEffect pooledEffect)
                {
                    pooledEffect.Deactivate();
                }
                else
                {
                    effect.QueueFree();
                }

                m_distanceBoundEffects.RemoveAt(index);
                GD.Print($"MechRewired: culled transient battlefield effect beyond {EffectPersistenceRadius:F0}m.");
            }
        }
    }

    private static void ActivateAmbientEffect(AmbientEffectState definition)
    {
        definition.IsActive = true;
        definition.Instance.Visible = true;
        definition.Instance.ProcessMode = ProcessModeEnum.Inherit;
        foreach (var particles in definition.Instance.GetChildren().OfType<GpuParticles3D>())
        {
            particles.Emitting = true;
            particles.Restart();
        }

        foreach (var audio in definition.Instance.GetChildren().OfType<AudioStreamPlayer3D>())
        {
            audio.Play();
        }
    }

    private static void DeactivateAmbientEffect(AmbientEffectState definition)
    {
        definition.IsActive = false;
        foreach (var particles in definition.Instance.GetChildren().OfType<GpuParticles3D>())
        {
            particles.Emitting = false;
        }

        foreach (var audio in definition.Instance.GetChildren().OfType<AudioStreamPlayer3D>())
        {
            audio.Stop();
        }

        definition.Instance.Visible = false;
        definition.Instance.ProcessMode = ProcessModeEnum.Disabled;
    }

    private EffectInstance CreateAmbientEffect(AmbientEffectState definition)
    {
        var volume = definition.Volume;
        var fireVolume = definition.FireVolume ?? volume;
        var plumeVolume = definition.PlumeVolume ?? volume;
        var position = new Vector3(
            fireVolume.GetCenter().X,
            fireVolume.Position.Y,
            fireVolume.GetCenter().Z);
        var effect = new EffectInstance(true)
        {
            Name = $"Ambient{definition.KindName}-{definition.SourceName}",
            Position = position
        };
        if (definition.IsFire)
        {
            effect.AddChild(CreateAmbientFireParticles(fireVolume.Size.X, fireVolume.Size.Y));
            var smoke = CreateAmbientSmokeParticles(
                Math.Max(fireVolume.Size.X * 0.7f, plumeVolume.Size.X),
                Math.Max(fireVolume.Size.Y * 1.3f, plumeVolume.Size.Y));
            smoke.Position = new Vector3(
                plumeVolume.GetCenter().X - position.X,
                0.0f,
                plumeVolume.GetCenter().Z - position.Z);
            effect.AddChild(smoke);
            effect.AddChild(CreateFireLight(
                fireVolume.Size.X * 0.65f,
                14.0f + fireVolume.Size.X * 0.12f));
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

    private void UpdateAmbientDetail(EffectInstance effect, Vector3 position)
    {
        var distance = m_observer.GlobalPosition.DistanceTo(position);
        var amountRatio = Mathf.Lerp(
            1.0f,
            MinimumAmbientAmountRatio,
            Mathf.Clamp(
                (distance - FullDetailEffectDistance) /
                (EffectPersistenceRadius - FullDetailEffectDistance),
                0.0f,
                1.0f));
        foreach (var particles in effect.GetChildren().OfType<GpuParticles3D>())
        {
            particles.AmountRatio = amountRatio;
        }
    }

    public void SpawnDestruction(BattlefieldActor actor, Vector3 hitPosition)
    {
        ArgumentNullException.ThrowIfNull(actor);
        SpawnDestruction(
            actor.Name,
            actor.Definition.ObjectId,
            actor.DestructionBounds,
            GetDestructionSmokeOrigin(actor, actor.DestructionBounds),
            hitPosition);
    }

    /// <summary>
    /// Spawns destruction effects for a dynamic combat actor such as an enemy mech.
    /// </summary>
    public void SpawnDestruction(
        string actorName,
        int soundVariant,
        Aabb bounds,
        Vector3 hitPosition,
        AudioStreamWav explosionSound = null)
    {
        var plumePosition = bounds.GetCenter();
        plumePosition.Y = FindTerrainHeight(plumePosition, bounds.Position.Y);
        SpawnDestruction(actorName, soundVariant, bounds, plumePosition, hitPosition, explosionSound);
    }

    private void SpawnDestruction(
        string actorName,
        int soundVariant,
        Aabb bounds,
        Vector3 plumePosition,
        Vector3 hitPosition,
        AudioStreamWav explosionSound = null)
    {
        if (!IsWithinEffectPersistenceRange(hitPosition))
        {
            GD.Print($"MechRewired: skipped distant destruction effect for {actorName} beyond {EffectPersistenceRadius:F0}m.");
            return;
        }

        var effect = AcquireDestructionEffect();
        effect.Name = $"Destruction-{actorName}";
        var localHit = hitPosition - plumePosition;
        var boundsLength = bounds.Size.Length();
        ConfigureFire(
            effect.ExplosionFire,
            40,
            1.05f,
            Math.Clamp(boundsLength * 0.12f, 2.5f, 7.0f));
        ConfigureSmoke(
            effect.ExplosionSmoke,
            true,
            34,
            4.5f,
            Math.Clamp(boundsLength * 0.1f, 1.8f, 4.0f));
        ConfigureSparks(effect.Sparks, Math.Clamp(boundsLength * 0.03f, 0.12f, 0.3f));
        ConfigureSmoke(
            effect.LingeringSmoke,
            false,
            76,
            7.0f,
            Math.Clamp(boundsLength * 0.08f, 1.5f, 3.5f));
        ConfigureExplosionFog(effect.ExplosionFog, boundsLength, m_explosionFogDensity);
        effect.ExplosionFogDensity = m_explosionFogDensity;
        effect.ExplosionLight.OmniRange = Math.Clamp(boundsLength * 0.45f, 5.0f, 13.0f);
        effect.ExplosionLight.LightEnergy = 22.0f;
        if (effect.ExplosionAudio != null && (explosionSound != null || m_explosionSounds.Count > 0))
        {
            effect.ExplosionAudio.Stream = explosionSound ??
                                           m_explosionSounds[Math.Abs(soundVariant) % m_explosionSounds.Count];
        }

        effect.Activate(plumePosition, localHit);
        m_distanceBoundEffects.Add(effect);
        SpawnDust(
            plumePosition,
            Math.Clamp(boundsLength * 0.18f, 1.8f, 7.0f),
            1.7f,
            1.6f);
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

        var effect = AcquireWeaponImpactEffect();
        var size = Math.Clamp(0.24f * m_fireSize, 0.55f, 1.8f);
        var amount = Math.Clamp((int)MathF.Round(7.0f * m_fireDensity), 8, 28);
        ConfigureFire(effect.Particles, amount, 0.52f, size);
        ConfigureSparks(effect.Sparks, Math.Clamp(size * 0.09f, 0.06f, 0.16f));
        var process = (ParticleProcessMaterial)effect.Particles.ProcessMaterial;
        process.InitialVelocityMin *= Math.Clamp(m_fireRise * 0.3f, 0.7f, 1.7f);
        process.InitialVelocityMax *= Math.Clamp(m_fireRise * 0.3f, 0.7f, 1.7f);
        if (effect.Particles.MaterialOverride is ShaderMaterial material)
        {
            material.SetShaderParameter("emission_strength", 0.55f * m_fireBrightness);
        }

        effect.Light.OmniRange = 4.5f * size;
        effect.Light.LightEnergy = 4.0f * m_fireBrightness;
        effect.Activate(hitPosition);
        m_distanceBoundEffects.Add(effect);
        SpawnDust(hitPosition, 0.8f, 0.8f, 0.9f);
    }

    /// <summary>Spawns a brief, terrain-hugging dust puff at a planted mech foot.</summary>
    public void SpawnFootfallDust(Vector3 position, float intensity) =>
        SpawnDust(
            position,
            1.35f + intensity * 0.55f,
            0.18f + intensity * 0.10f,
            2.3f,
            amountRatio: 0.36f,
            spread: 88.0f,
            emissionBoxExtents: new Vector3(1.15f + intensity * 0.35f, 0.14f, 1.15f + intensity * 0.35f));

    /// <summary>Spawns short-lived downwash dust beneath a low-flying DropShip.</summary>
    public void SpawnDropShipDownwash(Vector3 position, float intensity)
    {
        var size = 3.6f + intensity * 3.4f;
        // A DropShip's engines disturb a broad footprint, not a mech-sized point puff.
        // Keep the cloud close to the ground and distribute it across roughly 10-24m.
        SpawnDust(
            position,
            size,
            0.72f + intensity * 0.38f,
            4.5f,
            amountRatio: 0.34f,
            spread: 88.0f,
            emissionBoxExtents: new Vector3(5.0f + intensity * 7.0f, 0.18f, 5.0f + intensity * 7.0f));
    }

    private void SpawnDust(
        Vector3 position,
        float size,
        float rise,
        float lifetime,
        float? emissionRadius = null,
        float? amountRatio = null,
        float? spread = null,
        Vector3? emissionBoxExtents = null)
    {
        if (!IsWithinEffectPersistenceRange(position))
        {
            return;
        }

        var effect = AcquireDustEffect();
        ConfigureDust(effect.Particles, size, rise, lifetime, emissionRadius, amountRatio, spread, emissionBoxExtents);
        effect.Activate(new Vector3(position.X, FindTerrainHeight(position, position.Y), position.Z));
        m_distanceBoundEffects.Add(effect);
    }

    private ImpactEffect AcquireWeaponImpactEffect()
    {
        var effect = m_weaponImpactPool.FirstOrDefault(candidate => !candidate.IsActive) ??
                     m_weaponImpactPool.MaxBy(candidate => candidate.Age) ??
                     throw new InvalidOperationException("The weapon-impact VFX pool is empty.");
        m_distanceBoundEffects.Remove(effect);
        if (effect.IsActive)
        {
            effect.Deactivate();
        }

        return effect;
    }

    private EffectInstance AcquireDestructionEffect()
    {
        var effect = m_destructionPool.FirstOrDefault(candidate => !candidate.IsActive) ??
                     m_destructionPool.MaxBy(candidate => candidate.Age) ??
                     throw new InvalidOperationException("The destruction VFX pool is empty.");
        m_distanceBoundEffects.Remove(effect);
        if (effect.IsActive)
        {
            effect.Deactivate();
            GD.Print("MechRewired: recycled the oldest active destruction VFX pool entry.");
        }

        return effect;
    }

    private DustEffect AcquireDustEffect()
    {
        var effect = m_dustPool.FirstOrDefault(candidate => !candidate.IsActive) ??
                     m_dustPool.MaxBy(candidate => candidate.Age) ??
                     throw new InvalidOperationException("The dust VFX pool is empty.");
        m_distanceBoundEffects.Remove(effect);
        if (effect.IsActive)
        {
            effect.Deactivate();
        }

        return effect;
    }

    private static void ConfigureFire(
        GpuParticles3D particles,
        int amount,
        float lifetime,
        float size)
    {
        particles.AmountRatio = Mathf.Clamp((float)amount / particles.Amount, 0.0f, 1.0f);
        particles.Lifetime = lifetime;
        var process = (ParticleProcessMaterial)particles.ProcessMaterial;
        process.InitialVelocityMin = 5.0f;
        process.InitialVelocityMax = 13.0f;
        process.ScaleMin = size * 0.45f;
        process.ScaleMax = size;
        process.EmissionSphereRadius = size * 0.45f;
    }

    private static void ConfigureSmoke(
        GpuParticles3D particles,
        bool oneShot,
        int amount,
        float lifetime,
        float size)
    {
        particles.AmountRatio = Mathf.Clamp((float)amount / particles.Amount, 0.0f, 1.0f);
        particles.Lifetime = lifetime;
        var process = (ParticleProcessMaterial)particles.ProcessMaterial;
        process.InitialVelocityMin = oneShot ? 2.5f : 1.0f;
        process.InitialVelocityMax = oneShot ? 7.5f : 2.6f;
        process.ScaleMin = size * 0.35f;
        process.ScaleMax = size * 1.65f;
        process.EmissionSphereRadius = Math.Max(0.4f, size * (oneShot ? 0.55f : 0.3f));
    }

    private static void ConfigureSparks(GpuParticles3D particles, float size)
    {
        var process = (ParticleProcessMaterial)particles.ProcessMaterial;
        process.ScaleMin = size * 0.35f;
        process.ScaleMax = size;
        process.EmissionSphereRadius = 0.7f;
    }

    private static void ConfigureExplosionFog(FogVolume fog, float boundsLength, float density)
    {
        var radius = Math.Clamp(boundsLength * 0.27f, 5.5f, 15.0f);
        fog.Size = new Vector3(radius * 2.0f, radius * 1.20f, radius * 2.0f);
        if (fog.Material is ShaderMaterial material)
        {
            material.SetShaderParameter("smoke_density", density);
        }
    }

    private void ConfigureDust(
        GpuParticles3D particles,
        float size,
        float rise,
        float lifetime,
        float? emissionRadius,
        float? amountRatio,
        float? spread,
        Vector3? emissionBoxExtents)
    {
        particles.AmountRatio = amountRatio ?? Mathf.Clamp(0.25f + size * 0.16f, 0.25f, 1.0f);
        particles.Lifetime = lifetime * m_dustLifetime;
        var process = (ParticleProcessMaterial)particles.ProcessMaterial;
        process.Direction = (Vector3.Up + DustWindDirection * (0.10f * m_dustWind)).Normalized();
        // A wide cone gives planted feet and the DropShip's downwash an outward poof;
        // the small horizontal acceleration subsequently biases that cloud with the wind.
        process.Spread = spread ?? m_dustSpread;
        process.InitialVelocityMin = 0.30f * rise;
        process.InitialVelocityMax = 0.75f * rise;
        process.Gravity = new Vector3(
            DustWindDirection.X * (0.08f * m_dustWind),
            -0.14f,
            DustWindDirection.Z * (0.08f * m_dustWind));
        process.DampingMin = 0.42f;
        process.DampingMax = 0.82f;
        process.ScaleMin = size * 0.42f;
        process.ScaleMax = size * 1.15f;
        if (emissionBoxExtents is { } boxExtents)
        {
            process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
            process.EmissionBoxExtents = boxExtents;
        }
        else
        {
            process.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
            process.EmissionSphereRadius = emissionRadius ?? Math.Max(0.35f, size * 0.55f);
        }
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
        return m_terrainSurface != null && m_terrainSurface.TryGetHeight(position, out var height)
            ? height + 0.05f
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
            ColorRamp = m_fireColorRamp ??= CreateColorRamp(
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
            ColorRamp = m_ambientFireColorRamp ??= CreateColorRamp(
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
            ColorRamp = m_ambientSmokeColorRamp ??= CreateColorRamp(
                (0.0f, new Color(0.12f, 0.11f, 0.1f, 0.0f)),
                (0.1f, new Color(0.14f, 0.13f, 0.12f, 0.82f)),
                (0.6f, new Color(0.35f, 0.34f, 0.32f, 0.62f)),
                (1.0f, new Color(0.62f, 0.61f, 0.58f, 0.0f))),
            ColorInitialRamp = m_ambientSmokeInitialColorRamp ??= CreateColorRamp(
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
            ColorRamp = m_smokeColorRamp ??= CreateColorRamp(
                (0.0f, new Color(0.08f, 0.07f, 0.06f, 0.0f)),
                (0.12f, new Color(0.1f, 0.09f, 0.08f, 0.88f)),
                (0.62f, new Color(0.25f, 0.24f, 0.22f, 0.58f)),
                (1.0f, new Color(0.42f, 0.41f, 0.39f, 0.0f))),
            ColorInitialRamp = m_smokeInitialColorRamp ??= CreateColorRamp(
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

    private GpuParticles3D CreateDust(int amount, float lifetime, float size, float rise)
    {
        var material = new ParticleProcessMaterial
        {
            Direction = (Vector3.Up + DustWindDirection * 0.10f).Normalized(),
            Spread = 68.0f,
            InitialVelocityMin = 0.30f * rise,
            InitialVelocityMax = 0.75f * rise,
            Gravity = new Vector3(
                DustWindDirection.X * 0.08f,
                -0.14f,
                DustWindDirection.Z * 0.08f),
            DampingMin = 0.42f,
            DampingMax = 0.82f,
            ScaleMin = size * 0.42f,
            ScaleMax = size * 1.15f,
            ColorRamp = CreateColorRamp(
                (0.0f, new Color(0.68f, 0.61f, 0.48f, 0.0f)),
                (0.15f, new Color(0.78f, 0.70f, 0.55f, 0.48f)),
                (0.65f, new Color(0.70f, 0.63f, 0.50f, 0.28f)),
                (1.0f, new Color(0.58f, 0.52f, 0.41f, 0.0f))),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 0.55f,
            TurbulenceNoiseScale = 2.8f,
            TurbulenceNoiseSpeed = new Vector3(0.25f, 0.12f, 0.2f),
            TurbulenceInfluenceMin = 0.08f,
            TurbulenceInfluenceMax = 0.22f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = Math.Max(0.35f, size * 0.55f)
        };
        return CreateParticles("Dust", true, amount, lifetime, 0.92f, material, false, ParticleGeometry.Dust);
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
    /// F5 selects a parameter, F6/F7 reduce/increase it, F10 restores the
    /// authored defaults, and F11 writes the current preset to the log. Hold
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
            case Key.F10:
                ResetDebugTuning();
                return true;
            case Key.F11:
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
        var emitter = new TunableEmitter(
            particles,
            geometry,
            particles.Amount,
            particles.Lifetime,
            process.InitialVelocityMin,
            process.InitialVelocityMax,
            process.Gravity,
            process.ScaleMin,
            process.ScaleMax,
            process.Spread);
        m_tunableEmitters.Add(emitter);
        ApplyDebugTuning(emitter, false);
    }

    private void AdjustDebugParameter(float adjustment)
    {
        ref var selected = ref GetSelectedDebugParameter();
        if (m_selectedDebugParameter == DebugVfxParameter.DustSpread)
        {
            selected = Math.Clamp(selected + adjustment, 20.0f, 85.0f);
        }
        else
        {
            selected = Math.Clamp(selected + adjustment, 0.1f, 5.0f);
        }

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
            case DebugVfxParameter.DustBrightness:
                return ref m_dustBrightness;
            case DebugVfxParameter.DustWind:
                return ref m_dustWind;
            case DebugVfxParameter.DustLifetime:
                return ref m_dustLifetime;
            case DebugVfxParameter.DustSpread:
                return ref m_dustSpread;
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
        m_dustBrightness = 1.0f;
        m_dustWind = 1.0f;
        m_dustLifetime = 1.0f;
        m_dustSpread = 68.0f;
        ApplyDebugTuning();
        LogDebugTuning();
    }

    private void ApplyDebugTuning()
    {
        ApplyDustMaterialTuning();
        for (var index = m_tunableEmitters.Count - 1; index >= 0; index--)
        {
            var emitter = m_tunableEmitters[index];
            if (!GodotObject.IsInstanceValid(emitter.Particles))
            {
                m_tunableEmitters.RemoveAt(index);
                continue;
            }

            ApplyDebugTuning(emitter, true);
        }
    }

    private void ApplyDebugTuning(TunableEmitter emitter, bool restart)
    {
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

        if (restart)
        {
            emitter.Particles.Restart();
        }
    }

    private void LogDebugTuning() => GD.Print(
        $"MechRewired: VFX tuner [{GetDebugParameterName(m_selectedDebugParameter)}]; " +
        $"fire density {m_fireDensity:F2}, size {m_fireSize:F2}, rise {m_fireRise:F2}, brightness {m_fireBrightness:F2}; " +
        $"smoke density {m_smokeDensity:F2}, size {m_smokeSize:F2}, rise {m_smokeRise:F2}, lifetime {m_smokeLifetime:F2}; " +
        $"dust brightness {m_dustBrightness:F2}, wind {m_dustWind:F2}, lifetime {m_dustLifetime:F2}, spread {m_dustSpread:F1}. " +
        "F5 select; F6/F7 adjust (Shift x5); F10 reset; F11 log.");

    private void ApplyDustMaterialTuning()
    {
        if (m_dustVisualMaterial == null)
        {
            return;
        }

        m_dustVisualMaterial.SetShaderParameter("dust_albedo_multiplier", m_dustBrightness);
        m_dustVisualMaterial.SetShaderParameter("dust_fill", 0.085f * m_dustBrightness);
    }

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
        DebugVfxParameter.DustBrightness => "dust brightness",
        DebugVfxParameter.DustWind => "dust wind",
        DebugVfxParameter.DustLifetime => "dust lifetime",
        DebugVfxParameter.DustSpread => "dust spread",
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
        Godot.Material visualMaterial = particleGeometry switch
        {
            ParticleGeometry.Fire => m_fireVisualMaterial ??= CreateParticleShaderMaterial(false),
            ParticleGeometry.Smoke => m_smokeVisualMaterial ??= CreateParticleShaderMaterial(true),
            ParticleGeometry.Dust => m_dustVisualMaterial ??= CreateParticleShaderMaterial(true, true),
            ParticleGeometry.Spark => m_sparkVisualMaterial ??= new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true
            },
            _ => throw new ArgumentOutOfRangeException(nameof(geometry))
        };
        PrimitiveMesh mesh = particleGeometry switch
        {
            // The linked GodotExplosionVFX atlas is a high-resolution 8x8
            // flipbook.  Use it on camera-facing quads rather than stacking
            // opaque spheres: the sprite carries the fine smoke breakup and
            // the shader supplies the source project's ramp/normal treatment.
            ParticleGeometry.Fire or ParticleGeometry.Smoke or ParticleGeometry.Dust => m_particleQuadMesh ??= new QuadMesh
            {
                Size = new Vector2(1.0f, 1.0f)
            },
            ParticleGeometry.Spark => m_sparkMesh ??= new BoxMesh
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

    private static ShaderMaterial CreateParticleShaderMaterial(bool smoke, bool dust = false)
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
                uniform float dust_albedo_multiplier = 1.0;
                uniform float dust_fill = 0.0;
                uniform float dust_opacity = 1.0;

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

                    ALBEDO = colour * dust_albedo_multiplier;
                    EMISSION = emission * emission_strength + colour * dust_fill;
                    NORMAL = normalize(texture(normal_plus, UV).xyz - texture(normal_minus, UV).xyz);
                    ALPHA = alpha * dust_opacity;
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
                s_vfxTexturesLogged = true;
            }
        }

        var dustBaseColor = TerrainSurfaceMaterial.DesertBaseColor;
        material.SetShaderParameter(
            "base_ramp",
            dust
                ? CreateColorRamp(
                    (0.0f, dustBaseColor.Lerp(Colors.Black, 0.22f)),
                    (0.5f, dustBaseColor),
                    (1.0f, dustBaseColor.Lerp(Colors.White, 0.12f)))
                : smoke
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
            dust
                ? CreateColorRamp(
                    (0.0f, Colors.Black),
                    (1.0f, new Color(0.01f, 0.007f, 0.003f, 1.0f)))
                : smoke
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
        material.SetShaderParameter("emission_strength", dust ? 0.0f : smoke ? 0.04f : 0.55f);
        material.SetShaderParameter("dust_albedo_multiplier", 1.0f);
        material.SetShaderParameter("dust_fill", dust ? 0.085f : 0.0f);
        material.SetShaderParameter("dust_opacity", dust ? 0.38f : 1.0f);
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
            LightVolumetricFogEnergy = 1.35f,
            OmniRange = range,
            ShadowEnabled = false
        };

    private static FogVolume CreateExplosionFog()
    {
        var shader = new Shader
        {
            Code = """
                shader_type fog;

                uniform float smoke_density = 0.14;

                float hash(vec2 p) {
                    return fract(sin(dot(p, vec2(91.7, 263.3))) * 143758.5453);
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
                    vec2 sample_position = WORLD_POSITION.xz * 0.20 + TIME * vec2(0.08, 0.05);
                    float breakup = noise(sample_position) * noise(sample_position * 2.3 + vec2(17.0, 5.0));
                    float lower_smoke = 1.0 - smoothstep(0.45, 1.0, UVW.y);
                    DENSITY = smoke_density * smoothstep(0.08, 0.60, breakup) * lower_smoke;
                    ALBEDO = vec3(0.56, 0.34, 0.16);
                    // A restrained heated-dust fill keeps the volume legible in daylight even
                    // before the short-lived explosion light has reached it.
                    EMISSION = vec3(0.16, 0.065, 0.018) * lower_smoke;
                }
                """
        };
        return new FogVolume
        {
            Name = "ExplosionFog",
            Shape = RenderingServer.FogVolumeShape.Ellipsoid,
            Material = new ShaderMaterial { Shader = shader }
        };
    }

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
        public AmbientEffectState(
            bool isFire,
            Aabb volume,
            string sourceName,
            AudioStreamWav ambientSound,
            Aabb? fireVolume = null,
            Aabb? plumeVolume = null)
        {
            IsFire = isFire;
            Volume = volume;
            SourceName = sourceName;
            AmbientSound = ambientSound;
            FireVolume = fireVolume;
            PlumeVolume = plumeVolume;
        }

        public bool IsFire { get; }

        public Aabb Volume { get; }

        public string SourceName { get; }

        public AudioStreamWav AmbientSound { get; }

        public Aabb? FireVolume { get; }

        public Aabb? PlumeVolume { get; }

        public EffectInstance Instance { get; set; }

        public bool IsActive { get; set; }

        public bool IsCulled { get; set; }

        public string KindName => IsFire ? "fire" : "smoke";
    }

    private enum ParticleGeometry
    {
        Fire,
        Smoke,
        Dust,
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
        SmokeLifetime,
        DustBrightness,
        DustWind,
        DustLifetime,
        DustSpread
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

    private interface IPooledEffect
    {
        bool IsActive { get; }

        void Deactivate();
    }

    private sealed partial class DustEffect : Node3D, IPooledEffect
    {
        private float m_age;

        public DustEffect(GpuParticles3D particles) => Particles = particles;

        public GpuParticles3D Particles { get; }

        public bool IsActive { get; private set; }

        public float Age => m_age;

        public void Activate(Vector3 position)
        {
            Position = position;
            m_age = 0.0f;
            IsActive = true;
            Visible = true;
            ProcessMode = ProcessModeEnum.Inherit;
            Particles.Emitting = true;
            Particles.Restart();
        }

        public void Deactivate()
        {
            IsActive = false;
            Particles.Emitting = false;
            Visible = false;
            ProcessMode = ProcessModeEnum.Disabled;
        }

        public override void _Process(double delta)
        {
            if (!IsActive)
            {
                return;
            }

            m_age += (float)delta;
            if (m_age >= Particles.Lifetime + 0.1f)
            {
                Deactivate();
            }
        }
    }

    private sealed partial class ImpactEffect : Node3D, IPooledEffect
    {
        public ImpactEffect(GpuParticles3D particles, GpuParticles3D sparks, OmniLight3D light)
        {
            Particles = particles;
            Sparks = sparks;
            Light = light;
        }

        private float m_age;

        public GpuParticles3D Particles { get; }

        public GpuParticles3D Sparks { get; }

        public OmniLight3D Light { get; }

        public bool IsActive { get; private set; }

        public float Age => m_age;

        public void Activate(Vector3 position)
        {
            Position = position;
            m_age = 0.0f;
            IsActive = true;
            Visible = true;
            ProcessMode = ProcessModeEnum.Inherit;
            Light.Visible = true;
            Particles.Emitting = true;
            Particles.Restart();
            Sparks.Emitting = true;
            Sparks.Restart();
        }

        public void Deactivate()
        {
            IsActive = false;
            Particles.Emitting = false;
            Sparks.Emitting = false;
            Light.Visible = false;
            Visible = false;
            ProcessMode = ProcessModeEnum.Disabled;
        }

        public override void _Process(double delta)
        {
            if (!IsActive)
            {
                return;
            }

            m_age += (float)delta;
            if (m_age >= 0.9f)
            {
                Deactivate();
            }
        }
    }

    private sealed partial class EffectInstance : Node3D, IPooledEffect
    {
        private readonly bool m_ambient;
        private readonly bool m_pooled;
        private float m_age;

        public EffectInstance(bool ambient, bool pooled = false)
        {
            m_ambient = ambient;
            m_pooled = pooled;
            IsActive = !pooled;
        }

        public GpuParticles3D ExplosionFire { get; set; }

        public GpuParticles3D ExplosionSmoke { get; set; }

        public GpuParticles3D Sparks { get; set; }

        public GpuParticles3D LingeringSmoke { get; set; }

        public FogVolume ExplosionFog { get; set; }

        public float ExplosionFogDensity { get; set; }

        public OmniLight3D ExplosionLight { get; set; }

        public AudioStreamPlayer3D ExplosionAudio { get; set; }

        public bool IsActive { get; private set; }

        public float Age => m_age;

        public void Activate(Vector3 position, Vector3 localHit)
        {
            if (!m_pooled)
            {
                throw new InvalidOperationException("Only pooled destruction effects can be activated.");
            }

            Position = position;
            m_age = 0.0f;
            IsActive = true;
            Visible = true;
            ProcessMode = ProcessModeEnum.Inherit;
            Restart(ExplosionFire, localHit);
            Restart(ExplosionSmoke, localHit);
            Restart(Sparks, localHit);

            LingeringSmoke.Position = Vector3.Zero;
            LingeringSmoke.Emitting = true;
            LingeringSmoke.Restart();
            ExplosionFog.Position = localHit + Vector3.Up * (ExplosionFog.Size.Y * 0.18f);
            ExplosionFog.Visible = true;
            ExplosionLight.Position = localHit;
            ExplosionLight.LightEnergy = 22.0f;
            ExplosionLight.Visible = true;
            if (ExplosionAudio != null)
            {
                ExplosionAudio.Position = localHit;
                ExplosionAudio.Play();
            }
        }

        public void Deactivate()
        {
            if (!m_pooled)
            {
                return;
            }

            IsActive = false;
            ExplosionFire.Emitting = false;
            ExplosionSmoke.Emitting = false;
            Sparks.Emitting = false;
            LingeringSmoke.Emitting = false;
            ExplosionFog.Visible = false;

            ExplosionLight.Visible = false;
            ExplosionAudio?.Stop();
            Visible = false;
            ProcessMode = ProcessModeEnum.Disabled;
        }

        private static void Restart(GpuParticles3D particles, Vector3 position)
        {
            particles.Position = position;
            particles.Emitting = true;
            particles.Restart();
        }

        public override void _Process(double delta)
        {
            if (!IsActive)
            {
                return;
            }

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

            if (ExplosionFog?.Material is ShaderMaterial fogMaterial)
            {
                var fade = Mathf.Clamp(1.0f - m_age / ExplosionFogLifetimeSeconds, 0.0f, 1.0f);
                fogMaterial.SetShaderParameter("smoke_density", fade * ExplosionFogDensity);
                ExplosionFog.Visible = fade > 0.01f;
            }

        }
    }

    private sealed partial class FogTestEffect : Node3D
    {
        private const float LifetimeSeconds = ExplosionFogLifetimeSeconds;
        private readonly FogVolume m_fog;
        private readonly float m_initialDensity;
        private float m_age;

        public FogTestEffect(FogVolume fog, float initialDensity)
        {
            m_fog = fog;
            m_initialDensity = initialDensity;
            m_fog.Position = Vector3.Up * (m_fog.Size.Y * 0.18f);
        }

        public override void _Process(double delta)
        {
            m_age += (float)delta;
            var fade = Mathf.Clamp(1.0f - m_age / LifetimeSeconds, 0.0f, 1.0f);
            if (m_fog.Material is ShaderMaterial material)
            {
                material.SetShaderParameter("smoke_density", fade * m_initialDensity);
            }

            if (fade <= 0.01f)
            {
                QueueFree();
            }
        }
    }
}
