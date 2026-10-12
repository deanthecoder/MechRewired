// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Resources;

namespace MechRewired;

/// <summary>World-fixed, seated clan campaign selection for Quest.</summary>
public sealed partial class QuestVrClanSelection : Node3D
{
    private const float Distance = 1.4f;
    private const float TriggerThreshold = 0.7f;
    private readonly Action<ClanCampaignSelection> m_selected;
    private readonly OpenXRInterface m_interface;
    private readonly XROrigin3D m_origin;
    private readonly XRCamera3D m_camera;
    private readonly XRController3D m_rightAim;
    private readonly XRController3D m_rightController;
    private readonly Node3D m_panel;
    private readonly List<ClanTarget> m_targets = [];
    private Label3D m_status;
    private MeshInstance3D m_cursor;
    private MeshInstance3D m_beam;
    private bool m_loading;
    private bool m_anchored;
    private bool m_requireTriggerRelease = true;
    private int m_focusedTrackingFrames;
    private int m_hoveredTarget = -1;

    public QuestVrClanSelection(MechWarriorProjectArchive archive, Action<ClanCampaignSelection> selected)
    {
        ArgumentNullException.ThrowIfNull(archive);
        m_selected = selected ?? throw new ArgumentNullException(nameof(selected));
        m_interface = XRServer.FindInterface("OpenXR") as OpenXRInterface;
        Name = "QuestVrClanSelection";
        ProcessMode = ProcessModeEnum.Always;

        m_origin = new XROrigin3D { Name = "QuestClanSelectionOrigin" };
        AddChild(m_origin);
        m_camera = new XRCamera3D
        {
            Name = "QuestClanSelectionCamera", Current = true, Near = 0.05f, Far = 8000.0f
        };
        m_origin.AddChild(m_camera);
        m_rightAim = CreateController("QuestClanSelectionRightAim", "aim");
        m_rightController = CreateController("QuestClanSelectionRightHand", "grip");

        m_panel = new Node3D { Name = "QuestClanSelectionPanel", Visible = false };
        AddChild(m_panel);
        CreatePanel(archive);
        CreateRay();
    }

    /// <summary>Returns this selection camera to the viewport after a failed campaign load.</summary>
    public void ActivateCamera()
    {
        m_camera.Current = true;
        m_camera.MakeCurrent();
    }

    /// <summary>Prevents selection while a campaign is loading and gives a clear retry state afterwards.</summary>
    public void SetLoading(bool loading)
    {
        m_loading = loading;
        m_requireTriggerRelease = true;
        m_status.Text = loading ? "LOADING CAMPAIGN..." : "LOAD FAILED - POINT AT A CLAN TO RETRY";
        if (loading)
        {
            m_hoveredTarget = -1;
            UpdateTargetAppearance();
        }
    }

    public override void _Ready()
    {
        ActivateCamera();
        if (QuestVrRuntime.Preview)
            Input.MouseMode = Input.MouseModeEnum.Visible;
        if (QuestVrRuntime.Preview)
            AnchorPanel();
    }

