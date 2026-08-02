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
/// Provides stable attachment points for player locomotion, torso articulation and cameras.
/// </summary>
public partial class PlayerMech : Node3D
{
    public const uint ExteriorRenderLayer = 1u << 1;

    public PlayerMech()
    {
        Name = "PlayerMech";
        Legs = new Node3D { Name = "Legs" };
        Torso = new Node3D { Name = "Torso" };
        CockpitMount = new Node3D { Name = "CockpitMount" };
        AddChild(Legs);
        AddChild(Torso);
        Torso.AddChild(CockpitMount);
    }

    public Node3D Legs { get; }

    public Node3D Torso { get; }

    public Node3D CockpitMount { get; }

    public PlayerCockpitCamera CockpitCamera { get; private set; }

    public PlayerCockpit Cockpit { get; private set; }

    public Camera3D ExternalCamera { get; private set; }

    public Node3D GetPartParent(string partName) => partName switch
    {
        "Torso" or "Windshield" or "LeftArm" or "RightArm" => Torso,
        _ => Legs
    };

    public void ConfigureCameras(Aabb modelBounds)
    {
        var cockpitHeight = modelBounds.Position.Y + modelBounds.Size.Y - 0.8f;
        var cockpitFront = modelBounds.Position.Z - 0.15f;
        CockpitMount.Position = new Vector3(0.0f, cockpitHeight, cockpitFront);
        Cockpit = new PlayerCockpit();
        CockpitMount.AddChild(Cockpit);

        CockpitCamera = new PlayerCockpitCamera
        {
            Name = "CockpitCamera",
            Current = true,
            Near = 0.05f,
            Far = 8000.0f,
            Fov = 80.0f,
            CullMask = 1u | PlayerCockpit.RenderLayer
        };
        CockpitMount.AddChild(CockpitCamera);

        ExternalCamera = new Camera3D
        {
            Name = "ExternalCamera",
            Current = false,
            Far = 8000.0f,
            CullMask = 1u | ExteriorRenderLayer
        };
        AddChild(ExternalCamera);
        var target = modelBounds.GetCenter();
        var cameraPosition = new Vector3(0.0f, target.Y + modelBounds.Size.Y * 0.45f, modelBounds.Size.Z * 2.5f + 12.0f);
        ExternalCamera.Position = cameraPosition;
        ExternalCamera.LookAt(ToGlobal(target));
    }
}
