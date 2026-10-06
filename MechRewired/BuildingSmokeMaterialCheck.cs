// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

#if DEBUG
using System.Reflection;
using Godot;

namespace MechRewired;

/// <summary>Checks the live ambient soot material swap without mission archive dependencies.</summary>
public partial class BuildingSmokeMaterialCheck : Node3D
{
    public override async void _Ready()
    {
        BattlefieldEffects effects = null;
        try
        {
            QuestVrRuntime.Initialize(GetViewport());
            effects = new BattlefieldEffects(Array.Empty<AudioStreamWav>());
            var soot = Create(effects, "CreateAmbientSmokeParticles", 10f, 30f, true);
            var vapor = Create(effects, "CreateAmbientSmokeParticles", 10f, 30f, false);
            var explosion = Create(effects, "CreateSmoke", true, 34, 4.5f, 2.5f);
            AddChild(new Camera3D { Position = new Vector3(0, 5, 50), Current = true });
            AddChild(soot);
            AddChild(vapor);
            AddChild(explosion);
            vapor.Position = Vector3.Right * 10;
            explosion.Position = Vector3.Left * 10;
            var original = soot.MaterialOverride;
            var vaporMaterial = vapor.MaterialOverride;
            var explosionMaterial = explosion.MaterialOverride;
            var process = soot.ProcessMaterial;
            var amount = soot.Amount;
            var lifetime = soot.Lifetime;
            var seed = soot.Seed;
            var emitting = soot.Emitting;
            Check(effects.BuildingSmokeEmitterCount == 1, "only ambient soot counted");
            Check(effects.DetailedBuildingSmokeEnabled == !QuestVrRuntime.Active, "platform default");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            effects.DetailedBuildingSmokeEnabled = true;
            Check(effects.DetailedBuildingSmokeEnabled && soot.MaterialOverride == explosionMaterial,
                "detailed uses original shared smoke material");
            var later = Create(effects, "CreateAmbientSmokeParticles", 10f, 30f, true);
            AddChild(later);
            Check(later.MaterialOverride == explosionMaterial, "new emitters inherit current policy");
            effects.DetailedBuildingSmokeEnabled = false;
            Check(soot.MaterialOverride == original && later.MaterialOverride == original, "both live emitters restore default");
            Check(vapor.MaterialOverride == vaporMaterial && explosion.MaterialOverride == explosionMaterial,
                "vapor and transient smoke unchanged");
            Check(soot.ProcessMaterial == process && soot.Amount == amount && soot.Lifetime == lifetime &&
                  soot.Seed == seed && soot.Emitting == emitting, "simulation state retained");
            if (QuestVrRuntime.Active)
            {
                var shader = ((ShaderMaterial)original).Shader.Code;
                Check(shader.Contains("unshaded") && !shader.Contains("normal_plus") &&
                      shader.Split("texture(").Length == 2, "cheap material is unshaded with one texture read");
            }
            later.Free();
            Check(effects.BuildingSmokeEmitterCount == 1, "freed emitter excluded");
            effects.DetailedBuildingSmokeEnabled = true;
            effects.DetailedBuildingSmokeEnabled = false;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("BUILDING_SMOKE_MATERIAL_PASS");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
        finally
        {
            effects?.Free();
        }
    }

    private static GpuParticles3D Create(BattlefieldEffects effects, string method, params object[] arguments) =>
        (GpuParticles3D)typeof(BattlefieldEffects).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(effects, arguments)!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
