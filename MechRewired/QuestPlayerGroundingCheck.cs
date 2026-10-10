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
using System.Reflection;
using Godot;
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>Native regression check for Quest player terrain sampling and hidden-leg gait optimization.</summary>
/// <remarks>Run with original game data using the Mobile renderer, -- --vr-preview after the scene.</remarks>
public partial class QuestPlayerGroundingCheck : Node
{
    private static readonly MethodInfo s_tryGetSurface = typeof(PlayerMech).GetMethod("TryGetSurface", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo s_applyGaitGroundClearance = typeof(PlayerMech).GetMethod("ApplyGaitGroundClearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo s_surfaceCacheValid = typeof(PlayerMech).GetField("m_hasQuestSurface", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo s_gaitGroundElevation = typeof(PlayerMech).GetField("m_gaitGroundElevation", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo s_jumpJetsRequested = typeof(QuestVrRig).GetProperty("JumpJetsRequested", BindingFlags.Instance | BindingFlags.Public)!;

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private async void Run()
    {
        Main main = null;
        try
        {
            if (!QuestVrRuntime.Preview)
                throw new InvalidOperationException("Run with -- --vr-preview after the scene.");
            main = GD.Load<PackedScene>("res://Main.tscn").Instantiate<Main>();
            AddChild(main);
            // Let the mission build the real player, original terrain index and VR rig.
            for (var frame = 0; frame < 60; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Paused = true;
            var player = main.FindChildren("PlayerMech", "", true, false).OfType<PlayerMech>().SingleOrDefault()
                ?? throw new InvalidOperationException("Player was not created. Check original game data.");
            Check(player.IsVr, "VR preview builds the seated rig needed to activate Quest grounding");
            Check(Field<TerrainSurfaceIndex>(player, "m_terrainSurface") != null, "player received indexed terrain");

            CheckSurfaceCachingAndParity(player);
            CheckJumpJetInvalidation(player);
            CheckGroundClearance(player);
            CheckGaitPhaseAndFootfall();
            GD.Print("QUEST_PLAYER_GROUNDING_CHECK_PASS");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            QuestCombatTelemetry.Active = false;
            QuestCombatDiagnostics.LegacyPlayerGrounding = false;
            GD.PushError("QUEST_PLAYER_GROUNDING_CHECK_FAIL: " + exception);
            GetTree().Quit(1);
        }
    }

    private static void CheckSurfaceCachingAndParity(PlayerMech player)
    {
        var position = player.GlobalPosition;
        s_surfaceCacheValid.SetValue(player, false);
        QuestCombatDiagnostics.LegacyPlayerGrounding = false;
        QuestCombatTelemetry.Active = true;
        QuestCpuTelemetry.Reset();

        var first = Surface(player, position);
        Check(first.Success, "indexed surface succeeds at deployment position");
        var firstCounts = QuestCpuTelemetry.SnapshotAndReset();
        Check(firstCounts.PlayerSurfaceQuery.Calls == 1, "initial Quest grounding sample performs one terrain query");

        var repeated = Surface(player, new Vector3(position.X, position.Y + 75.0f, position.Z));
        Check(repeated.Success, "same X/Z uses cached surface even when input Y differs");
        var cachedCounts = QuestCpuTelemetry.SnapshotAndReset();
        Check(cachedCounts.PlayerSurfaceQuery.Calls == 0, "repeated exact X/Z uses the cached sample");
        Near(first.Height, repeated.Height, 0.0001f, "cached height");
        Near(first.Slope, repeated.Slope, 0.0001f, "cached slope");

        var moved = new Vector3(position.X + 0.01f, position.Y, position.Z + 0.01f);
        var movedQuest = Surface(player, moved);
        Check(movedQuest.Success, "small movement gets a fresh indexed sample");
        Check(QuestCpuTelemetry.SnapshotAndReset().PlayerSurfaceQuery.Calls == 1,
            "any exact X/Z movement invalidates the sample");
        QuestCombatDiagnostics.LegacyPlayerGrounding = true;
        var movedLegacy = Surface(player, moved);
        Check(movedLegacy.Success, "legacy terrain query succeeds at the same point");
        Check(QuestCpuTelemetry.SnapshotAndReset().PlayerSurfaceQuery.Calls == 1,
            "legacy mode always reaches the surface index");
        Near(movedQuest.Height, movedLegacy.Height, 0.0001f, "Quest/legacy surface height parity");
        Near(movedQuest.Slope, movedLegacy.Slope, 0.001f, "Quest/legacy surface slope parity");

        QuestCombatDiagnostics.LegacyPlayerGrounding = false;
        var outside = new Vector3(10_000_000.0f, position.Y, -10_000_000.0f);
        Check(!Surface(player, outside).Success && !Surface(player, outside).Success,
            "out-of-map samples remain failures");
        Check(QuestCpuTelemetry.SnapshotAndReset().PlayerSurfaceQuery.Calls == 2,
            "failed terrain queries are not cached");
        QuestCombatTelemetry.Active = false;
    }

    private static void CheckJumpJetInvalidation(PlayerMech player)
    {
        var position = player.GlobalPosition;
        s_surfaceCacheValid.SetValue(player, false);
        QuestCombatTelemetry.Active = true;
        QuestCpuTelemetry.Reset();
        Check(Surface(player, position).Success, "surface cache seeded before jump-jet invalidation");
        Check(QuestCpuTelemetry.SnapshotAndReset().PlayerSurfaceQuery.Calls == 1, "cache seed performs one query");

        var jumpSetter = s_jumpJetsRequested.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("Jump jet request setter not found.");
        jumpSetter.Invoke(player.VrRig, [true]);
        Check(Surface(player, position).Success && Surface(player, position).Success,
            "requested jump jets continue to resolve the ground surface");
        Check(QuestCpuTelemetry.SnapshotAndReset().PlayerSurfaceQuery.Calls == 2,
            "requested jump jets bypass the grounded surface cache");
        jumpSetter.Invoke(player.VrRig, [false]);

        var jumpJets = Field<MechJumpJets>(player, "m_jumpJets");
        jumpJets.Advance(0.9, thrustRequested: true, heightAboveGroundMeters: 0);
        Check(jumpJets.IsAirborne, "fixture can enter airborne state through the real jump-jet simulation");
        QuestCpuTelemetry.Reset();
        Check(Surface(player, position).Success && Surface(player, position).Success,
            "airborne motion continues to resolve the ground surface");
        Check(QuestCpuTelemetry.SnapshotAndReset().PlayerSurfaceQuery.Calls == 2,
            "airborne motion bypasses the grounded surface cache");
        jumpJets.Advance(2.0, thrustRequested: false, heightAboveGroundMeters: jumpJets.MaximumHeightMeters);
        Check(!jumpJets.IsAirborne, "jump-jet fixture settles back to grounded state");
        QuestCombatTelemetry.Active = false;
    }

    private static void CheckGroundClearance(PlayerMech player)
    {
        var originalPosition = player.Position;
        var originalElevation = Field<float>(player, "m_gaitGroundElevation");
        try
        {
            QuestCombatDiagnostics.LegacyPlayerGrounding = false;
            s_gaitGroundElevation.SetValue(player, 0.1f);
            QuestCombatTelemetry.Active = true;
            QuestCpuTelemetry.Reset();
            s_applyGaitGroundClearance.Invoke(player, [0.1f]);
            var sample = QuestCpuTelemetry.SnapshotAndReset();
            Near(0.0f, Field<float>(player, "m_gaitGroundElevation"), 0.0001f, "Quest clearance settles hidden-leg offset to zero");
            Near(originalPosition.Y - 0.1f, player.Position.Y, 0.0002f, "chassis follows the settled hidden-leg offset");
            Check(sample.PlayerGroundClearance.Calls == 1 && sample.PlayerSurfaceQuery.Calls == 0,
                "Quest hidden-leg clearance runs without requesting a chassis surface query");
            Check(float.IsFinite(player.Position.X) && float.IsFinite(player.Position.Y) && float.IsFinite(player.Position.Z),
                "optimized chassis position remains finite");

            var clearance = (Action<float>)s_applyGaitGroundClearance.CreateDelegate(typeof(Action<float>), player);
            s_gaitGroundElevation.SetValue(player, 0.0f);
            player.Position = originalPosition;
            QuestCombatDiagnostics.LegacyPlayerGrounding = true;
            QuestCpuTelemetry.Reset();
            for (var index = 0; index < 100; index++) clearance(1.0f / 60.0f);
            var legacy = QuestCpuTelemetry.SnapshotAndReset().PlayerGroundClearance;

            QuestCombatDiagnostics.LegacyPlayerGrounding = false;
            for (var index = 0; index < 100; index++) clearance(1.0f / 60.0f);
            var optimized = QuestCpuTelemetry.SnapshotAndReset().PlayerGroundClearance;
            GD.Print($"QUEST_PLAYER_GROUNDING_TIMING: legacy calls={legacy.Calls} elapsedMs={legacy.ElapsedMs:F3} bytes={legacy.AllocatedBytes}; optimized calls={optimized.Calls} elapsedMs={optimized.ElapsedMs:F3} bytes={optimized.AllocatedBytes}");
            Check(legacy.Calls == 100 && optimized.Calls == 100, "timed clearance variants exercise 100 callbacks each");
            Check(Field<float>(player, "m_gaitGroundElevation") < 0.0001f,
                "optimized clearance leaves no toe-driven chassis hover offset");
        }
        finally
        {
            QuestCombatTelemetry.Active = false;
            QuestCombatDiagnostics.LegacyPlayerGrounding = false;
            s_gaitGroundElevation.SetValue(player, originalElevation);
            player.Position = originalPosition;
        }
    }

    private void CheckGaitPhaseAndFootfall()
    {
        var legacyHost = new Node3D { Name = "LegacyGaitHost" };
        var optimizedHost = new Node3D { Name = "OptimizedGaitHost" };
        AddChild(legacyHost);
        AddChild(optimizedHost);
        var legacy = new MechRig();
        var optimized = new MechRig();
        legacyHost.AddChild(legacy);
        optimizedHost.AddChild(optimized);
        var optimizedParts = new List<(Node3D Node, Vector3 Rotation)>();
        RegisterRigParts(legacyHost, legacy, null);
        RegisterRigParts(optimizedHost, optimized, optimizedParts);

        var desktopMovedPose = false;
        for (var frame = 0; frame < 120; frame++)
        {
            var distance = frame % 25 < 12 ? 0.13f : -0.08f;
            var heading = frame % 17 == 0 ? 0.04f : 0.0f;
            var desktopFootfall = legacy.Advance(distance, heading, 0.48f, 1.0f / 60.0f);
            var questFootfall = optimized.Advance(distance, heading, 0.48f, 1.0f / 60.0f, applyPose: false);
            Check(desktopFootfall == questFootfall, $"gait footfall parity at frame {frame}");
            Near(legacy.Phase, optimized.Phase, 0.00001f, $"gait phase parity at frame {frame}");
            Near(legacy.Weight, optimized.Weight, 0.00001f, $"gait weight parity at frame {frame}");
            foreach (var part in optimizedParts)
                Check(part.Node.Rotation == part.Rotation, $"optimized gait leaves joint pose unchanged at frame {frame}");
            desktopMovedPose |= RigHasPoseChange(legacyHost);
        }
        Check(desktopMovedPose, "default desktop gait continues applying articulated joint poses");
        legacyHost.QueueFree();
        optimizedHost.QueueFree();
    }

    private static void RegisterRigParts(Node3D host, MechRig rig, List<(Node3D Node, Vector3 Rotation)> captured)
    {
        foreach (var partName in new[] { "LEFTUPPERLEG", "LEFTLOWERLEG", "LFTOE", "RIGHTUPPERLEG", "RIGHTLOWERLEG", "RFTOE" })
        {
            var part = new Node3D { Name = partName };
            if (captured != null) part.Rotation = new Vector3(0.17f, -0.08f, 0.03f);
            host.AddChild(part);
            Check(rig.RegisterPart(part, partName), $"register gait part {partName}");
            captured?.Add((part, part.Rotation));
        }
    }

    private static bool RigHasPoseChange(Node3D host) => host.GetChildren().OfType<Node3D>()
        .Where(node => node.Name.ToString().Contains("LEG", StringComparison.Ordinal) || node.Name.ToString().Contains("TOE", StringComparison.Ordinal))
        .Any(node => node.Rotation.LengthSquared() > 0.000001f);

    private static (bool Success, float Height, float Slope) Surface(PlayerMech player, Vector3 position)
    {
        var parameters = new object[] { position, 0.0f, 0.0f };
        var success = (bool)s_tryGetSurface.Invoke(player, parameters)!;
        return (success, (float)parameters[1], (float)parameters[2]);
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Near(float expected, float actual, float tolerance, string description)
    {
        if (!float.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{description}: expected {expected}, got {actual}");
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
    }
}
#endif
