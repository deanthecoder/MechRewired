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
    private const float MoveSpeed = 120.0f;
    private const float BoostMultiplier = 6.0f;
    private const float MouseSensitivity = 0.002f;
    private const float MaximumPitch = Mathf.Pi / 2.0f - 0.01f;

    private bool m_wireframeEnabled;
    private PopupMenu m_debugMenu;

    public IReadOnlyList<DebugTriangle> SceneTriangles { get; init; } = Array.Empty<DebugTriangle>();

    public override void _Ready()
    {
        AddDebugMenu();
        GD.Print(
            "MechRewired: debug camera ready (click to capture; WASD move; Q/E descend/ascend; " +
            "Shift boosts; F1 wireframe; F2 unshaded; F3 logs camera; Escape releases).");
    }

    public override void _Process(double delta)
    {
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

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion mouseMotion when Input.MouseMode == Input.MouseModeEnum.Captured:
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
            Position = new Vector2(12.0f, 12.0f)
        };
        canvasLayer.AddChild(menuButton);

        m_debugMenu = menuButton.GetPopup();
        m_debugMenu.AddCheckItem("Wireframe (F1)", WireframeMenuItemId);
        m_debugMenu.AddCheckItem("Unshaded (F2)", UnshadedMenuItemId);
        m_debugMenu.AddItem("Log camera (F3)", LogCameraMenuItemId);
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
        }
    }

    private void LogCamera()
    {
        var forward = -GlobalBasis.Z.Normalized();
        var sourcePosition = MechWarriorCoordinateSystem.ToSourcePosition(GlobalPosition);
        var sourceDirection = MechWarriorCoordinateSystem.ToSourcePosition(forward);
        var rotationDegrees = MechWarriorCoordinateSystem.ToSourceRotation(RotationDegrees);
        GD.Print(
            $"MechRewired: debug camera MW2 position ({sourcePosition.X:F2}, {sourcePosition.Y:F2}, {sourcePosition.Z:F2}); " +
            $"direction ({sourceDirection.X:F4}, {sourceDirection.Y:F4}, {sourceDirection.Z:F4}); " +
            $"rotation ({rotationDegrees.X:F2}, {rotationDegrees.Y:F2}, {rotationDegrees.Z:F2}) degrees.");
        LogSceneRay(GlobalPosition, forward);
    }

    public void LogSceneRay(Vector3 origin, Vector3 direction)
    {
        DebugTriangle nearestTriangle = null;
        var nearestDistance = float.PositiveInfinity;
        foreach (var triangle in SceneTriangles)
        {
            if (TryIntersectRay(origin, direction, triangle, out var distance) && distance < nearestDistance)
            {
                nearestTriangle = triangle;
                nearestDistance = distance;
            }
        }

        if (nearestTriangle == null)
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

    private static bool TryIntersectRay(
        Vector3 origin,
        Vector3 direction,
        DebugTriangle triangle,
        out float distance)
    {
        const float epsilon = 0.000001f;
        var edge1 = triangle.B - triangle.A;
        var edge2 = triangle.C - triangle.A;
        var perpendicular = direction.Cross(edge2);
        var determinant = edge1.Dot(perpendicular);
        if (Mathf.Abs(determinant) < epsilon)
        {
            distance = 0.0f;
            return false;
        }

        var inverseDeterminant = 1.0f / determinant;
        var originOffset = origin - triangle.A;
        var u = originOffset.Dot(perpendicular) * inverseDeterminant;
        if (u is < 0.0f or > 1.0f)
        {
            distance = 0.0f;
            return false;
        }

        var cross = originOffset.Cross(edge1);
        var v = direction.Dot(cross) * inverseDeterminant;
        if (v < 0.0f || u + v > 1.0f)
        {
            distance = 0.0f;
            return false;
        }

        distance = edge2.Dot(cross) * inverseDeterminant;
        return distance >= 0.0f;
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
