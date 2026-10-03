// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

#if DEBUG
using Godot;

namespace MechRewired;

/// <summary>Exercises native node reuse without mission/game-data dependencies.</summary>
public partial class WeaponEffectPoolSmokeCheck : Node
{
    public override async void _Ready()
    {
        try
        {
            var pool = WeaponEffectPool.Prewarm(this);
            Check(ReferenceEquals(pool, WeaponEffectPool.Prewarm(this)), "mission prewarm is idempotent");
            var nodes = pool.GetChildren();
            var laser = nodes.OfType<LaserEffect>().First();
            var tracer = nodes.OfType<BallisticTracerEffect>().First();
            var pulse = laser.GetChildren().OfType<MeshInstance3D>().Single();
            var light = laser.GetChildren().OfType<OmniLight3D>().Single();
            var originalMesh = pulse.Mesh;
            var originalMaterial = pulse.MaterialOverride;
            Check(nodes.Count == 128 && nodes.OfType<LaserEffect>().All(effect => !effect.IsActive && !effect.IsProcessing()),
                "64 slots of each type prewarmed and idle");
            QuestCombatTelemetry.Reset();
            QuestCombatTelemetry.Active = true;
            QuestCombatTelemetry.WeaponLightsDisabled = false;
            WeaponEffectPool.FireLaser(this, Vector3.Zero, Vector3.Right * 100, Colors.Green, 0.055f, 0.1f);
            WeaponEffectPool.FireTracer(this, Vector3.Zero, Vector3.Right * 100, 0.1f);
            Check(laser.IsActive && tracer.IsActive && !pulse.Visible && !light.Visible, "delayed effects start hidden");
            WeaponEffectPool.FireLaser(this, Vector3.Zero, Vector3.Up * 50, Colors.Blue, 0.1f, 0.4f);
            var secondLaser = nodes.OfType<LaserEffect>().Skip(1).First();
            var secondPulse = secondLaser.GetChildren().OfType<MeshInstance3D>().Single();
            Check(ReferenceEquals(pulse.Mesh, secondPulse.Mesh) && !ReferenceEquals(pulse.MaterialOverride, secondPulse.MaterialOverride),
                "overlapping slots share geometry and retain independent colors");
            laser._Process(0.11);
            tracer._Process(0.11);
            Check(pulse.Visible && light.Visible && light.LightColor == Colors.Green, "laser keeps traveling colored light");
            Check(Mathf.IsEqualApprox(pulse.Scale.X, 0.055f), "shared unit mesh preserves radius");
            laser._Process(10);
            tracer._Process(10);
            secondLaser._Process(10);
            Check(!laser.IsActive && !tracer.IsActive && !laser.IsProcessing() && !light.Visible, "expired slots disable nodes and light");
            WeaponEffectPool.FireLaser(this, Vector3.One, Vector3.One + Vector3.Forward * 20, Colors.Red, 0.09f);
            WeaponEffectPool.FireTracer(this, Vector3.One, Vector3.One + Vector3.Forward * 20, 0.0f);
            Check(laser.IsActive && tracer.IsActive && pool.GetChildCount() == 128, "same slots reused with no new children");
            Check(ReferenceEquals(originalMesh, pulse.Mesh) && ReferenceEquals(originalMaterial, pulse.MaterialOverride), "GPU resources retained across shots");
            Check(light.LightColor == Colors.Red && Mathf.IsEqualApprox(pulse.Scale.X, 0.09f), "reuse resets color and radius");
            laser._Process(10);
            tracer._Process(10);
            var oneShotLaser = new LaserEffect(Vector3.Zero, Vector3.Right);
            var oneShotTracer = new BallisticTracerEffect(Vector3.Zero, Vector3.Right, 0);
            AddChild(oneShotLaser);
            AddChild(oneShotTracer);
            oneShotLaser._Process(10);
            oneShotTracer._Process(10);
            Check(oneShotLaser.IsQueuedForDeletion() && oneShotTracer.IsQueuedForDeletion(), "nonpooled compatibility effects still free");
            Check(QuestCombatTelemetry.SnapshotAndReset().ActiveLasers == 0, "pooled expiry and one-shot expiry balance telemetry");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("WEAPON_EFFECT_POOL_SMOKE_PASS");
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
            QuestCombatTelemetry.Reset();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
