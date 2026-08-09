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
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>
/// Runs the external rising camera, fade and automatic restart after player destruction.
/// </summary>
public partial class PlayerDeathSequence : Node
{
    private readonly PlayerMech m_playerMech;
    private readonly BattlefieldEffects m_battlefieldEffects;
    private readonly AudioStreamWav m_deathExplosion;
    private readonly AudioStreamPlayer m_missionFailed;
    private readonly ColorRect m_fade;
    private PlayerDeathTimeline m_timeline;
    private float m_orbitRadius;
    private float m_startingAngle;
    private float m_startingHeight;
    private bool m_active;
    private bool m_restartRequested;
    private bool m_missionFailedPlayed;

    public PlayerDeathSequence(
        PlayerMech playerMech,
        BattlefieldEffects battlefieldEffects,
        AudioStreamWav deathExplosion,
        AudioStreamWav missionFailed)
    {
        m_playerMech = playerMech ?? throw new ArgumentNullException(nameof(playerMech));
        m_battlefieldEffects = battlefieldEffects ?? throw new ArgumentNullException(nameof(battlefieldEffects));
        m_deathExplosion = deathExplosion ?? throw new ArgumentNullException(nameof(deathExplosion));
        ArgumentNullException.ThrowIfNull(missionFailed);
        Name = "PlayerDeathSequence";
        m_missionFailed = new AudioStreamPlayer
        {
            Name = "MissionFailedReport",
            Stream = missionFailed
        };
        AddChild(m_missionFailed);
        var fadeLayer = new CanvasLayer
        {
            Name = "DeathFadeLayer",
            Layer = 100
        };
        AddChild(fadeLayer);
        m_fade = new ColorRect
        {
            Name = "DeathFade",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = new Color(0.0f, 0.0f, 0.0f, 0.0f)
        };
        fadeLayer.AddChild(m_fade);
    }

    public override void _Ready() => m_playerMech.Destroyed += Begin;

    public override void _ExitTree() => m_playerMech.Destroyed -= Begin;

    public override void _Process(double delta)
    {
        if (!m_active || m_restartRequested)
        {
            return;
        }

        var frame = m_timeline.Advance(delta);
        var camera = m_playerMech.ExternalCamera;
        var target = m_playerMech.TargetPosition;
        var angle = m_startingAngle + (float)frame.OrbitRadians;
        camera.GlobalPosition = target + new Vector3(
            Mathf.Sin(angle) * m_orbitRadius,
            m_startingHeight + (float)frame.AscentMeters,
            Mathf.Cos(angle) * m_orbitRadius);
        camera.LookAt(target);
        m_fade.Color = new Color(0.0f, 0.0f, 0.0f, (float)frame.FadeOpacity);
        if (!m_missionFailedPlayed && frame.FadeOpacity > 0.0)
        {
            m_missionFailedPlayed = true;
            m_missionFailed.Play();
        }

        if (!frame.ShouldRestart)
        {
            return;
        }

        m_restartRequested = true;
        GD.Print("MechRewired: death sequence complete; restarting current mission.");
        var error = GetTree().ReloadCurrentScene();
        if (error != Error.Ok)
        {
            GD.PushError($"MechRewired could not restart the current mission: {error}.");
        }
    }

    private void Begin()
    {
        if (m_active)
        {
            return;
        }

        m_active = true;
        m_timeline = new PlayerDeathTimeline();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        m_playerMech.CockpitCamera.Current = false;
        m_playerMech.ExternalCamera.Current = true;
        var target = m_playerMech.TargetPosition;
        var offset = m_playerMech.ExternalCamera.GlobalPosition - target;
        m_orbitRadius = Math.Max(new Vector2(offset.X, offset.Z).Length(), 20.0f);
        m_startingAngle = Mathf.Atan2(offset.X, offset.Z);
        m_startingHeight = offset.Y;
        m_battlefieldEffects.SpawnDestruction(
            "PlayerMech",
            0,
            m_playerMech.WorldBounds,
            m_playerMech.TargetPosition,
            m_deathExplosion);
        GD.Print("MechRewired: player controls offline; external death camera engaged.");
    }
}
