// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Resources;

namespace MechRewired;

/// <summary>Lets a Quest player import their own MW2.PRJ through Android's Storage Access Framework.</summary>
/// <remarks>The picker grant is used only to copy the selected archive into private app storage.</remarks>
public sealed partial class QuestVrDataSetup : Node3D
{
    private const long MaximumProjectArchiveBytes = 256L * 1024 * 1024;
    private const float TriggerThreshold = 0.7f;
    private readonly DirectoryInfo m_destination;
    private readonly Action<FileInfo> m_installed;
    private readonly XROrigin3D m_origin;
    private readonly XRCamera3D m_camera;
    private readonly XRController3D m_rightAim;
    private readonly XRController3D m_rightController;
    private Label3D m_status;
    private MeshInstance3D m_button;
    private MeshInstance3D m_cursor;
    private MeshInstance3D m_beam;
    private bool m_triggerWasDown;
    private bool m_installing;

    public QuestVrDataSetup(DirectoryInfo destination, string error, Action<FileInfo> installed)
    {
        m_destination = destination ?? throw new ArgumentNullException(nameof(destination));
        m_installed = installed ?? throw new ArgumentNullException(nameof(installed));
        Name = "QuestVrDataSetup";
        ProcessMode = ProcessModeEnum.Always;

        m_origin = new XROrigin3D { Name = "QuestDataSetupOrigin" };
        AddChild(m_origin);
        m_camera = new XRCamera3D { Name = "QuestDataSetupCamera", Current = true, Near = 0.05f };
        m_origin.AddChild(m_camera);
        m_rightAim = CreateController("QuestDataSetupRightAim", "aim");
        m_rightController = CreateController("QuestDataSetupRightHand", "grip");
        CreateSlate(error);
    }

    public override void _Process(double delta)
    {
        if (m_installing || QuestVrRuntime.Preview)
            return;

        if (!m_rightAim.GetIsActive() || !m_rightController.GetIsActive())
        {
            m_cursor.Visible = false;
            m_beam.Visible = false;
            return;
        }

        var origin = m_rightAim.GlobalPosition;
        var direction = -m_rightAim.GlobalBasis.Z;
        UpdateBeam(origin, direction);
        var hovering = IsButtonHovered(origin, direction);
        m_cursor.Visible = hovering;
        var triggerDown = m_rightController.GetFloat("trigger") > TriggerThreshold;
        if (hovering && triggerDown && !m_triggerWasDown)
            Browse();
        m_triggerWasDown = triggerDown;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!QuestVrRuntime.Preview || m_installing || inputEvent is not InputEventMouse mouse)
            return;

