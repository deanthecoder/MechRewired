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

/// <summary>
/// Provides free-flight controls for inspecting loaded battlefields.
/// </summary>
/// <remarks>
/// Click the viewport to capture the mouse; Escape releases it for normal desktop use.
/// </remarks>
public partial class DebugCamera : Camera3D
{
    public const string SolidMeshGroup = "debug_solid_meshes";
    public const string WireframeMeshGroup = "debug_wireframe_meshes";
    private const int WireframeMenuItemId = 1;
    private const int LogCameraMenuItemId = 2;
    private const int CycleCameraMenuItemId = 3;
#if DEBUG
    private const int DestroyHostilesMenuItemId = 4;
#endif
    private const float MoveSpeed = 120.0f;
    private const float BoostMultiplier = 6.0f;
    private const float MouseSensitivity = 0.002f;
    private const float MaximumPitch = Mathf.Pi / 2.0f - 0.01f;

    private bool m_wireframeEnabled;
    private PopupMenu m_debugMenu;

    public IReadOnlyList<DebugTriangle> SceneTriangles { get; init; } = Array.Empty<DebugTriangle>();

    public Camera3D CockpitCamera { get; init; }

    public Camera3D ExternalCamera { get; init; }

    public Camera3D WeaponCamera { get; init; }

    public PlayerMech PlayerMech { get; init; }

    public PlayerTargeting PlayerTargeting { get; init; }

#if DEBUG
    /// <summary>Raised by the DEBUG-only kill-hostiles shortcut.</summary>
    public event Action DestroyHostilesRequested;
#endif

    public override void _Ready()
    {
        AddDebugMenu();
    }

    public override void _Process(double delta)
    {
        if (!Current)
        {
            return;
        }

        var direction = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            direction -= GlobalBasis.Z;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            direction += GlobalBasis.Z;
        }

        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            direction -= GlobalBasis.X;
        }

        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            direction += GlobalBasis.X;
        }

        if (Input.IsPhysicalKeyPressed(Key.Q))
        {
            direction -= Vector3.Up;
        }

        if (Input.IsPhysicalKeyPressed(Key.E))
        {
            direction += Vector3.Up;
        }

        if (direction.IsZeroApprox())
        {
            return;
        }

        var speed = Input.IsPhysicalKeyPressed(Key.Shift) ? MoveSpeed * BoostMultiplier : MoveSpeed;
        GlobalPosition += direction.Normalized() * speed * (float)delta;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventKey { Pressed: false, CtrlPressed: true, Keycode: Key.F1 }:
                ToggleWireframe();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: false, Keycode: Key.F3 }:
                LogCamera();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: false, Keycode: Key.F4 }:
                CycleCamera();
                GetViewport().SetInputAsHandled();
                break;

#if DEBUG
            case InputEventKey { Pressed: false, Keycode: Key.K }:
                DestroyHostilesRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;
#endif

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Current:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion mouseMotion when Current && Input.MouseMode == Input.MouseModeEnum.Captured:
                RotateY(-mouseMotion.Relative.X * MouseSensitivity);
                Rotation = new Vector3(
                    Mathf.Clamp(Rotation.X - mouseMotion.Relative.Y * MouseSensitivity, -MaximumPitch, MaximumPitch),
                    Rotation.Y,
                    0.0f);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void AddDebugMenu()
    {
        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);

        var menuButton = new MenuButton
        {
            Text = "Debug",
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -82.0f,
            OffsetRight = -12.0f,
            OffsetTop = 12.0f,
            OffsetBottom = 43.0f
        };
        canvasLayer.AddChild(menuButton);

        m_debugMenu = menuButton.GetPopup();
        m_debugMenu.AddCheckItem("Wireframe (Ctrl+F1)", WireframeMenuItemId);
        m_debugMenu.AddItem("Log camera (F3)", LogCameraMenuItemId);
        m_debugMenu.AddItem("Cycle camera (F4)", CycleCameraMenuItemId);
#if DEBUG
        m_debugMenu.AddItem("Destroy hostiles (K)", DestroyHostilesMenuItemId);
