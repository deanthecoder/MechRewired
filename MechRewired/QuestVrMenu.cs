// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using Godot;

namespace MechRewired;

/// <summary>
/// A compact, world-space pause slate for Quest, split into legible main, graphics, and HUD pages.
/// </summary>
public sealed partial class QuestVrMenu : Node3D
{
    private const float Distance = 1.15f;
    private const float Width = 0.92f;
    private const float RowHeight = 0.075f;
    private const float Top = 0.39f;
    private const float Bottom = -0.38f;
    private const float TriggerThreshold = 0.7f;
    private readonly Camera3D m_camera;
    private readonly XRController3D m_rightAim;
    private readonly XRController3D m_rightController;
    private readonly QuestGraphicsSettings m_settings;
    private readonly PlayerHud m_hud;
    private readonly Action m_recenter;
    private readonly Action m_restart;
    private readonly List<MenuRow> m_rows = [];
    private MeshInstance3D m_cursor;
    private MeshInstance3D m_beam;
    private Label3D m_performanceLabel;
    private bool m_triggerWasDown;
    private int m_hoveredRow = -1;
    private int m_lastHoveredRow = -2;
    private int m_page;
    private string m_resultSummary;
    private double m_performanceRefreshAt;
    private float m_lastRunningFps;
    private float m_lastRunningMilliseconds;

    public QuestVrMenu(
        Camera3D camera,
        XRController3D rightAim,
        XRController3D rightController,
        QuestGraphicsSettings settings,
        PlayerHud hud,
        Action recenter,
        Action restart = null)
    {
        m_camera = camera ?? throw new ArgumentNullException(nameof(camera));
        m_rightAim = rightAim ?? throw new ArgumentNullException(nameof(rightAim));
        m_rightController = rightController ?? throw new ArgumentNullException(nameof(rightController));
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_hud = hud ?? throw new ArgumentNullException(nameof(hud));
        m_recenter = recenter ?? throw new ArgumentNullException(nameof(recenter));
        m_restart = restart;
        Name = "QuestVrMenu";
        ProcessMode = ProcessModeEnum.Always;
    }

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        CreateSlate();
        AddAction("RESUME", Close);
        AddAction("RECENTER VIEW", RecenterAndClose);
        AddAction("GRAPHICS SETTINGS  >", () => SetPage(1));
        AddAction("HUD SETTINGS  >", () => SetPage(2));
        AddToggle("SUN SHADOWS", () => m_settings.SunShadowsEnabled, value => m_settings.SunShadowsEnabled = value, 1);
        AddToggle("SCENE GLOW", () => m_settings.GlowEnabled, value => m_settings.GlowEnabled = value, 1);
        AddToggle("COCKPIT GLASS", () => m_settings.CockpitGlassEnabled, value => m_settings.CockpitGlassEnabled = value, 1);
        AddToggle("TERRAIN TRIPLANAR", () => m_settings.TerrainTriplanarEnabled, value => m_settings.TerrainTriplanarEnabled = value, 1);
        AddToggle("SMOKE / DUST", () => m_settings.SmokeAndDustEnabled, value => m_settings.SmokeAndDustEnabled = value, 1);
        AddAction("<  BACK", () => SetPage(0), 1);
        AddToggle("HUD RADAR", () => m_hud.ShowRadar, value => m_hud.ShowRadar = value, 2);
        AddToggle("HUD GLOW", () => m_settings.HudGlowEnabled, value => m_settings.HudGlowEnabled = value, 2);
        AddToggle("HUD WEAPONS", () => m_hud.ShowWeapons, value => m_hud.ShowWeapons = value, 2);
        AddToggle("HUD STATUS", () => m_hud.ShowStatus, value => m_hud.ShowStatus = value, 2);
        AddToggle("HUD NAVIGATION", () => m_hud.ShowNavigation, value => m_hud.ShowNavigation = value, 2);
        AddToggle("HUD TARGETING", () => m_hud.ShowTargeting, value => m_hud.ShowTargeting = value, 2);
        AddAction("<  BACK", () => SetPage(0), 2);
        if (m_restart != null)
        {
            AddAction("RESTART MISSION", m_restart);
        }

