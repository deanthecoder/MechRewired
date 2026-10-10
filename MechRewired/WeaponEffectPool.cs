// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>Mission-owned pulse/tracer slots; expiry disables them instead of freeing GPU resources.</summary>
public partial class WeaponEffectPool : Node
{
    private const string PoolName = "WeaponEffectPool";
    private const int InitialCapacity = 64;
    private const int MaximumCapacity = 128;
    private readonly List<LaserEffect> m_lasers = new(MaximumCapacity);
    private readonly List<BallisticTracerEffect> m_tracers = new(MaximumCapacity);
    private bool m_loggedLaserOverflow;
    private bool m_loggedTracerOverflow;

    public static WeaponEffectPool Prewarm(Node parent)
    {
        var existing = parent.GetNodeOrNull<WeaponEffectPool>(PoolName);
        if (existing != null) return existing;
        var pool = new WeaponEffectPool { Name = PoolName };
        parent.AddChild(pool);
        for (var index = 0; index < InitialCapacity; index++)
        {
            pool.AddLaser();
            pool.AddTracer();
        }
        return pool;
    }

    internal static void FireLaser(Node parent, Vector3 start, Vector3 end, Color color, float radius, float delay = 0.0f)
    {
        var pool = parent.GetNodeOrNull<WeaponEffectPool>(PoolName);
        // Compatibility for debug scenes that do not run mission setup.
        if (pool == null)
        {
            QuestCombatTelemetry.RecordWeaponEffectPoolBuild(true);
            QuestCombatTelemetry.RecordWeaponEffectPoolFallback(true);
            parent.AddChild(new LaserEffect(start, end, color, radius, delay));
            return;
        }
        foreach (var laser in pool.m_lasers)
        {
            if (laser.IsActive) continue;
            laser.Launch(start, end, color, radius, delay);
            return;
        }
        if (pool.m_lasers.Count < MaximumCapacity)
        {
            QuestCombatTelemetry.RecordWeaponEffectPoolFallback(true);
            pool.AddLaser().Launch(start, end, color, radius, delay);
            pool.ReportOverflow(true);
            return;
        }
        // An unusually large battle must retain its visuals, even after retained capacity is exhausted.
        QuestCombatTelemetry.RecordWeaponEffectPoolFallback(true);
        QuestCombatTelemetry.RecordWeaponEffectPoolBuild(true);
        parent.AddChild(new LaserEffect(start, end, color, radius, delay));
        pool.ReportOverflow(true);
    }

    internal static void FireTracer(Node parent, Vector3 start, Vector3 end, float delay)
    {
        var pool = parent.GetNodeOrNull<WeaponEffectPool>(PoolName);
        if (pool == null)
        {
            QuestCombatTelemetry.RecordWeaponEffectPoolBuild(false);
            QuestCombatTelemetry.RecordWeaponEffectPoolFallback(false);
            parent.AddChild(new BallisticTracerEffect(start, end, delay));
            return;
        }
        foreach (var tracer in pool.m_tracers)
        {
            if (tracer.IsActive) continue;
            tracer.Launch(start, end, delay);
            return;
        }
        if (pool.m_tracers.Count < MaximumCapacity)
        {
            QuestCombatTelemetry.RecordWeaponEffectPoolFallback(false);
            pool.AddTracer().Launch(start, end, delay);
            pool.ReportOverflow(false);
            return;
        }
        QuestCombatTelemetry.RecordWeaponEffectPoolFallback(false);
        QuestCombatTelemetry.RecordWeaponEffectPoolBuild(false);
        parent.AddChild(new BallisticTracerEffect(start, end, delay));
        pool.ReportOverflow(false);
    }

    private LaserEffect AddLaser()
    {
        QuestCombatTelemetry.RecordWeaponEffectPoolBuild(true);
        var laser = new LaserEffect();
        AddChild(laser);
        m_lasers.Add(laser);
        return laser;
    }

    private BallisticTracerEffect AddTracer()
    {
        QuestCombatTelemetry.RecordWeaponEffectPoolBuild(false);
        var tracer = new BallisticTracerEffect();
        AddChild(tracer);
        m_tracers.Add(tracer);
        return tracer;
    }

    private void ReportOverflow(bool laser)
    {
        if (laser ? m_loggedLaserOverflow : m_loggedTracerOverflow) return;
        if (laser) m_loggedLaserOverflow = true;
        else m_loggedTracerOverflow = true;
        GD.PushWarning($"MechRewired: {(laser ? "laser" : "tracer")} pool exceeded {InitialCapacity} prewarmed slots; allocating fallback. Retained limit {MaximumCapacity}.");
    }
}