        var hovering = IsButtonHovered(m_camera.ProjectRayOrigin(mouse.Position), m_camera.ProjectRayNormal(mouse.Position));
        m_cursor.Visible = hovering;
        if (hovering && inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            Browse();
            GetViewport().SetInputAsHandled();
        }
    }

    private XRController3D CreateController(string name, string pose)
    {
        var controller = new XRController3D { Name = name, Tracker = "right_hand", Pose = pose };
        m_origin.AddChild(controller);
        return controller;
    }

    private void CreateSlate(string error)
    {
        var screen = new Node3D { Name = "QuestDataSetupSlate", Position = new Vector3(0, 0, -1.5f) };
        m_camera.AddChild(screen);
        screen.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.12f, 0.82f) },
            MaterialOverride = FlatMaterial(new Color(0.015f, 0.045f, 0.06f, 0.96f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
        AddLabel(screen, "MECHREWIRED", new Vector3(-0.48f, 0.31f, 0.012f), 34, new Color(0.35f, 0.95f, 0.85f));
        AddLabel(screen, "ORIGINAL GAME DATA REQUIRED", new Vector3(-0.48f, 0.23f, 0.012f), 22, Colors.White);
        AddLabel(screen, "Copy your own MW2.PRJ to Downloads/MechRewired,\nthen point at IMPORT MW2.PRJ and pull the right trigger.",
            new Vector3(-0.48f, 0.12f, 0.012f), 19, new Color(0.80f, 0.88f, 0.87f));
        AddLabel(screen, "The archive is checked, then saved only in this app's private game-data folder.",
            new Vector3(-0.48f, -0.01f, 0.012f), 17, new Color(0.60f, 0.74f, 0.75f));
        m_button = new MeshInstance3D
        {
            Name = "ImportMw2ProjectButton",
            Mesh = new QuadMesh { Size = new Vector2(0.82f, 0.11f) },
            Position = new Vector3(0, -0.17f, 0.012f),
            MaterialOverride = FlatMaterial(new Color(0.10f, 0.32f, 0.34f, 0.98f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        screen.AddChild(m_button);
        AddLabel(screen, "IMPORT MW2.PRJ", new Vector3(-0.24f, -0.19f, 0.022f), 25, new Color(0.80f, 1.0f, 0.92f));
        m_status = AddLabel(screen, string.IsNullOrWhiteSpace(error) ? "Ready to import your DOS archive." : error,
            new Vector3(-0.48f, -0.31f, 0.012f), 16, new Color(0.93f, 0.73f, 0.50f));
        m_status.Width = 960;
        m_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        m_cursor = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.012f, Height = 0.024f },
            MaterialOverride = FlatMaterial(new Color(0.35f, 1.0f, 0.88f)),
            Visible = false
        };
        screen.AddChild(m_cursor);
        m_beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.0015f, BottomRadius = 0.0015f, Height = 1.4f },
            MaterialOverride = FlatMaterial(new Color(0.25f, 0.8f, 0.72f, 0.38f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        m_origin.AddChild(m_beam);
    }

    private static Label3D AddLabel(Node parent, string text, Vector3 position, int size, Color color)
    {
        var label = new Label3D
        {
            Text = text,
            Position = position,
            FontSize = size,
            PixelSize = 0.001f,
            NoDepthTest = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 960,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        parent.AddChild(label);
        return label;
    }

    private void UpdateBeam(Vector3 origin, Vector3 direction)
    {
        const float length = 1.4f;
        m_beam.Visible = true;
        m_beam.GlobalPosition = origin + direction * (length * 0.5f);
        m_beam.GlobalBasis = new Basis(new Quaternion(Vector3.Up, direction));
    }

    private bool IsButtonHovered(Vector3 origin, Vector3 direction)
    {
        var localOrigin = m_button.ToLocal(origin);
        var localDirection = m_button.GlobalBasis.Inverse() * direction;
        if (Mathf.IsZeroApprox(localDirection.Z))
            return false;
        var distance = -localOrigin.Z / localDirection.Z;
        if (distance <= 0)
            return false;
        var hit = localOrigin + localDirection * distance;
        if (Mathf.Abs(hit.X) > 0.41f || Mathf.Abs(hit.Y) > 0.055f)
            return false;
        m_cursor.GlobalPosition = origin + direction * distance;
        return true;
    }

    private void Browse()
    {
        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Title = "Select your MW2.PRJ",
            UseNativeDialog = true,
            Filters = ["*.prj,*.PRJ;MW2.PRJ archive"]
        };
        dialog.FileSelected += path =>
        {
            dialog.QueueFree();
            Import(path);
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCenteredRatio(0.8f);
    }

    private async void Import(string selectedPath)
    {
        if (m_installing)
            return;
        m_installing = true;
        m_status.Text = "Copying selected archive into private storage…";
        FileInfo staged = null;
        try
        {
            staged = CopySelectedArchive(selectedPath);
            m_status.Text = "Checking MW2.PRJ…";
            var installed = await Task.Run(() => MechWarriorDataInstaller.Install(staged.FullName, m_destination));
            if (IsInsideTree())
            {
                m_installed(installed);
                QueueFree();
            }
        }
        catch (Exception exception)
        {
            m_status.Text = exception.Message;
            GD.PushWarning($"MechRewired Quest import: {exception.Message}");
        }
        finally
        {
            if (staged?.Directory is { Exists: true } directory)
            {
                try { directory.Delete(true); }
                catch (Exception exception) { GD.PushWarning($"MechRewired Quest import cleanup: {exception.Message}"); }
            }
            m_installing = false;
        }
    }

    private static FileInfo CopySelectedArchive(string selectedPath)
    {
        using var source = Godot.FileAccess.Open(selectedPath, Godot.FileAccess.ModeFlags.Read)
            ?? throw new IOException($"The selected MW2.PRJ could not be opened ({Godot.FileAccess.GetOpenError()}).");
        var stagingDirectory = $"user://.mw2-prj-import-{Guid.NewGuid():N}";
        var stagingFolder = new DirectoryInfo(ProjectSettings.GlobalizePath(stagingDirectory));
        stagingFolder.Create();
        try
        {
            var stagingPath = stagingDirectory + "/MW2.PRJ";
            using var destination = Godot.FileAccess.Open(stagingPath, Godot.FileAccess.ModeFlags.Write)
                ?? throw new IOException($"Private app storage could not be opened ({Godot.FileAccess.GetOpenError()}).");
            long copied = 0;
            while (!source.EofReached())
            {
                var bytes = source.GetBuffer(64 * 1024);
                if (bytes.Length == 0)
                    break;
                copied += bytes.Length;
                if (copied > MaximumProjectArchiveBytes)
                    throw new InvalidDataException("MW2.PRJ exceeds the 256 MB import limit.");
                destination.StoreBuffer(bytes);
            }
            return new FileInfo(ProjectSettings.GlobalizePath(stagingPath));
        }
        catch
        {
            stagingFolder.Delete(true);
            throw;
        }
    }

    private static StandardMaterial3D FlatMaterial(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha
    };
}
