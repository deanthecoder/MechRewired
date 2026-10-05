// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

#if DEBUG
using System.Reflection;
using Godot;
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>Native regression check; run with original game data and --vr-preview.</summary>
public partial class HotPathAllocationCheck : Node
{
    public override void _Ready() => Callable.From(Run).CallDeferred();

    private async void Run()
    {
        try
        {
            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate();
            AddChild(main);
            // Let deferred mission startup and the Quest sky bake finish before pausing.
            for (var frame = 0; frame < 30; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Paused = true;
            var targeting = main.FindChild("PlayerTargeting", true, false) as PlayerTargeting
                ?? throw new InvalidOperationException("Original game data and --vr-preview are required.");
            var player = main.FindChild("PlayerMech", true, false) as PlayerMech;
            var selectMissile = Bind<Func<MechMountedWeapon>>(targeting, "GetSelectedMissile");
            var operational = Bind<Func<MechMountedWeapon, bool>>(targeting, "IsWeaponOperational");
            var updateObjective = Bind<Action>(targeting, "UpdateObjectiveActor");
            var mission = Field<PlayerMission>(targeting, "m_playerMission");
            var roots = Field<IReadOnlyDictionary<BattlefieldActor, BattlefieldActor>>(targeting, "m_objectiveRootsByActor");
            var selection = targeting.WeaponSelection;
            // Cover all groups, chain/group mode and cycling using the former query as oracle.
            for (var mode = 0; mode < 2; mode++)
            {
                for (var group = 0; group < PlayerWeaponSelection.GroupCount; group++)
                {
                    for (var weapon = 0; weapon < selection.Weapons.Count; weapon++)
                    {
                        var expected = selection.GetFireIndices().Select(index => selection.Weapons[index])
                            .FirstOrDefault(value => value.Specification.Kind == MechWeaponKind.Missile && operational(value));
                        Check(ReferenceEquals(expected, selectMissile()), "missile selection parity");
                        selection.CycleWeapon();
                    }
                    selection.CycleGroup();
                }
                selection.ToggleGroupFire();
            }
            foreach (var position in new[] { player.GlobalPosition, new Vector3(100000, 0, 100000), player.GlobalPosition })
            {
                player.GlobalPosition = position;
                var current = targeting.ObjectiveActor;
                var expected = current != null && mission.IsActiveObjectiveTarget(current) &&
                    current.TargetPosition.DistanceTo(position) <= 300 ? current : targeting.Actors
                    .Select(actor => roots.TryGetValue(actor, out var root) ? root : actor).Distinct()
                    .Where(mission.IsActiveObjectiveTarget)
                    .Select(actor => new { Actor = actor, Distance = actor.TargetPosition.DistanceTo(position) })
                    .Where(candidate => candidate.Distance <= 300).OrderBy(candidate => candidate.Distance)
                    .ThenBy(candidate => candidate.Actor.Definition.ObjectId).Select(candidate => candidate.Actor).FirstOrDefault();
                updateObjective();
                Check(ReferenceEquals(expected, targeting.ObjectiveActor), "objective query parity");
            }

            var holder = new Node3D();
            AddChild(holder);
            var rig = new MechRig();
            holder.AddChild(rig);
            var nodes = new List<Node3D>();
            foreach (var name in new[] { "LEFTUPPERLEG", "LEFTLOWERLEG", "LFTOE", "RIGHTUPPERLEG", "RIGHTLOWERLEG", "RFTOE" })
            {
                var part = new Node3D();
                holder.AddChild(part);
                Check(rig.RegisterPart(part, name), "part registration");
                nodes.Add(part);
            }
            for (var i = 0; i < 60; i++) rig.Advance(.1f, 0, .5f, 1f / 60);
            Check(nodes.Any(node => node.Rotation.LengthSquared() > 0), "animated cached parts");
            var detached = nodes[0];
            holder.RemoveChild(detached);
            var before = detached.Rotation;
            rig.Advance(.1f, 0, .5f, 1f / 60);
            Check(detached.Rotation == before, "detached part unchanged");
            detached.Free();
            rig.Advance(.1f, 0, .5f, 1f / 60); // A freed cached part must also be skipped.
            var latePart = new Node3D();
            holder.AddChild(latePart);
            Check(rig.RegisterPart(latePart, "LEFTUPPERLEG"), "late registration");
            rig.Advance(.1f, 0, .5f, 1f / 60);
            Check(latePart.Rotation.LengthSquared() > 0, "late part participates");

            player.GlobalPosition = new Vector3(100000, 0, 100000);
            Measure("gait", () => rig.Advance(.1f, 0, .5f, 1f / 60));
            Measure("missile-selection", () => selectMissile());
            Measure("objective-no-nearby-target", updateObjective);
            GD.Print("HOT_PATH_ALLOCATION_CHECK_PASS");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static T Bind<T>(object owner, string name) where T : Delegate => owner.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<T>(owner);
    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException(name);
    }
    private static void Measure(string name, Action action)
    {
        for (var i = 0; i < 100; i++) action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) action();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GD.Print($"HOT_PATH_ALLOCATION: {name} calls=1000 bytes={bytes}");
        Check(bytes == 0, name + " must not allocate after warmup");
    }
}
#endif
