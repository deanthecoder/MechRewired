// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Diagnostics;
using System.Threading;

namespace MechRewired;

/// <summary>
/// Opt-in, allocation-free combat counters for Quest performance experiments.
/// </summary>
/// <remarks>
/// The benchmark runner owns <see cref="Active"/> and the ablation flags. Gameplay never reads a
/// snapshot, and all recording calls return immediately while telemetry is inactive.
/// </remarks>
public static class QuestCombatTelemetry
{
    private static long s_enemyAiTicks;
    private static long s_enemyAiElapsedTicks;
    private static long s_lineOfSightCalls;
    private static long s_lineOfSightElapsedTicks;
    private static long s_missilePoolsCreated;
    private static long s_missilePoolCreationElapsedTicks;
    private static long s_weaponLaunches;
    private static long s_enemyWeaponLaunches;
    private static long s_missileLaunches;
    private static long s_impacts;
    private static int s_activeMissiles;
    private static int s_activeLasers;

    public static bool Active { get; set; }

    public static bool WeaponLightsDisabled { get; set; }

    public static bool SmokeDisabled { get; set; }

    /// <summary>Returns this sampling interval and clears its interval counters.</summary>
    public static QuestCombatTelemetrySnapshot SnapshotAndReset() => new(
        Interlocked.Exchange(ref s_enemyAiTicks, 0),
        Interlocked.Exchange(ref s_enemyAiElapsedTicks, 0),
        Interlocked.Exchange(ref s_lineOfSightCalls, 0),
        Interlocked.Exchange(ref s_lineOfSightElapsedTicks, 0),
        Interlocked.Exchange(ref s_missilePoolsCreated, 0),
        Interlocked.Exchange(ref s_missilePoolCreationElapsedTicks, 0),
        Interlocked.Exchange(ref s_weaponLaunches, 0),
        Interlocked.Exchange(ref s_enemyWeaponLaunches, 0),
        Interlocked.Exchange(ref s_missileLaunches, 0),
        Interlocked.Exchange(ref s_impacts, 0),
        Volatile.Read(ref s_activeMissiles),
        Volatile.Read(ref s_activeLasers));

    /// <summary>Alias used by frame-based benchmark runners.</summary>
    public static QuestCombatTelemetrySnapshot TakeFrameSnapshot() => SnapshotAndReset();

    public static void Reset() => SnapshotAndReset();

    internal static void RecordEnemyAi(long elapsedTicks)
    {
        if (!Active) return;
        Interlocked.Increment(ref s_enemyAiTicks);
        Interlocked.Add(ref s_enemyAiElapsedTicks, elapsedTicks);
    }

    internal static void RecordLineOfSight(long elapsedTicks)
    {
        if (!Active) return;
        Interlocked.Increment(ref s_lineOfSightCalls);
        Interlocked.Add(ref s_lineOfSightElapsedTicks, elapsedTicks);
    }

    internal static void RecordMissilePoolCreation(long elapsedTicks)
    {
        if (!Active) return;
        Interlocked.Increment(ref s_missilePoolsCreated);
        Interlocked.Add(ref s_missilePoolCreationElapsedTicks, elapsedTicks);
    }

    internal static void RecordEnemyWeaponLaunch()
    {
        if (!Active) return;
        Interlocked.Increment(ref s_enemyWeaponLaunches);
        Interlocked.Increment(ref s_weaponLaunches);
    }

    internal static void RecordWeaponLaunch()
    {
        if (Active) Interlocked.Increment(ref s_weaponLaunches);
    }

    internal static void RecordMissileLaunch()
    {
        if (Active) Interlocked.Increment(ref s_missileLaunches);
    }

    internal static void RecordImpact()
    {
        if (Active) Interlocked.Increment(ref s_impacts);
    }

    internal static bool TrackMissileLaunched()
    {
        if (!Active) return false;
        Interlocked.Increment(ref s_activeMissiles);
        return true;
    }

    internal static void TrackMissileStopped()
    {
        Interlocked.Decrement(ref s_activeMissiles);
    }

    internal static bool TrackLaserCreated()
    {
        if (!Active) return false;
        Interlocked.Increment(ref s_activeLasers);
        return true;
    }

    internal static void TrackLaserStopped()
    {
        Interlocked.Decrement(ref s_activeLasers);
    }
}

/// <summary>Combat counters accumulated since the previous telemetry snapshot.</summary>
public readonly record struct QuestCombatTelemetrySnapshot(
    long EnemyAiTicks,
    long EnemyAiElapsedTicks,
    long LineOfSightCalls,
    long LineOfSightElapsedTicks,
    long MissilePoolsCreated,
    long MissilePoolCreationElapsedTicks,
    long WeaponLaunches,
    long EnemyWeaponLaunches,
    long MissileLaunches,
    long Impacts,
    int ActiveMissiles,
    int ActiveLasers)
{
    public double EnemyAiMilliseconds => EnemyAiElapsedTicks * 1000.0 / Stopwatch.Frequency;

    public double EnemyAiElapsedMs => EnemyAiMilliseconds;

    public double LineOfSightMilliseconds => LineOfSightElapsedTicks * 1000.0 / Stopwatch.Frequency;

    public double LineOfSightElapsedMs => LineOfSightMilliseconds;

    public double MissilePoolCreationMilliseconds => MissilePoolCreationElapsedTicks * 1000.0 / Stopwatch.Frequency;

    public double MissilePoolCreationElapsedMs => MissilePoolCreationMilliseconds;
}
