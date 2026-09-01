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

/// <summary>Shows the in-mission objective summary and a concise pilot control reference.</summary>
/// <remarks>
/// F12 preserves the original game's objective-summary binding, while F1 provides a modern
/// quick-reference page without requiring the player to leave the cockpit. The original archive
/// contains no standalone objective or control-reference artwork, so this panel is procedural and
/// follows the existing green/cyan cockpit display language.
/// </remarks>
public partial class PilotReferenceOverlay : CanvasLayer
{
    private const float PanelWidth = 1120.0f;
    private const float PanelHeight = 650.0f;
    private static readonly Color NeonCyan = Color.FromHtml("31d8ff");
    private static readonly Color NeonGreen = Color.FromHtml("20f078");
    private static readonly Color WarmAmber = Color.FromHtml("e5b93f");
    private static readonly Color PrimaryText = Color.FromHtml("d8f7ff");
    private static readonly Color SecondaryText = Color.FromHtml("8db5bf");
    private static readonly Color PendingText = Color.FromHtml("66818a");

    private readonly PlayerMission m_mission;
    private Control m_objectivesPage;
    private Control m_controlsPage;
    private GridContainer m_objectiveGrid;
    private Font m_font;
    private ReferencePage m_page;
    private bool m_wasPaused;

    public PilotReferenceOverlay(PlayerMission mission)
    {
        m_mission = mission ?? throw new ArgumentNullException(nameof(mission));
        Name = "PilotReferenceOverlay";
        Layer = 150;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        m_font = GD.Load<FontFile>("res://Assets/Fonts/Orbitron-Variable.ttf") ??
                 ThemeDB.FallbackFont;

        var backdrop = new ColorRect
        {
            Name = "ReferenceBackdrop",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Color = new Color(0.0f, 0.025f, 0.035f, 0.9f)
        };
        AddChild(backdrop);

        var center = new CenterContainer
        {
            Name = "ReferenceCenter",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(center);

        var panel = new PanelContainer
        {
            Name = "ReferencePanel",
            CustomMinimumSize = new Vector2(PanelWidth, PanelHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        center.AddChild(panel);

        var pageHost = new Control
        {
            Name = "ReferencePageHost",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.AddChild(pageHost);

        m_objectivesPage = BuildObjectivesPage();
        pageHost.AddChild(m_objectivesPage);
        m_controlsPage = BuildControlsPage();
        pageHost.AddChild(m_controlsPage);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey keyEvent)
        {
            if (IsPlainKey(keyEvent, Key.F1))
            {
                GetViewport().SetInputAsHandled();
                if (keyEvent.Pressed && !keyEvent.Echo)
                {
                    Toggle(ReferencePage.Controls);
                }
                return;
            }

            if (IsPlainKey(keyEvent, Key.F12))
            {
                GetViewport().SetInputAsHandled();
                if (keyEvent.Pressed && !keyEvent.Echo)
                {
                    Toggle(ReferencePage.Objectives);
                }
                return;
            }

            if (Visible && keyEvent is { Pressed: true, Echo: false, Keycode: Key.Escape })
            {
                GetViewport().SetInputAsHandled();
                Close();
                return;
            }
        }

        if (Visible)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        if (Visible && GetTree() != null)
        {
            GetTree().Paused = m_wasPaused;
        }
    }

    private static bool IsPlainKey(InputEventKey keyEvent, Key key) =>
        keyEvent.Keycode == key &&
        !keyEvent.CtrlPressed &&
        !keyEvent.AltPressed &&
        !keyEvent.MetaPressed &&
        !keyEvent.ShiftPressed;

    private void Toggle(ReferencePage page)
    {
        if (Visible && m_page == page)
        {
            Close();
            return;
        }

        if (!Visible)
        {
            m_wasPaused = GetTree().Paused;
            GetTree().Paused = true;
        }

        m_page = page;
        m_objectivesPage.Visible = page == ReferencePage.Objectives;
        m_controlsPage.Visible = page == ReferencePage.Controls;
        if (page == ReferencePage.Objectives)
        {
            RefreshObjectives();
        }

        Visible = true;
    }

    private void Close()
    {
        if (!Visible)
        {
            return;
        }

        Visible = false;
        GetTree().Paused = m_wasPaused;
    }

    private Control BuildObjectivesPage()
    {
        var page = CreatePageMargin("ObjectivesPage");
        var content = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        content.AddThemeConstantOverride("separation", 12);
        page.AddChild(content);
        AddPageHeading(content, "MISSION OBJECTIVES");
        content.AddChild(CreateDivider());

        m_objectiveGrid = new GridContainer
        {
            Name = "ObjectiveGrid",
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        m_objectiveGrid.AddThemeConstantOverride("h_separation", 24);
        m_objectiveGrid.AddThemeConstantOverride("v_separation", 16);
        content.AddChild(m_objectiveGrid);

        page.Visible = false;
        return page;
    }

    private Control BuildControlsPage()
    {
        var page = CreatePageMargin("ControlsPage");
        var content = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        content.AddThemeConstantOverride("separation", 12);
        page.AddChild(content);
        AddPageHeading(content, "PILOT CONTROLS");
        content.AddChild(CreateDivider());

        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        columns.AddThemeConstantOverride("separation", 22);
        content.AddChild(columns);

        var left = CreateControlColumn();
        columns.AddChild(left);
        columns.AddChild(CreateVerticalDivider());
        var right = CreateControlColumn();
        columns.AddChild(right);

        AddControlSection(left, "MISSION",
            new ControlBinding(["F1"], "Quick controls"),
            new ControlBinding(["F12"], "Mission objectives"),
            new ControlBinding(["N", "OR", "Shift", "+", "N"], "Next / previous NAV"),
            new ControlBinding(["A"], "Autopilot to selected NAV"));
        AddControlSection(left, "MOVEMENT",
            new ControlBinding(["0–9"], "Set throttle"),
            new ControlBinding(["−", "OR", "="], "Lower / raise throttle"),
            new ControlBinding(["Backspace"], "Toggle reverse"),
            new ControlBinding(["←", "OR", "→"], "Steer legs"),
            new ControlBinding(["↑", "OR", "↓"], "Tilt torso"),
            new ControlBinding([",", "OR", "."], "Turn torso"),
            new ControlBinding(["Shift", "+", "Arrows"], "Look around"),
            new ControlBinding(["/"], "Center torso and view"),
            new ControlBinding(["M"], "Align legs to torso"));

        AddControlSection(right, "COMBAT",
            new ControlBinding(["Space", "OR", "Mouse 1"], "Fire selected weapon"),
            new ControlBinding(["Enter", "OR", "Tab"], "Cycle weapon"),
            new ControlBinding(["T", "OR", "R"], "Next / previous hostile"),
            new ControlBinding(["Ctrl", "+", "T"], "Clear target"),
            new ControlBinding(["E"], "Nearest hostile"),
            new ControlBinding(["Q", "OR", "Mouse 3"], "Target under reticle"),
            new ControlBinding(["I"], "Inspect target"),
            new ControlBinding(["Shift", "+", "1–3"], "Assign weapon group"),
            new ControlBinding(["'", "OR", ";"], "Next / fire group"),
            new ControlBinding(["\\"], "Chain / group fire"));
        AddControlSection(right, "SYSTEMS & VIEW",
            new ControlBinding(["S"], "Shutdown / restart"),
            new ControlBinding(["O"], "Shutdown override"),
            new ControlBinding(["X", "OR", "Shift", "+", "X"], "Radar range down / up"),
            new ControlBinding(["Z", "OR", "Shift", "+", "Z"], "Zoom / wide view"),
            new ControlBinding(["C"], "Cockpit / external view"),
            new ControlBinding(["F4"], "Cycle all cameras"),
            new ControlBinding(["Esc"], "Release mouse"));

        page.Visible = false;
        return page;
    }

    private void RefreshObjectives()
    {
        foreach (var child in m_objectiveGrid.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var objective in m_mission.Objectives)
        {
            var state = m_mission.GetState(objective.Id);
            var status = state switch
            {
                MissionObjectiveState.Completed => "COMPLETE",
                MissionObjectiveState.Active => "ACTIVE",
                _ => "PENDING"
            };
            var color = state == MissionObjectiveState.Completed
                ? NeonGreen
                : objective.IsOptional
                    ? WarmAmber
                    : state == MissionObjectiveState.Active
                        ? NeonCyan
                        : PendingText;
            var statusLabel = CreateLabel(status, 17, color, HorizontalAlignment.Right);
            statusLabel.CustomMinimumSize = new Vector2(160.0f, 30.0f);
            statusLabel.VerticalAlignment = VerticalAlignment.Center;
            m_objectiveGrid.AddChild(statusLabel);

            var description = objective.Description;
            if (objective.IsOptional)
            {
                description += "  //  OPTIONAL";
            }
            var descriptionLabel = CreateLabel(description, 18, color, HorizontalAlignment.Left);
            descriptionLabel.CustomMinimumSize = new Vector2(0.0f, 30.0f);
            descriptionLabel.VerticalAlignment = VerticalAlignment.Center;
            descriptionLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            m_objectiveGrid.AddChild(descriptionLabel);
        }
    }

    private MarginContainer CreatePageMargin(string name)
    {
        var page = new MarginContainer
        {
            Name = name,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        page.AddThemeConstantOverride("margin_left", 30);
        page.AddThemeConstantOverride("margin_top", 24);
        page.AddThemeConstantOverride("margin_right", 30);
        page.AddThemeConstantOverride("margin_bottom", 20);
        return page;
    }

    private void AddPageHeading(VBoxContainer parent, string title)
    {
        var header = new CenterContainer
        {
            Name = "PageHeader",
            CustomMinimumSize = new Vector2(0.0f, 56.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        header.AddChild(CreateLabel(title, 28, NeonCyan, HorizontalAlignment.Center));
        parent.AddChild(header);
    }

    private VBoxContainer CreateControlColumn()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        column.AddThemeConstantOverride("separation", 12);
        return column;
    }

    private void AddControlSection(
        VBoxContainer parent,
        string title,
        params ControlBinding[] bindings)
    {
        var section = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        section.AddThemeConstantOverride("separation", 5);
        parent.AddChild(section);

        section.AddChild(CreateLabel(title, 14, NeonCyan, HorizontalAlignment.Left));
        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        grid.AddThemeConstantOverride("h_separation", 12);
        // Keep the denser controls column within the panel's available height. If its minimum
        // height overflows, Godot shifts the whole VBox upward and the shared page header no
        // longer renders at the same position as the objectives header.
        grid.AddThemeConstantOverride("v_separation", 2);
        section.AddChild(grid);

        foreach (var binding in bindings)
        {
            grid.AddChild(CreateKeyGroup(binding.Tokens));
            var description = CreateLabel(binding.Description, 13, PrimaryText, HorizontalAlignment.Left);
            description.VerticalAlignment = VerticalAlignment.Center;
            description.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(description);
        }
    }

    private HBoxContainer CreateKeyGroup(IReadOnlyList<string> tokens)
    {
        var group = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(205.0f, 25.0f),
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        group.AddThemeConstantOverride("separation", 4);
        foreach (var token in tokens)
        {
            if (token is "+" or "OR")
            {
                var separatorText = token == "OR" ? "/" : token;
                var separator = CreateLabel(separatorText, 12, SecondaryText, HorizontalAlignment.Center);
                separator.CustomMinimumSize = new Vector2(10.0f, 24.0f);
                separator.VerticalAlignment = VerticalAlignment.Center;
                group.AddChild(separator);
                continue;
            }

            group.AddChild(CreateKeyCap(token));
        }

        return group;
    }

    private PanelContainer CreateKeyCap(string text)
    {
        var width = text switch
        {
            "Shift" => 68.0f,
            "Backspace" => 92.0f,
            "Arrows" => 67.0f,
            "Mouse 1" or "Mouse 3" => 73.0f,
            _ when text.Length >= 5 => 58.0f,
            _ when text.Length >= 3 => 45.0f,
            _ => 29.0f
        };
        var key = new PanelContainer
        {
            CustomMinimumSize = new Vector2(width, 24.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        key.AddThemeStyleboxOverride("panel", CreateKeyStyle());
        var label = CreateLabel(text, 11, PrimaryText, HorizontalAlignment.Center);
        label.VerticalAlignment = VerticalAlignment.Center;
        key.AddChild(label);
        return key;
    }

    private Label CreateLabel(
        string text,
        int fontSize,
        Color color,
        HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontOverride("font", m_font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static ColorRect CreateDivider() => new()
    {
        CustomMinimumSize = new Vector2(0.0f, 1.0f),
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        Color = new Color(NeonCyan, 0.36f)
    };

    private static ColorRect CreateVerticalDivider() => new()
    {
        CustomMinimumSize = new Vector2(1.0f, 0.0f),
        SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        Color = new Color(NeonCyan, 0.24f)
    };

    private static StyleBoxFlat CreatePanelStyle() => new()
    {
        BgColor = Color.FromHtml("061116f7"),
        BorderColor = Color.FromHtml("197f99"),
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 18,
        CornerRadiusTopRight = 18,
        CornerRadiusBottomLeft = 18,
        CornerRadiusBottomRight = 18,
        ShadowColor = new Color(NeonCyan, 0.25f),
        ShadowSize = 14
    };

    private static StyleBoxFlat CreateKeyStyle() => new()
    {
        BgColor = Color.FromHtml("071b24e8"),
        BorderColor = NeonCyan,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        ShadowColor = new Color(NeonCyan, 0.42f),
        ShadowSize = 4
    };

    private enum ReferencePage
    {
        Objectives,
        Controls
    }

    private sealed record ControlBinding(IReadOnlyList<string> Tokens, string Description);
}
