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
    private const string ModelPath = "POLY/BM1_HIPS.WTB";
    private const string PalettePath = "PAL/BROWN_DA.COL";

    public override void _Ready()
    {
        GD.Print("MechRewired: reactor online.");
        if (!TryLoadGameData(out var palette, out var model))
        {
            return;
        }

        try
        {
            BuildModelScene(model, palette);
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot render the model: {exception.Message}");
        }
    }

    private static bool TryLoadGameData(out MechWarriorPalette palette, out MechWarriorModel model)
    {
        palette = null;
        model = null;
        try
        {
            var projectDirectory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
            var repositoryDirectory = projectDirectory.Parent ??
                                      throw new DirectoryNotFoundException("The MechRewired repository directory could not be resolved.");
            var dataDirectory = new DirectoryInfo(Path.Combine(repositoryDirectory.FullName, "local", "game-data"));
            var projectArchive = MechWarriorResourceCheck.CheckDosFiles(dataDirectory);
            var archive = MechWarriorProjectArchive.Open(projectArchive);
            GD.Print(
                $"MechRewired: indexed {archive.Entries.Count:N0} resources from {projectArchive.Name} " +
                $"({projectArchive.Length:N0} bytes).");

            var paletteEntry = archive.GetEntry(PalettePath);
            palette = MechWarriorPalette.Load(archive.ReadEntry(paletteEntry));
            GD.Print($"MechRewired: loaded {paletteEntry.Path} ({palette.Colors.Count} colors).");

            var modelEntry = archive.GetEntry(ModelPath);
            model = MechWarriorModel.Load(archive.ReadEntry(modelEntry));
            GD.Print(
                $"MechRewired: loaded {modelEntry.Path} (subtype {model.Subtype}, {model.Vertices.Count} vertices, " +
                $"{model.Polygons.Count} polygons).");
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot load original game data: {exception.Message}");
            return false;
        }
    }

    private void BuildModelScene(MechWarriorModel model, MechWarriorPalette palette)
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
                AlbedoColor = new Color("343940"),
                Roughness = 0.92f
            }
        };
        AddChild(ground);

        var renderMesh = MechWarriorModelMeshBuilder.Build(model, palette);
        var modelInstance = new MeshInstance3D
        {
            Mesh = renderMesh
        };
        var bounds = renderMesh.GetAabb();
        modelInstance.Position = new Vector3(0.0f, -bounds.Position.Y, 0.0f);
        AddChild(modelInstance);

        var triangleCount = model.Polygons.Sum(polygon => polygon.VertexIndices.Count - 2);
        GD.Print(
            $"MechRewired: built render mesh for {ModelPath} ({triangleCount} triangles, " +
            $"scale {MechWarriorModelMeshBuilder.SourceUnitScale}).");

        var target = modelInstance.Position + bounds.GetCenter();
        var modelSize = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        var cameraDistance = Math.Max(modelSize * 1.3f, 1.0f);
        var cameraDirection = new Vector3(1.0f, 0.55f, 1.35f).Normalized();
        var camera = new Camera3D
        {
            Position = target + cameraDirection * cameraDistance,
            Current = true
        };
        camera.LookAtFromPosition(camera.Position, target);
        AddChild(camera);
    }
}
