// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using System.Diagnostics;

namespace MechRewired;

/// <summary>Benchmark-only main-thread callback measurements with no recording allocations.</summary>
/// <remarks>
/// Times and allocated bytes are inclusive: nested categories can overlap and must not be summed.
/// SnapshotAndReset must run on the main thread between callbacks. Scope acquisition and disposal
/// use value types; conversion and serialization belong outside the measured callbacks.
/// </remarks>
public static class QuestCpuTelemetry
{
    public enum Category { PlayerPhysics, PlayerTargeting, HudProcess, HudDraw, EnemyPhysics, Missiles, TerrainRocks, VrProcess, HudCoverage, PlayerGroundClearance, PlayerSurface, PlayerSurfaceQuery, PlayerGait, PlayerJumpJets }

    public readonly record struct Measurement(int Calls, double ElapsedMs, long AllocatedBytes);
    public readonly record struct Snapshot(
        Measurement PlayerPhysics,
        Measurement PlayerTargeting,
        Measurement HudProcess,
        Measurement HudDraw,
        Measurement EnemyPhysics,
        Measurement Missiles,
        Measurement TerrainRocks,
        Measurement VrProcess,
        Measurement HudCoverage,
        Measurement PlayerGroundClearance,
        Measurement PlayerSurface,
        Measurement PlayerSurfaceQuery,
        Measurement PlayerGait,
        Measurement PlayerJumpJets);

    private struct Counter
    {
        public int Calls;
        public long Ticks;
        public long Bytes;
        public Measurement Read() => new(Calls, Ticks * 1000.0 / Stopwatch.Frequency, Bytes);
    }

    private static Counter s_PlayerPhysics;
    private static Counter s_PlayerTargeting;
    private static Counter s_HudProcess;
    private static Counter s_HudDraw;
    private static Counter s_EnemyPhysics;
    private static Counter s_Missiles;
    private static Counter s_TerrainRocks;
    private static Counter s_VrProcess;
    private static Counter s_HudCoverage;

    private static Counter s_PlayerGroundClearance;
    private static Counter s_PlayerSurface;
    private static Counter s_PlayerSurfaceQuery;
    private static Counter s_PlayerGait;
    private static Counter s_PlayerJumpJets;

    public static Scope Measure(Category category) => new(category, QuestCombatTelemetry.Active);

    public readonly struct Scope : IDisposable
    {
        private readonly Category m_category;
        private readonly bool m_active;
        private readonly long m_started;
        private readonly long m_allocated;

        internal Scope(Category category, bool active)
        {
            m_category = category;
            m_active = active;
            m_allocated = active ? GC.GetAllocatedBytesForCurrentThread() : 0;
            m_started = active ? Stopwatch.GetTimestamp() : 0;
        }

        public void Dispose()
        {
            if (!m_active) return;
            var ticks = Stopwatch.GetTimestamp() - m_started;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - m_allocated;
            ref var counter = ref GetCounter(m_category);
            counter.Calls++;
            counter.Ticks += ticks;
            counter.Bytes += bytes;
        }
    }

    private static ref Counter GetCounter(Category category)
    {
        switch (category)
        {
            case Category.PlayerPhysics: return ref s_PlayerPhysics;
            case Category.PlayerTargeting: return ref s_PlayerTargeting;
            case Category.HudProcess: return ref s_HudProcess;
            case Category.HudDraw: return ref s_HudDraw;
            case Category.EnemyPhysics: return ref s_EnemyPhysics;
            case Category.Missiles: return ref s_Missiles;
            case Category.TerrainRocks: return ref s_TerrainRocks;
            case Category.VrProcess: return ref s_VrProcess;
            case Category.HudCoverage: return ref s_HudCoverage;
            case Category.PlayerGroundClearance: return ref s_PlayerGroundClearance;
            case Category.PlayerSurface: return ref s_PlayerSurface;
            case Category.PlayerSurfaceQuery: return ref s_PlayerSurfaceQuery;
            case Category.PlayerGait: return ref s_PlayerGait;
            case Category.PlayerJumpJets: return ref s_PlayerJumpJets;
            default: throw new ArgumentOutOfRangeException(nameof(category));
        }
    }

    public static Snapshot SnapshotAndReset()
    {
        var snapshot = new Snapshot(s_PlayerPhysics.Read(), s_PlayerTargeting.Read(), s_HudProcess.Read(), s_HudDraw.Read(), s_EnemyPhysics.Read(), s_Missiles.Read(), s_TerrainRocks.Read(), s_VrProcess.Read(), s_HudCoverage.Read(), s_PlayerGroundClearance.Read(), s_PlayerSurface.Read(), s_PlayerSurfaceQuery.Read(), s_PlayerGait.Read(), s_PlayerJumpJets.Read());
        Reset();
        return snapshot;
    }

    public static void Reset()
    {
        s_PlayerPhysics = default;
        s_PlayerTargeting = default;
        s_HudProcess = default;
        s_HudDraw = default;
        s_EnemyPhysics = default;
        s_Missiles = default;
        s_TerrainRocks = default;
        s_VrProcess = default;
        s_HudCoverage = default;
        s_PlayerGroundClearance = default;
        s_PlayerSurface = default;
        s_PlayerSurfaceQuery = default;
        s_PlayerGait = default;
        s_PlayerJumpJets = default;

    }
}
