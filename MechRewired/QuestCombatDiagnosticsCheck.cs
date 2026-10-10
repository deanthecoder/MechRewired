// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, compile, or distribute this software,
// for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

#if DEBUG
using Godot;
using MechRewired.Diagnostics;

namespace MechRewired;

/// <summary>Standalone headless checks for combat diagnostic callbacks, telemetry, and saved logs.</summary>
public partial class QuestCombatDiagnosticsCheck : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Callable.From(Run).CallDeferred();
    }

    private void Run()
    {
        try
        {
            CheckCallbackFreezeRestoresStates();
            CheckTelemetryGateAndReset();
            CheckCpuTelemetryScopes();
            CheckArchiveRoundTrip();
            GD.Print("QUEST_COMBAT_DIAGNOSTICS_CHECK_PASS");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError("QUEST_COMBAT_DIAGNOSTICS_CHECK_FAIL: " + exception);
            GetTree().Quit(1);
        }
    }

    private void CheckCallbackFreezeRestoresStates()
    {
        var world = new Node3D { Name = "DiagnosticsFixture" };
        AddChild(world);
        var missile = new MissileEffect(carriesLight: false);
        var laser = new LaserEffect();
        var xrCamera = new XRCamera3D { Name = "UnrelatedXrCamera" };
        missile.AddChild(xrCamera);
        world.AddChild(missile);
        world.AddChild(laser);

        world.SetProcess(false);
        world.SetPhysicsProcess(true);
        missile.SetProcess(true);
        missile.SetPhysicsProcess(false);
        laser.SetProcess(false);
        laser.SetPhysicsProcess(true);
        xrCamera.SetProcess(true);
        xrCamera.SetPhysicsProcess(true);
        Check(missile.IsProcessing() && !missile.IsPhysicsProcessing(), "fixture missile initial callback state");
        Check(!laser.IsProcessing() && laser.IsPhysicsProcessing(), "fixture laser initial callback state");

        var diagnostics = new QuestCombatDiagnostics();
        diagnostics.Apply(world, "simulation-frozen");
        Check(!missile.IsProcessing() && !missile.IsPhysicsProcessing(), "freeze stops missile callbacks");
        Check(!laser.IsProcessing() && !laser.IsPhysicsProcessing(), "freeze stops laser callbacks");
        Check(xrCamera.IsProcessing() && xrCamera.IsPhysicsProcessing(), "freeze leaves XR camera child callbacks live");
        diagnostics.Dispose();

        Check(missile.IsProcessing() && !missile.IsPhysicsProcessing(), "dispose restores missile's original states");
        Check(!laser.IsProcessing() && laser.IsPhysicsProcessing(), "dispose restores laser's original states");
        Check(xrCamera.IsProcessing() && xrCamera.IsPhysicsProcessing(), "dispose leaves XR camera child callbacks live");
        Check(!QuestCombatDiagnostics.EnemyHalfRate, "dispose clears half-rate AI flag");
        world.QueueFree();
    }

    private void CheckTelemetryGateAndReset()
    {
        QuestCombatTelemetry.Active = false;
        QuestCombatTelemetry.Reset();
        for (var index = 0; index < 100; index++) QuestCombatTelemetry.RecordEnemyAi(index);
        var inactive = QuestCombatTelemetry.SnapshotAndReset();
        Check(inactive.EnemyAiTicks == 0 && inactive.EnemyAiElapsedTicks == 0, "inactive telemetry records no samples");

        QuestCombatTelemetry.Active = true;
        QuestCombatTelemetry.RecordEnemyAi(17);
        var sample = QuestCombatTelemetry.SnapshotAndReset();
        var reset = QuestCombatTelemetry.SnapshotAndReset();
        QuestCombatTelemetry.Active = false;
        Check(sample.EnemyAiTicks == 1 && sample.EnemyAiElapsedTicks == 17, "active telemetry records counters and elapsed ticks");
        Check(reset.EnemyAiTicks == 0 && reset.EnemyAiElapsedTicks == 0, "snapshot resets interval counters");

        for (var index = 0; index < 100; index++) QuestCombatTelemetry.RecordEnemyAi(index);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10000; index++) QuestCombatTelemetry.RecordEnemyAi(index);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"inactive telemetry callback remains allocation-free, allocated {allocated} bytes");
    }

    private static void CheckCpuTelemetryScopes()
    {
        const QuestCpuTelemetry.Category category = QuestCpuTelemetry.Category.PlayerPhysics;
        QuestCombatTelemetry.Active = false;
        QuestCpuTelemetry.Reset();
        using (QuestCpuTelemetry.Measure(category))
        {
            var inactiveAllocation = new byte[1024];
            GC.KeepAlive(inactiveAllocation);
        }
        var inactive = QuestCpuTelemetry.SnapshotAndReset().PlayerPhysics;
        Check(inactive.Calls == 0 && inactive.AllocatedBytes == 0, "inactive CPU scopes record no calls or allocation");

        QuestCombatTelemetry.Active = true;
        using (QuestCpuTelemetry.Measure(category))
        {
            var deliberateAllocation = new byte[1024];
            GC.KeepAlive(deliberateAllocation);
        }
        var measured = QuestCpuTelemetry.SnapshotAndReset().PlayerPhysics;
        Check(measured.Calls == 1 && measured.AllocatedBytes >= 1024,
            $"active CPU scope attributes body allocation, calls={measured.Calls} bytes={measured.AllocatedBytes}");
        var afterReset = QuestCpuTelemetry.SnapshotAndReset().PlayerPhysics;
        Check(afterReset.Calls == 0 && afterReset.AllocatedBytes == 0, "CPU snapshot resets scope counters");

        QuestCombatTelemetry.Active = true;
        RunCpuScopeLoop(category, 1000); // Warm JIT and scope paths before allocation checks.
        QuestCpuTelemetry.Reset();
        var activeBefore = GC.GetAllocatedBytesForCurrentThread();
        RunCpuScopeLoop(category, 10000);
        var activeBytes = GC.GetAllocatedBytesForCurrentThread() - activeBefore;
        Check(activeBytes == 0, $"active CPU scopes allocate no recorder memory, allocated {activeBytes} bytes");
        var activeLoops = QuestCpuTelemetry.SnapshotAndReset().PlayerPhysics;
        Check(activeLoops.Calls == 10000, $"active warmed scopes count every call, got {activeLoops.Calls} (active={QuestCombatTelemetry.Active})");

        QuestCombatTelemetry.Active = false;
        RunCpuScopeLoop(category, 10000);
        var inactiveBefore = GC.GetAllocatedBytesForCurrentThread();
        RunCpuScopeLoop(category, 10000);
        var inactiveBytes = GC.GetAllocatedBytesForCurrentThread() - inactiveBefore;
        var inactiveLoops = QuestCpuTelemetry.SnapshotAndReset().PlayerPhysics;
        QuestCombatTelemetry.Active = false;
        Check(inactiveBytes == 0 && inactiveLoops.Calls == 0,
            $"inactive warmed scopes allocate nothing and record no calls, bytes={inactiveBytes}, calls={inactiveLoops.Calls}");
    }

    private static void RunCpuScopeLoop(QuestCpuTelemetry.Category category, int iterations)
    {
        for (var index = 0; index < iterations; index++)
        {
            using var scope = QuestCpuTelemetry.Measure(category);
        }
    }

    private static void CheckArchiveRoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mechrewired-godot-archive-check", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "run-check.log");
        try
        {
            using (var archive = new DurableTaggedLogStore(directory))
            {
                archive.BeginRun("run-check");
                archive.AppendLine("QUEST_COMBAT_RUN: {\"runId\":\"run-check\"}");
                archive.AppendLine("QUEST_COMBAT_STATUS: {\"runId\":\"run-check\",\"status\":\"complete\"}");
                archive.Flush();
                Check(archive.ActivePath == path, "archive path is stable and run-specific");
            }
            var records = File.ReadAllLines(path);
            Check(records.Length == 2 && records[0].StartsWith("QUEST_COMBAT_RUN:", StringComparison.Ordinal) &&
                  records[1].Contains("\"status\":\"complete\"", StringComparison.Ordinal),
                "flushed benchmark records survive archive close and reopen");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
    }
}
#endif
