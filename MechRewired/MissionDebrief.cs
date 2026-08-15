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
using MechRewired.Missions;

namespace MechRewired;

/// <summary>Displays a deliberately plain, data-backed debrief until authored menu screens replace it.</summary>
public partial class MissionDebrief : Node
{
    private const float FadeSeconds = 0.45f;
    private readonly PlayerMission m_mission;
    private readonly CanvasLayer m_layer;
    private readonly ColorRect m_backdrop;
    private readonly VBoxContainer m_summary;
    private float m_fadeProgress;
    private float m_inputDelay;
    private bool m_presented;

    public MissionDebrief(PlayerMission mission)
    {
        m_mission = mission ?? throw new ArgumentNullException(nameof(mission));
        Name = "MissionDebrief";
        m_layer = new CanvasLayer
        {
            Name = "MissionDebriefLayer",
            Layer = 200,
            Visible = false
        };
        AddChild(m_layer);
        m_backdrop = new ColorRect
        {
            Name = "DebriefBackdrop",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Color = new Color(0.0f, 0.0f, 0.0f, 0.0f)
        };
        m_layer.AddChild(m_backdrop);
        var center = new CenterContainer
        {
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        m_layer.AddChild(center);
        m_summary = new VBoxContainer
        {
            Name = "DebriefSummary",
            CustomMinimumSize = new Vector2(620.0f, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        m_summary.AddThemeConstantOverride("separation", 12);
        center.AddChild(m_summary);
    }

    public void Present(MissionOutcome outcome)
    {
        if (m_presented || outcome == MissionOutcome.Active)
        {
            return;
        }

        m_presented = true;
        m_fadeProgress = 0.0f;
        m_inputDelay = 0.6f;
        foreach (var child in m_summary.GetChildren())
        {
            child.QueueFree();
        }

        var successful = outcome == MissionOutcome.Successful;
        AddLabel(
            successful ? "MISSION SUCCESSFUL" : "MISSION FAILED",
            successful ? Color.FromHtml("00f000") : Color.FromHtml("ff3030"),
            38);
        AddLabel("MISSION DEBRIEF", Color.FromHtml("c8c8c8"), 21);
        AddSpacer(12.0f);
        foreach (var objective in m_mission.Objectives)
        {
            var state = m_mission.GetState(objective.Id);
            var stateText = state switch
            {
                MissionObjectiveState.Completed => "COMPLETE",
                MissionObjectiveState.Active => "INCOMPLETE",
                _ => "NOT REACHED"
            };
            var color = state == MissionObjectiveState.Completed
                ? Color.FromHtml("00d860")
                : objective.IsOptional
                    ? Color.FromHtml("d0a020")
                    : Color.FromHtml("e0e0e0");
            AddLabel($"{stateText,-12} {objective.Description}", color, 20);
        }

        AddSpacer(18.0f);
        AddLabel("Press Fire to continue", Color.FromHtml("a0a0a0"), 18);
        m_layer.Visible = true;
        GD.Print($"MechRewired: presenting {outcome} diagnostic debrief from MTBL objectives.");
    }

    public override void _Process(double delta)
    {
        if (!m_presented)
        {
            return;
        }

        m_fadeProgress = Math.Min(m_fadeProgress + (float)delta / FadeSeconds, 1.0f);
        var opacity = Mathf.SmoothStep(0.0f, 1.0f, m_fadeProgress);
        m_backdrop.Color = new Color(0.0f, 0.0f, 0.0f, opacity * 0.9f);
        m_summary.Modulate = new Color(1.0f, 1.0f, 1.0f, opacity);
        m_inputDelay -= (float)delta;
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!m_presented || m_inputDelay > 0.0f)
        {
            return;
        }

        if (inputEvent is InputEventKey
            {
                Pressed: true,
                Echo: false,
                Keycode: Key.Space or Key.Enter or Key.KpEnter
            } ||
            inputEvent is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left
            })
        {
            GetViewport().SetInputAsHandled();
            Redeploy();
        }
    }

    private void Redeploy()
    {
        GD.Print("MechRewired: redeploy requested from mission debrief.");
        var error = GetTree().ReloadCurrentScene();
        if (error != Error.Ok)
        {
            GD.PushError($"MechRewired could not redeploy from debrief: {error}.");
        }
    }

    private void AddLabel(string text, Color color, int size)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", size);
        m_summary.AddChild(label);
    }

    private void AddSpacer(float height) => m_summary.AddChild(new Control
    {
        CustomMinimumSize = new Vector2(0.0f, height),
        MouseFilter = Control.MouseFilterEnum.Ignore
    });
}
