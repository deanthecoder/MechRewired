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
    private const int UnshadedMenuItemId = 2;
    private const int LogCameraMenuItemId = 3;
    private const int CycleCameraMenuItemId = 4;
    private const float MoveSpeed = 120.0f;
    private const float BoostMultiplier = 6.0f;
    private const float MouseSensitivity = 0.002f;
    private const float MaximumPitch = Mathf.Pi / 2.0f - 0.01f;

    private bool m_wireframeEnabled;
    private PopupMenu m_debugMenu;

    public IReadOnlyList<DebugTriangle> SceneTriangles { get; init; } = Array.Empty<DebugTriangle>();

    public Camera3D CockpitCamera { get; init; }

    public Camera3D ExternalCamera { get; init; }

    public PlayerMech PlayerMech { get; init; }

    public PlayerTargeting PlayerTargeting { get; init; }

    public override void _Ready()
    {
        AddDebugMenu();
        GD.Print(
            "MechRewired: debug camera ready (click to capture; WASD move; Q/E descend/ascend; " +
            "Shift boosts; F1 wireframe; F2 unshaded; F3 logs camera/cockpit; F4 cycles cameras; " +
            "F5 VFX parameter; F6/F7 VFX adjust; F8 VFX reset; F9 VFX log; Escape releases).");
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
            case InputEventKey { Pressed: false, Keycode: Key.F1 }:
                ToggleWireframe();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: false, Keycode: Key.F2 }:
                ToggleUnshaded();
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
        m_debugMenu.AddCheckItem("Wireframe (F1)", WireframeMenuItemId);
        m_debugMenu.AddCheckItem("Unshaded (F2)", UnshadedMenuItemId);
        m_debugMenu.AddItem("Log camera (F3)", LogCameraMenuItemId);
        m_debugMenu.AddItem("Cycle camera (F4)", CycleCameraMenuItemId);
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

    private void ToggleUnshaded()
    {
        var viewport = GetViewport();
        var enabled = viewport.DebugDraw != Viewport.DebugDrawEnum.Unshaded;
        viewport.DebugDraw = enabled ? Viewport.DebugDrawEnum.Unshaded : Viewport.DebugDrawEnum.Disabled;
        SetMenuItemChecked(UnshadedMenuItemId, enabled);
        GD.Print($"MechRewired: unshaded debug view {(enabled ? "enabled" : "disabled")}.");
    }

    private void OnDebugMenuItemPressed(long id)
    {
        switch (id)
        {
            case WireframeMenuItemId:
                ToggleWireframe();
                break;

            case UnshadedMenuItemId:
                ToggleUnshaded();
                break;

            case LogCameraMenuItemId:
                LogCamera();
                break;

            case CycleCameraMenuItemId:
                CycleCamera();
                break;
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
            CockpitCamera.Current = false;
            ExternalCamera.Current = false;
            Current = true;
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

    private void LogCamera()
    {
        var activeCamera = CockpitCamera.Current
            ? CockpitCamera
            : ExternalCamera.Current
                ? ExternalCamera
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
        PlayerTargeting?.LogSelectedEnemyState();
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
            $"({sourceHitPosition.X:F2}, {sourceHitPosition.Y:F2}, {sourceHitPosition.Z:F2}), distance {nearestDistance:F2}.");
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
