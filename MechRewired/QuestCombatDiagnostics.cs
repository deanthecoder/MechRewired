// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using Godot;

namespace MechRewired;

/// <summary>Temporary callback controls used only by the combat diagnostic suite.</summary>
/// <remarks>Restores individual callback states without disabling the XR rig or head tracking.</remarks>
internal sealed class QuestCombatDiagnostics : IDisposable
{
    private readonly List<(Node Node, bool Process, bool Physics)> m_saved = new();
    public static bool EnemyHalfRate { get; set; }
    public static bool LegacyPlayerGrounding { get; set; }

    public void Apply(Node root, string variant)
    {
        LegacyPlayerGrounding = variant == "baseline";
        EnemyHalfRate = variant == "enemy-half-rate";
        if (variant is "hud-frozen" or "simulation-frozen") Visit(root, variant);
    }

    private void Visit(Node node, string variant)
    {
        var freezeHud = variant == "hud-frozen" && node is PlayerHud;
        var freezeCombat = variant == "simulation-frozen" &&
            node is PlayerMech or EnemyMech or PlayerTargeting or MissileEffect or LaserEffect;
        if (freezeHud || freezeCombat)
        {
            m_saved.Add((node, node.IsProcessing(), node.IsPhysicsProcessing()));
            node.SetProcess(false);
            node.SetPhysicsProcess(false);
        }
        foreach (var child in node.GetChildren()) Visit(child, variant);
    }

    public void Dispose()
    {
        EnemyHalfRate = false;
        LegacyPlayerGrounding = false;
        foreach (var saved in m_saved)
        {
            if (!GodotObject.IsInstanceValid(saved.Node)) continue;
            saved.Node.SetProcess(saved.Process);
            saved.Node.SetPhysicsProcess(saved.Physics);
        }
        m_saved.Clear();
    }
}
