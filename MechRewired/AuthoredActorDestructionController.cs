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

/// <summary>Propagates destruction through nested original-data actor assemblies.</summary>
public partial class AuthoredActorDestructionController : Node
{
    private const float FirstExplosionDelaySeconds = 0.12f;
    private const float ExplosionIntervalSeconds = 0.14f;

    private readonly IReadOnlyDictionary<BattlefieldActor, IReadOnlyList<BattlefieldActor>> m_childrenByParent;
    private readonly HashSet<BattlefieldActor> m_scheduled = new();

    public AuthoredActorDestructionController(
        IReadOnlyDictionary<BattlefieldActor, IReadOnlyList<BattlefieldActor>> childrenByParent)
    {
        ArgumentNullException.ThrowIfNull(childrenByParent);
        Name = "AuthoredActorDestruction";
        m_childrenByParent = childrenByParent;
        foreach (var parent in childrenByParent.Keys)
        {
            parent.Destroyed += OnParentDestroyed;
        }
    }

    public override void _ExitTree()
    {
        foreach (var parent in m_childrenByParent.Keys)
        {
            parent.Destroyed -= OnParentDestroyed;
        }
    }

    private void OnParentDestroyed(BattlefieldActor parent, Vector3 hitPosition)
    {
        var delay = FirstExplosionDelaySeconds;
        foreach (var child in m_childrenByParent[parent]
                     .Where(child => !child.IsDestroyed && m_scheduled.Add(child))
                     .OrderBy(child => child.Definition.ObjectId))
        {
            var timer = GetTree().CreateTimer(delay);
            timer.Timeout += () => DestroyLinkedActor(parent, child);
            delay += ExplosionIntervalSeconds;
        }
    }

    private void DestroyLinkedActor(BattlefieldActor parent, BattlefieldActor child)
    {
        if (child.IsDestroyed)
        {
            return;
        }

        var hitPosition = child.TargetPosition;
        GD.Print(
            $"MechRewired: {parent.Description} destruction propagated to authored child " +
            $"{child.Description} object {child.Definition.ObjectId}.");
        child.ApplyDamage(child.Health, hitPosition);
    }
}
