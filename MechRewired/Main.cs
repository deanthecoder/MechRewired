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
using MechRewired.Resources;

namespace MechRewired;

/// <summary>
/// Hosts the initial MechRewired Godot scene.
/// </summary>
/// <remarks>
/// Startup composition remains here while resource parsing and simulation live in the engine-independent core project.
/// </remarks>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        GD.Print("MechRewired: reactor online.");
        if (!CheckGameData())
        {
            return;
        }

        BuildPlaceholderScene();
    }

    private static bool CheckGameData()
    {
        try
        {
            var projectDirectory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
            var repositoryDirectory = projectDirectory.Parent ??
                                      throw new DirectoryNotFoundException("The MechRewired repository directory could not be resolved.");
            var dataDirectory = new DirectoryInfo(Path.Combine(repositoryDirectory.FullName, "local", "game-data"));
            var projectArchive = MechWarriorResourceCheck.CheckDosFiles(dataDirectory);
            GD.Print($"MechRewired: found {projectArchive.Name} ({projectArchive.Length:N0} bytes).");
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot load original game data: {exception.Message}");
            return false;
        }
    }

    private void BuildPlaceholderScene()
    {
        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("10141d"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("6f829b"),
                AmbientLightEnergy = 0.35f
            }
        };
        AddChild(environment);

        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55.0f, -25.0f, 0.0f),
            LightColor = new Color("f0c690"),
            LightEnergy = 1.2f,
            ShadowEnabled = true
        };
        AddChild(light);

        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh
            {
                Size = new Vector2(80.0f, 80.0f)
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("663d2d"),
                Roughness = 0.92f
            }
        };
        AddChild(ground);

        var marker = new MeshInstance3D
        {
            Position = new Vector3(0.0f, 1.5f, -8.0f),
            Mesh = new BoxMesh
            {
                Size = new Vector3(2.0f, 3.0f, 1.5f)
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("77856f"),
                Metallic = 0.55f,
                Roughness = 0.48f
            }
        };
        AddChild(marker);

        var camera = new Camera3D
        {
            Position = new Vector3(0.0f, 4.0f, 10.0f),
            Current = true
        };
        camera.LookAtFromPosition(camera.Position, new Vector3(0.0f, 1.5f, -8.0f));
        AddChild(camera);
    }
}
