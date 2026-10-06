// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

#if DEBUG
using Godot;

namespace MechRewired;

/// <summary>Checks shared guidance, independent impacts, and native query allocation savings.</summary>
public partial class MissileOptimizationCheck : Node3D
{
    public override async void _Ready()
    {
        try
        {
            QuestVrRuntime.Initialize(GetViewport());
            CheckGuidance();
            var wall = new StaticBody3D { CollisionLayer = BattlefieldPhysics.TerrainLayer, CollisionMask = 0,
                Position = new Vector3(0, 0, -5) };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20, 20, 1) } });
            AddChild(wall);
            await PhysicsFrames(3);
            QuestCombatTelemetry.SmokeDisabled = true;
            QuestCombatTelemetry.Active = true;
            QuestCombatTelemetry.Reset();
            Vector3? firstHit = null;
            Vector3? secondHit = null;
            var first = new MissileEffect(false);
            var second = new MissileEffect(false);
            AddChild(first);
            AddChild(second);
            first.Launch(Vector3.Zero, Vector3.Forward, 100, null, null, terrainImpact: point => firstHit = point);
            second.Launch(Vector3.Right * 3, Vector3.Forward, 100, null, null, terrainImpact: point => secondHit = point);
            first.SetProcess(false);
            second.SetProcess(false);
            first._Process(0.1);
            second._Process(0.1);
            Check(firstHit.HasValue && secondHit.HasValue && Mathf.IsEqualApprox(firstHit.Value.X, 0) &&
                Mathf.IsEqualApprox(secondHit.Value.X, 3), "each missile keeps its own terrain impact");
            Check(!first.IsFlying && !second.IsFlying, "swept queries hit terrain without tunneling");

            first.Launch(Vector3.Right * 30, Vector3.Forward, 100, null, null);
            first.SetProcess(false);
            first._Process(0.1);
            Check(first.IsFlying && first.GlobalPosition.IsEqualApprox(new Vector3(30, 0, -10)),
                "pooled relaunch replaces query endpoints and old hit state");
            Check(QuestCombatTelemetry.SnapshotAndReset().MissileTerrainQueryCalls == 3,
                "each swept segment retains a collision query");

            // Moving the pooled node to another physics world must invalidate the borrowed space state.
            var viewport = new SubViewport { OwnWorld3D = true, Size = new Vector2I(32, 32) };
            AddChild(viewport);
            var otherWorld = new Node3D();
            viewport.AddChild(otherWorld);
            RemoveChild(first);
            otherWorld.AddChild(first);
            await PhysicsFrames(3);
            first.Launch(Vector3.Zero, Vector3.Forward, 100, null, null);
            first.SetProcess(false);
            first._Process(0.1);
            Check(first.IsFlying && first.GlobalPosition.IsEqualApprox(Vector3.Forward * 10),
                "physics world transition does not retain the old terrain");
            var targetSamples = 0;
            Vector3? target = new Vector3(30, 0, -100);
            var shared = new MissileSalvoGuidance(() => { targetSamples++; return target; });
            first.Launch(Vector3.Right * 30, Vector3.Forward, 100, null, null, guidance: shared);
            second.Launch(Vector3.Right * 40, Vector3.Forward, 100, null, null, guidance: shared);
            first.SetProcess(false);
            second.SetProcess(false);
            first._Process(0.01);
            second._Process(0.01);
            Check(targetSamples == 1 && !first.Direction.IsEqualApprox(second.Direction),
                "one target sample drives independent missile steering");
            // Reusing the first slot must not reset or own the other follower's guidance.
            first.Launch(Vector3.Right * 30, Vector3.Forward, 0.5f, null, null);
            first.SetProcess(false);
            first._Process(0.01);
            first._Process(0.1);
            Check(first.GlobalPosition.Y < 0, "range exhaustion retains ballistic gravity");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            target = new Vector3(35, 0, -100);
            second._Process(0.01);
            Check(targetSamples == 2 && second.IsFlying,
                "follower guidance survives the first pool slot being reused");
            QuestCombatTelemetry.SmokeDisabled = false;
            QuestCombatTelemetry.WeaponLightsDisabled = false;
            var cadence = new MechRewired.Simulation.MissileVisualCadence();
            var smoke = first.GetChildren().OfType<GpuParticles3D>().Single();
            var light = first.GetChildren().OfType<OmniLight3D>().Single();
            QuestCombatTelemetry.ReducedMissileSmoke = true;
            QuestCombatTelemetry.SmallProjectileLights = true;
            first.Launch(Vector3.Zero, Vector3.Forward, 100, null, null, carriesSmoke: true, carriesLight: true);
            first.SetProcess(false);
            var scaledSmoke = (ParticleProcessMaterial)smoke.ProcessMaterial;
            var expectedScale = QuestVrRuntime.Active ? 0.75f : 1.0f;
            Check(Mathf.IsEqualApprox(scaledSmoke.ScaleMin, 0.82f * expectedScale) &&
                  Mathf.IsEqualApprox(scaledSmoke.ScaleMax, 1.28f * expectedScale), "Quest smoke size candidate; desktop unchanged");
            Check(Mathf.IsEqualApprox(light.OmniRange, QuestVrRuntime.Active ? 3 : 6) && light.Visible,
                "smaller projectile light retains illumination");
            first.Launch(Vector3.Zero, Vector3.Forward, 100, null, null, carriesSmoke: false, carriesLight: true);
            Check(!smoke.Visible && !smoke.Emitting && light.Visible, "trail suppression retains projectile light");
            QuestCombatTelemetry.ReducedMissileSmoke = false;
            QuestCombatTelemetry.SmallProjectileLights = false;
            first.Launch(Vector3.Zero, Vector3.Forward, 100, null, null, carriesSmoke: true, carriesLight: true);
            Check(Mathf.IsEqualApprox(((ParticleProcessMaterial)smoke.ProcessMaterial).ScaleMax, 1.28f) &&
                  Mathf.IsEqualApprox(light.OmniRange, 6) && smoke.Emitting, "pooled slot restores baseline size and light radius");
            var smokeLaunches = 0;
            var lightLaunches = 0;
            for (var launch = 0; launch < 24; launch++)
            {
                var visuals = cadence.Next();
                first.Launch(Vector3.Zero, Vector3.Forward, 100, () => Vector3.Forward * 10, null,
                    carriesSmoke: visuals.Smoke, carriesLight: visuals.Light);
                first.SetProcess(false);
                Check(smoke.Visible == visuals.Smoke && smoke.Emitting == visuals.Smoke && light.Visible == visuals.Light,
                    "pooled relaunch applies smoke/light eligibility independently of pool slot");
                if (smoke.Emitting) smokeLaunches++;
                if (light.Visible) lightLaunches++;
                first._Process(0.1);
                Check(!first.IsFlying && first.IsActive == visuals.Smoke,
                    "only smoke carriers retain a fade tail after impact");
                first._Process(2);
                Check(!first.IsActive && !first.IsProcessing() && !light.Visible,
                    "expiry disables sparse visual slots and balances telemetry");
            }
            Check(smokeLaunches == 8 && lightLaunches == 3, "24 launches produce eight smoke trails and three lights");
            first.Launch(Vector3.Zero, Vector3.Forward, 100, null, null, carriesSmoke: true, carriesLight: true);
            first.SetProcess(false);
            QuestCombatTelemetry.SmokeDisabled = true;
            QuestCombatTelemetry.WeaponLightsDisabled = true;
            first._Process(0.01);
            Check(!smoke.Visible && !smoke.Emitting && !light.Visible, "benchmark switches override sparse eligibility");
            QuestCombatTelemetry.SmokeDisabled = false;
            QuestCombatTelemetry.WeaponLightsDisabled = false;
            first._Process(0.01);
            Check(smoke.Visible && smoke.Emitting && light.Visible, "benchmark restore retains launch eligibility");
            first.Launch(Vector3.Zero, Vector3.Forward, 100, null, null);
            first.SetProcess(false);
            Check(smoke.Visible && smoke.Emitting && !light.Visible,
                "legacy launches retain full smoke and constructor light defaults");
            GD.Print("MISSILE_VISUAL_CADENCE_PASS: launches=24 smoke=8 lights=3");
            CompareQueryAllocations();
            GD.Print("MISSILE_OPTIMIZATION_CHECK_PASS");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            QuestCombatTelemetry.Active = false;
            QuestCombatTelemetry.SmokeDisabled = false;
            QuestCombatTelemetry.WeaponLightsDisabled = false;
            QuestCombatTelemetry.Reset();
            QuestCombatTelemetry.ReducedMissileSmoke = false;
            QuestCombatTelemetry.SmallProjectileLights = false;
        }
    }

    private static void CheckGuidance()
    {
        var samples = 0;
        Vector3? target = new Vector3(0, 0, -100);
        var guidance = new MissileSalvoGuidance(() => { samples++; return target; });
        for (var missile = 0; missile < 20; missile++)
            Check(guidance.Sample(0, 0.1f) == target, "salvo shares current target sample");
        Check(samples == 1 && guidance.TargetVelocity.IsZeroApprox(), "twenty followers sample once, with no initial velocity spike");
        target = new Vector3(10, 0, -100);
        guidance.Sample(1, 0.1f);
        Check(samples == 2 && guidance.TargetVelocity.IsEqualApprox(Vector3.Right * 80),
            "new frame updates filtered velocity once");
        for (var missile = 0; missile < 20; missile++) guidance.Sample(1, 0.1f);
        Check(samples == 2 && guidance.TargetVelocity.IsEqualApprox(Vector3.Right * 80),
            "followers do not repeatedly advance the velocity filter");
        target = new Vector3(1000, 0, -100);
        guidance.Sample(4, 0.1f);
        Check(guidance.TargetVelocity.IsZeroApprox(), "sampling gaps do not exaggerate target velocity");
        target = null;
        Check(guidance.Sample(5, 0.1f) == null && guidance.Sample(5, 0.1f) == null && samples == 4,
            "destroyed target result is shared without retaining a live target");
        var alive = true;
        target = Vector3.Forward * 100;
        var liveGuidance = new MissileSalvoGuidance(() => target, () => alive);
        Check(liveGuidance.Sample(0, 0.1f).HasValue, "live target initially available");
        alive = false;
        Check(liveGuidance.Sample(0, 0.1f) == null, "target death invalidates followers in the same frame");
    }

    private void CompareQueryAllocations()
    {
        var space = GetWorld3D().DirectSpaceState;
        var start = Vector3.Right * 30;
        var end = start + Vector3.Forward;
        using var reused = PhysicsRayQueryParameters3D.Create(start, end, BattlefieldPhysics.TerrainLayer);
        reused.HitBackFaces = true;
        for (var i = 0; i < 100; i++)
        {
            using var query = PhysicsRayQueryParameters3D.Create(start, end, BattlefieldPhysics.TerrainLayer);
            query.HitBackFaces = true;
            using var freshWarm = space.IntersectRay(query);
            using var reusedWarm = space.IntersectRay(reused);
        }
        const int count = 1000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
        {
            using var query = PhysicsRayQueryParameters3D.Create(start, end, BattlefieldPhysics.TerrainLayer);
            query.HitBackFaces = true;
            using var result = space.IntersectRay(query);
        }
        var freshBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
        {
            reused.From = start;
            reused.To = end;
            using var result = space.IntersectRay(reused);
        }
        var reusedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(reusedBytes < freshBytes, "reused query reduces measured managed allocations");
        GD.Print($"MISSILE_QUERY_ALLOCATIONS: queries={count} freshBytes={freshBytes} reusedBytes={reusedBytes} savedBytes={freshBytes - reusedBytes}");
    }

    private async System.Threading.Tasks.Task PhysicsFrames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