    public override void _Process(double delta)
    {
        EnsurePanelAnchored();
        if (!m_anchored || m_loading)
        {
            HideRay();
            return;
        }

        // Preview selection comes from the mouse ray in _UnhandledInput.
        if (QuestVrRuntime.Preview)
            return;

        var headTracked = (XRServer.GetTracker("head") as XRPositionalTracker)?.GetPose("default")?.HasTrackingData == true;
        var focused = m_interface?.GetSessionState() == OpenXRInterface.SessionState.Focused;
        if (!focused || !headTracked || !m_rightAim.GetIsActive() || !m_rightController.GetIsActive())
        {
            m_hoveredTarget = -1;
            UpdateTargetAppearance();
            HideRay();
            m_requireTriggerRelease = true;
            return;
        }

        UpdateRay(m_rightAim.GlobalPosition, -m_rightAim.GlobalBasis.Z);
        var triggerDown = m_rightController.GetFloat("trigger") > TriggerThreshold;
        if (!triggerDown)
            m_requireTriggerRelease = false;
        else if (!m_requireTriggerRelease)
        {
            m_requireTriggerRelease = true;
            if (m_hoveredTarget >= 0) Select(m_targets[m_hoveredTarget].Campaign);
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!QuestVrRuntime.Preview || !m_anchored || m_loading || inputEvent is not InputEventMouse mouse)
            return;

        UpdateRay(m_camera.ProjectRayOrigin(mouse.Position), m_camera.ProjectRayNormal(mouse.Position));
        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && m_hoveredTarget >= 0)
        {
            Select(m_targets[m_hoveredTarget].Campaign);
            GetViewport().SetInputAsHandled();
        }
    }

    private XRController3D CreateController(string name, string pose)
    {
        var controller = new XRController3D { Name = name, Tracker = "right_hand", Pose = pose };
        m_origin.AddChild(controller);
        return controller;
    }

    private void EnsurePanelAnchored()
    {
        if (m_anchored)
            return;
        if (QuestVrRuntime.Preview)
        {
            AnchorPanel();
            return;
        }

        var focused = m_interface?.GetSessionState() == OpenXRInterface.SessionState.Focused;
        var headTracked = (XRServer.GetTracker("head") as XRPositionalTracker)?.GetPose("default")?.HasTrackingData == true;
        m_focusedTrackingFrames = focused && headTracked ? m_focusedTrackingFrames + 1 : 0;
        if (m_focusedTrackingFrames >= 2)
            AnchorPanel();
    }

    private void AnchorPanel()
    {
        // Keep the slate upright and in the room, rather than attaching it to the user's gaze.
        var forward = -m_camera.GlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();
        var yaw = Mathf.Atan2(-forward.X, -forward.Z);
        m_panel.GlobalBasis = new Basis(Vector3.Up, yaw);
        m_panel.GlobalPosition = m_camera.GlobalPosition + forward * Distance + new Vector3(0.0f, -0.06f, 0.0f);
        m_panel.Visible = true;
        m_anchored = true;
    }

    private void CreatePanel(MechWarriorProjectArchive archive)
    {
        AddQuad(m_panel, new Vector2(1.22f, 0.86f), Vector3.Zero, FlatMaterial(new Color(0.012f, 0.035f, 0.05f, 0.96f)));
        AddLabel(m_panel, "MECHWARRIOR 2  /  CHOOSE YOUR CLAN", new Vector3(-0.52f, 0.35f, 0.012f), 42,
            new Color(0.35f, 0.95f, 0.85f));
        AddLabel(m_panel, "POINT AT A CLAN AND PULL THE RIGHT TRIGGER", new Vector3(-0.52f, 0.295f, 0.012f), 26,
            new Color(0.64f, 0.76f, 0.76f));

        AddTarget(archive, ClanCampaignSelection.JadeFalcon, "CLAN JADE FALCON", "STORMCROW",
            new Vector3(-0.30f, -0.035f, 0.006f));
        AddTarget(archive, ClanCampaignSelection.Wolf, "CLAN WOLF", "MAD DOG",
            new Vector3(0.30f, -0.035f, 0.006f));
        m_status = AddLabel(m_panel, "CHOOSE A CLAN CAMPAIGN", new Vector3(-0.52f, -0.36f, 0.012f), 26,
            new Color(0.78f, 0.86f, 0.85f));
    }

    private void AddTarget(MechWarriorProjectArchive archive, ClanCampaignSelection campaign, string name, string level, Vector3 position)
    {
        var material = FlatMaterial(new Color(0.035f, 0.12f, 0.15f, 0.98f));
        material.RenderPriority = 1;
        var hoverMaterial = FlatMaterial(new Color(0.10f, 0.34f, 0.34f, 0.98f));
        hoverMaterial.RenderPriority = 1;
        var target = AddQuad(m_panel, new Vector2(0.54f, 0.52f), position, material);
        var insignia = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.28f, 0.28f) },
            Position = position + new Vector3(0.0f, 0.075f, 0.014f),
            MaterialOverride = InsigniaMaterial(ClanSelectionScreen.LoadInsigniaTexture(archive, campaign)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        m_panel.AddChild(insignia);
        var title = AddLabel(m_panel, name, position + new Vector3(0.0f, -0.155f, 0.016f), 32, Colors.White);
        title.Width = 460;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var startingLevel = AddLabel(m_panel, level, position + new Vector3(0.0f, -0.215f, 0.016f), 26, new Color(0.63f, 0.78f, 0.77f));
        startingLevel.Width = 460;
        startingLevel.HorizontalAlignment = HorizontalAlignment.Center;
        m_targets.Add(new ClanTarget(campaign, target, material, hoverMaterial, title, startingLevel));
    }

    private void CreateRay()
    {
        m_cursor = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.012f, Height = 0.024f },
            MaterialOverride = FlatMaterial(new Color(0.35f, 1.0f, 0.88f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        AddChild(m_cursor);
        m_beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.0015f, BottomRadius = 0.0015f, Height = 1.0f },
            MaterialOverride = FlatMaterial(new Color(0.25f, 0.8f, 0.72f, 0.70f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        AddChild(m_beam);
    }

    private void UpdateRay(Vector3 origin, Vector3 direction)
    {
        var localOrigin = m_panel.ToLocal(origin);
        var localDirection = m_panel.GlobalBasis.Inverse() * direction;
        m_hoveredTarget = -1;
        var distance = Distance;
        if (!Mathf.IsZeroApprox(localDirection.Z))
        {
            var planeDistance = -localOrigin.Z / localDirection.Z;
            var hit = localOrigin + localDirection * planeDistance;
            if (planeDistance > 0.0f)
            {
                for (var i = 0; i < m_targets.Count; i++)
                {
                    var position = m_targets[i].Panel.Position;
                    if (Mathf.Abs(hit.X - position.X) <= 0.27f && Mathf.Abs(hit.Y - position.Y) <= 0.26f)
                    {
                        m_hoveredTarget = i;
                        distance = planeDistance;
                        m_cursor.GlobalPosition = origin + direction * planeDistance;
                        m_cursor.Visible = true;
                        break;
                    }
                }
            }
        }
        if (m_hoveredTarget < 0)
            m_cursor.Visible = false;
        m_beam.Visible = true;
        m_beam.GlobalPosition = origin + direction * (distance * 0.5f);
        m_beam.GlobalBasis = new Basis(new Quaternion(Vector3.Up, direction));
        m_beam.Scale = new Vector3(1.0f, distance, 1.0f);
        UpdateTargetAppearance();
    }

    private void Select(ClanCampaignSelection campaign)
    {
        if (m_loading)
            return;
        m_requireTriggerRelease = true;
        SetLoading(true);
        m_selected(campaign);
    }

    private void UpdateTargetAppearance()
    {
        for (var i = 0; i < m_targets.Count; i++)
        {
            var target = m_targets[i];
            var hovered = !m_loading && i == m_hoveredTarget;
            target.Panel.MaterialOverride = hovered ? target.HoverMaterial : target.Material;
            target.Title.Modulate = hovered ? new Color(0.72f, 1.0f, 0.94f) : Colors.White;
        }
    }

    private void HideRay()
    {
        m_cursor.Visible = false;
        m_beam.Visible = false;
    }

    private static MeshInstance3D AddQuad(Node parent, Vector2 size, Vector3 position, Godot.Material material)
    {
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = size },
            Position = position,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        parent.AddChild(quad);
        return quad;
    }

    private static Label3D AddLabel(Node parent, string text, Vector3 position, int size, Color color)
    {
        var label = new Label3D
        {
            Text = text,
            Position = position,
            FontSize = size,
            PixelSize = 0.001f,
            Modulate = color,
            NoDepthTest = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 1040,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
            RenderPriority = 3
        };
        parent.AddChild(label);
        return label;
    }

    private static StandardMaterial3D InsigniaMaterial(Texture2D texture) => new()
    {
        AlbedoTexture = texture,
        RenderPriority = 2,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true
    };

    private static StandardMaterial3D FlatMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = true
    };

    private sealed record ClanTarget(
        ClanCampaignSelection Campaign,
        MeshInstance3D Panel,
        Godot.Material Material,
        Godot.Material HoverMaterial,
        Label3D Title,
        Label3D StartingLevel);
}