        CreateRay();
        Visible = false;
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }

        // Project only the headset yaw, so the slate stays upright while looking up or down.
        GlobalRotation = new Vector3(0.0f, m_camera.GlobalRotation.Y, 0.0f);
        GlobalPosition = m_camera.GlobalPosition - GlobalBasis.Z * Distance;
        Visible = true;
        GetTree().Paused = true;
        m_triggerWasDown = true;
        SetPage(0);
        RefreshRows();
    }

    /// <summary>Shows a mission outcome on the same slate, with restart kept within reach.</summary>
    public void ShowMissionResult(string summary)
    {
        m_resultSummary = summary;
        if (!IsOpen)
        {
            Toggle();
        }
        else
        {
            SetPage(0);
        }
    }

    public void Close()
    {
        Visible = false;
        m_beam.Visible = false;
        m_cursor.Visible = false;
        GetTree().Paused = false;
    }

    public override void _Process(double delta)
    {
        CaptureRunningPerformance();
        if (!IsOpen)
        {
            return;
        }

        UpdatePerformanceReadout();
        // Preview selection is driven by _UnhandledInput; do not overwrite its hover state
        // each frame with the deliberately untracked desktop controller nodes.
        if (QuestVrRuntime.Preview)
        {
            return;
        }

        if (!m_rightAim.GetIsActive() || !m_rightController.GetIsActive())
        {
            m_hoveredRow = -1;
            ApplyHighlights();
            m_beam.Visible = false;
            m_cursor.Visible = false;
            return;
        }

        UpdateRay();
        var triggerDown = m_rightController.GetFloat("trigger") > TriggerThreshold;
        if (triggerDown && !m_triggerWasDown && m_hoveredRow >= 0)
        {
            m_rows[m_hoveredRow].Activate();
            RefreshRows();
        }

        m_triggerWasDown = triggerDown;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!QuestVrRuntime.Preview || !IsOpen || inputEvent is not InputEventMouse mouseEvent)
        {
            return;
        }

        UpdateRay(m_camera.ProjectRayOrigin(mouseEvent.Position), m_camera.ProjectRayNormal(mouseEvent.Position));
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && m_hoveredRow >= 0)
        {
            m_rows[m_hoveredRow].Activate();
            RefreshRows();
            GetViewport().SetInputAsHandled();
        }
    }

    private void CreateSlate()
    {
        var panel = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(Width, Top - Bottom) },
            Position = new Vector3(0.0f, (Top + Bottom) * 0.5f, 0.0f),
            MaterialOverride = FlatMaterial(new Color(0.015f, 0.045f, 0.06f, 0.94f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(panel);
        AddLabel("MECHREWIRED  /  PAUSED", new Vector3(-0.39f, 0.35f, 0.012f), 36, new Color(0.35f, 0.95f, 0.85f));
        AddLabel("POINT AND PULL TRIGGER", new Vector3(-0.39f, 0.30f, 0.012f), 18, new Color(0.55f, 0.70f, 0.72f));
        m_performanceLabel = AddLabel("", new Vector3(0.39f, -0.32f, 0.012f), 17, new Color(0.55f, 0.70f, 0.72f));
        m_performanceLabel.HorizontalAlignment = HorizontalAlignment.Right;
        m_performanceLabel.Width = 250.0f;
    }

    private void AddToggle(string caption, Func<bool> value, Action<bool> setValue, int page = 0) =>
        AddRow(caption, () => setValue(!value()), () => value() ? "ON" : "OFF", page);

    private void AddAction(string caption, Action action, int page = 0) => AddRow(caption, action, () => "SELECT", page);

    private void AddRow(string caption, Action action, Func<string> value, int page)
    {
        var index = m_rows.Count;
        var y = 0.20f - index * RowHeight;
        var row = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.78f, RowHeight - 0.008f) },
            Position = new Vector3(0.0f, y, 0.006f),
            MaterialOverride = FlatMaterial(new Color(0.04f, 0.12f, 0.15f, 0.96f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(row);
        var label = AddLabel(caption, new Vector3(-0.35f, y, 0.014f), 20, new Color(0.82f, 0.92f, 0.90f));
        var valueLabel = AddLabel(string.Empty, new Vector3(0.20f, y, 0.014f), 20, new Color(0.35f, 0.95f, 0.85f));
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        valueLabel.Width = 150.0f;
        m_rows.Add(new MenuRow(row, label, valueLabel, FlatMaterial(new Color(0.04f, 0.12f, 0.15f, 0.96f)),
            FlatMaterial(new Color(0.10f, 0.32f, 0.34f, 0.98f)), action, value, page));
    }

    private void CreateRay()
    {
        m_cursor = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.009f, Height = 0.018f },
            MaterialOverride = FlatMaterial(new Color(0.35f, 1.0f, 0.88f))
        };
        AddChild(m_cursor);
        m_beam = new MeshInstance3D
        {
            Visible = false,
            Mesh = new CylinderMesh { TopRadius = 0.0015f, BottomRadius = 0.0015f, Height = 1.0f },
            MaterialOverride = FlatMaterial(new Color(0.25f, 0.8f, 0.72f, 0.7f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        GetParent().AddChild(m_beam);
    }

    private void UpdateRay()
    {
        UpdateRay(m_rightAim.GlobalPosition, -m_rightAim.GlobalBasis.Z);
    }

    private void UpdateRay(Vector3 origin, Vector3 direction)
    {
        var localOrigin = ToLocal(origin);
        var localDirection = GlobalBasis.Inverse() * direction;
        m_hoveredRow = -1;
        var distance = Distance;
        if (!Mathf.IsZeroApprox(localDirection.Z))
        {
            var planeDistance = -localOrigin.Z / localDirection.Z;
            var hit = localOrigin + localDirection * planeDistance;
            if (planeDistance > 0.0f && Mathf.Abs(hit.X) <= Width * 0.5f && hit.Y <= Top && hit.Y >= Bottom)
            {
                distance = planeDistance;
                m_cursor.Position = new Vector3(hit.X, hit.Y, 0.025f);
                m_cursor.Visible = true;
                for (var i = 0; i < m_rows.Count; i++)
                {
                    if (m_rows[i].Page == m_page && Mathf.Abs(hit.Y - m_rows[i].Panel.Position.Y) < RowHeight * 0.5f)
                    {
                        m_hoveredRow = i;
                        break;
                    }
                }
            }
            else
            {
                m_cursor.Visible = false;
            }
        }

        m_beam.Visible = true;
        m_beam.GlobalPosition = origin + direction * distance * 0.5f;
        m_beam.GlobalBasis = new Basis(new Quaternion(Vector3.Up, direction));
        m_beam.Scale = new Vector3(1.0f, distance, 1.0f);
        ApplyHighlights();
    }

    private void RefreshRows()
    {
        var rowIndex = 0;
        for (var i = 0; i < m_rows.Count; i++)
        {
            var visible = m_rows[i].Page == m_page;
            m_rows[i].Panel.Visible = visible;
            m_rows[i].Caption.Visible = visible;
            m_rows[i].Value.Visible = visible;
            if (visible)
            {
                var y = 0.20f - rowIndex++ * RowHeight;
                m_rows[i].Panel.Position = new Vector3(0, y, 0.006f);
                m_rows[i].Caption.Position = new Vector3(-0.35f, y, 0.014f);
                m_rows[i].Value.Position = new Vector3(0.20f, y, 0.014f);
            }
            m_rows[i].Value.Text = m_rows[i].ValueText();
        }
        ApplyHighlights();
    }

    private void ApplyHighlights()
    {
        if (m_lastHoveredRow == m_hoveredRow)
        {
            return;
        }

        m_lastHoveredRow = m_hoveredRow;
        for (var i = 0; i < m_rows.Count; i++)
        {
            var hovered = i == m_hoveredRow;
            m_rows[i].Panel.MaterialOverride = hovered ? m_rows[i].HoverMaterial : m_rows[i].Material;
            m_rows[i].Caption.Modulate = hovered ? Colors.White : new Color(0.82f, 0.92f, 0.90f);
        }
    }

    private void SetPage(int page)
    {
        m_page = page;
        m_hoveredRow = -1;
        m_lastHoveredRow = -2;
        RefreshRows();
    }

    private void RecenterAndClose()
    {
        Close();
        m_recenter();
    }

    private void UpdatePerformanceReadout()
    {
        m_performanceLabel.Text = string.IsNullOrEmpty(m_resultSummary)
            ? $"LAST RUN  {m_lastRunningFps:0} FPS  {m_lastRunningMilliseconds:0.0} ms"
            : m_resultSummary;
    }

    private void CaptureRunningPerformance()
    {
        if (GetTree().Paused)
        {
            return;
        }

        var now = Time.GetTicksMsec() / 1000.0;
        if (now < m_performanceRefreshAt) return;
        m_performanceRefreshAt = now + 0.25;
        var fps = Engine.GetFramesPerSecond();
        m_lastRunningFps = (float)fps;
        m_lastRunningMilliseconds = fps > 0 ? (float)(1000.0 / fps) : 0.0f;
    }

    private Label3D AddLabel(string text, Vector3 position, int fontSize, Color color)
    {
        var label = new Label3D
        {
            Text = text,
            Position = position,
            FontSize = fontSize,
            PixelSize = 0.001f,
            Modulate = color,
            HorizontalAlignment = HorizontalAlignment.Left,
            NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
            RenderPriority = 2
        };
        AddChild(label);
        return label;
    }

    private static StandardMaterial3D FlatMaterial(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = true
    };

    private sealed record MenuRow(
        MeshInstance3D Panel,
        Label3D Caption,
        Label3D Value,
        Godot.Material Material,
        Godot.Material HoverMaterial,
        Action Activate,
        Func<string> ValueText,
        int Page);
}