#endif
        m_debugMenu.IdPressed += OnDebugMenuItemPressed;
    }

    private void ToggleWireframe()
    {
        m_wireframeEnabled = !m_wireframeEnabled;
        SetGroupVisible(SolidMeshGroup, !m_wireframeEnabled);
        SetGroupVisible(WireframeMeshGroup, m_wireframeEnabled);
        SetMenuItemChecked(WireframeMenuItemId, m_wireframeEnabled);
        GD.Print($"MechRewired: wireframe debug view {(m_wireframeEnabled ? "enabled" : "disabled")}.");
    }

    private void OnDebugMenuItemPressed(long id)
    {
        switch (id)
        {
            case WireframeMenuItemId:
                ToggleWireframe();
                break;

            case LogCameraMenuItemId:
                LogCamera();
                break;

            case CycleCameraMenuItemId:
                CycleCamera();
                break;

#if DEBUG
            case DestroyHostilesMenuItemId:
                DestroyHostilesRequested?.Invoke();
                break;
#endif
        }
    }

    private void CycleCamera()
    {
        string cameraName;
        if (CockpitCamera.Current)
        {
            CockpitCamera.Current = false;
            ExternalCamera.Current = true;
            Current = false;
            cameraName = "external";
        }
        else if (ExternalCamera.Current)
        {
            ActivatePlayerInspection();
            cameraName = "inspector";
        }
        else
        {
            ExternalCamera.Current = false;
            Current = false;
            CockpitCamera.Current = true;
            cameraName = "cockpit";
        }

        Input.MouseMode = Input.MouseModeEnum.Visible;
        GD.Print($"MechRewired: switched to {cameraName} camera.");
    }

    /// <summary>
    /// Activates the floating camera and frames the player from a repeatable front three-quarter view.
    /// </summary>
    public void ActivatePlayerInspection()
    {
        CockpitCamera.Current = false;
        ExternalCamera.Current = false;
        FramePlayerMech();
        Current = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void FramePlayerMech()
    {
        if (PlayerMech == null)
        {
            return;
        }

        var bounds = PlayerMech.WorldBounds;
        var center = bounds.GetCenter();
        var modelSize = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        var front = -PlayerMech.GlobalBasis.Z.Normalized();
        var side = PlayerMech.GlobalBasis.X.Normalized();
        var cameraDirection = (front + side * 0.42f).Normalized();
        var cameraPosition = center + cameraDirection * Math.Max(modelSize * 0.95f, 1.0f);
        cameraPosition.Y += modelSize * 0.12f;
        var target = center + Vector3.Up * modelSize * 0.04f;
        LookAtFromPosition(cameraPosition, target, Vector3.Up);
    }

    private void LogCamera()
    {
        var activeCamera = CockpitCamera.Current
            ? CockpitCamera
            : ExternalCamera.Current
                ? ExternalCamera
                : WeaponCamera.Current
                    ? WeaponCamera
                    : this;
        var forward = -activeCamera.GlobalBasis.Z.Normalized();
        var sourcePosition = MechWarriorCoordinateSystem.ToSourcePosition(activeCamera.GlobalPosition);
        var sourceDirection = MechWarriorCoordinateSystem.ToSourcePosition(forward);
        var rotationDegrees = MechWarriorCoordinateSystem.ToSourceRotation(activeCamera.GlobalRotationDegrees);
        GD.Print(
            $"MechRewired: {activeCamera.Name} MW2 position " +
            $"({sourcePosition.X:F2}, {sourcePosition.Y:F2}, {sourcePosition.Z:F2}); " +
            $"direction ({sourceDirection.X:F4}, {sourceDirection.Y:F4}, {sourceDirection.Z:F4}); " +
            $"rotation ({rotationDegrees.X:F2}, {rotationDegrees.Y:F2}, {rotationDegrees.Z:F2}) degrees.");
        LogSceneRay(activeCamera.GlobalPosition, forward);
        PlayerMech?.LogMovementState();
        PlayerTargeting?.LogTargetingState();
    }

    public void LogSceneRay(Vector3 origin, Vector3 direction)
    {
        if (!DebugTriangleRaycaster.TryFindNearest(
                SceneTriangles,
                origin,
                direction,
                out var nearestTriangle,
                out var nearestDistance))
        {
            GD.Print($"MechRewired: debug camera ray missed all {SceneTriangles.Count:N0} rendered triangles.");
            return;
        }

        var hitPosition = origin + direction * nearestDistance;
        var sourceHitPosition = MechWarriorCoordinateSystem.ToSourcePosition(hitPosition);
        GD.Print(
            $"MechRewired: debug camera ray hit {nearestTriangle.ResourcePath} object {nearestTriangle.ObjectId}, " +
            $"model {nearestTriangle.ModelIndex}, polygon {nearestTriangle.PolygonIndex} at " +
            $"({sourceHitPosition.X:F2}, {sourceHitPosition.Y:F2}, {sourceHitPosition.Z:F2}), distance {nearestDistance:F2}." +
            DescribeActorState(nearestTriangle));
    }

    private string DescribeActorState(DebugTriangle triangle)
    {
        var description = PlayerTargeting?.DescribeSceneTriangle(triangle);
        return string.IsNullOrWhiteSpace(description) ? string.Empty : $" ({description}).";
    }

    private void SetMenuItemChecked(int id, bool isChecked)
    {
        m_debugMenu.SetItemChecked(m_debugMenu.GetItemIndex(id), isChecked);
    }

    private void SetGroupVisible(string groupName, bool visible)
    {
        foreach (var node in GetTree().GetNodesInGroup(groupName))
        {
            if (node is MeshInstance3D meshInstance)
            {
                meshInstance.Visible = visible;
            }
        }
    }
}
