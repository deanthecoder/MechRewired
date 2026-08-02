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
    private const string PalettePath = "PAL/YELL_DA.COL";
    private const string LevelPath = "BWD/YELLWLD1.BWD";
    private const string LevelAreaPrefix = "YELLARE";

    public override void _Ready()
    {
        GD.Print("MechRewired: reactor online.");
        if (!TryLoadGameData(out var archive, out var palette, out var modelParts, out var level))
        {
            return;
        }

        try
        {
            BuildScene(archive, palette, modelParts, level);
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot render the scene: {exception.Message}");
        }
    }

    private static bool TryLoadGameData(
        out MechWarriorProjectArchive archive,
        out MechWarriorPalette palette,
        out IReadOnlyList<(MechWarriorModelPartDefinition Definition, MechWarriorModel Model)> modelParts,
        out MechWarriorLevel level)
    {
        archive = null;
        palette = null;
        modelParts = null;
        level = null;
        try
        {
            var projectDirectory = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
            var repositoryDirectory = projectDirectory.Parent ??
                                      throw new DirectoryNotFoundException("The MechRewired repository directory could not be resolved.");
            var dataDirectory = new DirectoryInfo(Path.Combine(repositoryDirectory.FullName, "local", "game-data"));
            var projectArchive = MechWarriorResourceCheck.CheckDosFiles(dataDirectory);
            archive = MechWarriorProjectArchive.Open(projectArchive);
            GD.Print(
                $"MechRewired: indexed {archive.Entries.Count:N0} resources from {projectArchive.Name} " +
                $"({projectArchive.Length:N0} bytes).");

            var paletteEntry = archive.GetEntry(PalettePath);
            palette = MechWarriorPalette.Load(archive.ReadEntry(paletteEntry));
            GD.Print($"MechRewired: loaded {paletteEntry.Path} ({palette.Colors.Count} colors).");

            var loadedParts = new List<(MechWarriorModelPartDefinition, MechWarriorModel)>();
            foreach (var definition in TimberWolfModelDefinition.Parts)
            {
                var modelEntry = archive.GetEntry(definition.ResourcePath);
                var model = MechWarriorModel.Load(archive.ReadEntry(modelEntry));
                loadedParts.Add((definition, model));
                GD.Print(
                    $"MechRewired: loaded {modelEntry.Path} (subtype {model.Subtype}, " +
                    $"{model.Vertices.Count} vertices, {model.Polygons.Count} polygons).");
            }

            modelParts = loadedParts.AsReadOnly();
            level = MechWarriorLevel.Load(
                archive,
                LevelPath,
                include => include.Name.StartsWith(LevelAreaPrefix, StringComparison.OrdinalIgnoreCase));
            foreach (var source in level.Sources)
            {
                GD.Print($"MechRewired: loaded {source.Entry.Path} ({source.ObjectCount} objects).");
            }

            GD.Print(
                $"MechRewired: assembled Pyre Light world ({level.Sources.Count} BWD resources, " +
                $"{level.Objects.Count} positioned objects).");
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot load original game data: {exception.Message}");
            return false;
        }
    }

    private void BuildScene(
        MechWarriorProjectArchive archive,
        MechWarriorPalette palette,
        IReadOnlyList<(MechWarriorModelPartDefinition Definition, MechWarriorModel Model)> modelParts,
        MechWarriorLevel level)
    {
        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("10141d"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b8c5d6"),
                AmbientLightEnergy = 0.8f
            }
        };
        AddChild(environment);

        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55.0f, -25.0f, 0.0f),
            LightColor = new Color("fff2d2"),
            LightEnergy = 1.6f,
            ShadowEnabled = true
        };
        AddChild(light);

        var levelRoot = new Node3D
        {
            Name = "PyreLight"
        };
        AddChild(levelRoot);

        var meshCache = new Dictionary<string, IReadOnlyList<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
        var terrainTopPoints = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        var worldBounds = new Aabb();
        var hasWorldBounds = false;
        var renderedInstanceCount = 0;
        Vector3? mechSpawn = null;
        foreach (var levelObject in level.Objects)
        {
            if (!meshCache.TryGetValue(levelObject.ModelEntry.Path, out var meshes))
            {
                try
                {
                    var models = MechWarriorModel.LoadAll(archive.ReadEntry(levelObject.ModelEntry));
                    meshes = models.Select(model => MechWarriorModelMeshBuilder.Build(model, palette)).ToArray();
                    var highestVertex = models
                        .SelectMany(model => model.Vertices)
                        .MaxBy(vertex => vertex.Position.Y);
                    terrainTopPoints.Add(
                        levelObject.ModelEntry.Path,
                        ToGodot(highestVertex.Position) * MechWarriorModelMeshBuilder.SourceUnitScale);
                    GD.Print(
                        $"MechRewired: loaded {levelObject.ModelEntry.Path} ({models.Count} model objects, " +
                        $"{models.Sum(model => model.Vertices.Count)} vertices, " +
                        $"{models.Sum(model => model.Polygons.Count)} polygons).");
                }
                catch (InvalidDataException exception)
                {
                    meshes = Array.Empty<ArrayMesh>();
                    GD.PushWarning($"MechRewired: skipped unsupported {levelObject.ModelEntry.Path}: {exception.Message}");
                }

                meshCache.Add(levelObject.ModelEntry.Path, meshes);
            }

            if (meshes.Count == 0)
            {
                continue;
            }

            var position = ToGodot(levelObject.Transform.Translation);
            var objectRoot = new Node3D
            {
                Name = levelObject.ModelEntry.Name,
                Position = position,
                RotationDegrees = ToGodot(levelObject.Transform.RotationDegrees),
                Scale = ToGodot(levelObject.Transform.Scale)
            };
            levelRoot.AddChild(objectRoot);
            foreach (var mesh in meshes)
            {
                objectRoot.AddChild(new MeshInstance3D
                {
                    Mesh = mesh
                });
            }

            if (mechSpawn == null && levelObject.ModelEntry.Name.StartsWith("T_", StringComparison.OrdinalIgnoreCase))
            {
                mechSpawn = objectRoot.Transform * terrainTopPoints[levelObject.ModelEntry.Path];
            }

            renderedInstanceCount++;
            var pointBounds = new Aabb(position, Vector3.Zero);
            worldBounds = hasWorldBounds ? worldBounds.Merge(pointBounds) : pointBounds;
            hasWorldBounds = true;
        }

        GD.Print(
            $"MechRewired: rendered Pyre Light world ({renderedInstanceCount} instances, " +
            $"{meshCache.Count} unique models).");

        var mech = new Node3D
        {
            Name = "TimberWolf"
        };
        AddChild(mech);

        var bounds = new Aabb();
        var hasBounds = false;
        var triangleCount = 0;
        var vertexCount = 0;
        foreach (var (definition, model) in modelParts)
        {
            var renderMesh = MechWarriorModelMeshBuilder.Build(model, palette);
            var partPosition = ToGodot(definition.Translation);
            var modelInstance = new MeshInstance3D
            {
                Name = definition.Name,
                Mesh = renderMesh,
                Position = partPosition
            };
            mech.AddChild(modelInstance);

            var partBounds = renderMesh.GetAabb();
            partBounds.Position += partPosition;
            bounds = hasBounds ? bounds.Merge(partBounds) : partBounds;
            hasBounds = true;
            vertexCount += model.Vertices.Count;
            triangleCount += model.Polygons.Sum(polygon => polygon.VertexIndices.Count - 2);
        }

        var mechPosition = mechSpawn ?? Vector3.Zero;
        mech.Position = mechPosition + new Vector3(0.0f, -bounds.Position.Y, 0.0f);

        GD.Print(
            $"MechRewired: assembled Timber Wolf ({modelParts.Count} parts, {vertexCount} source vertices, " +
            $"{triangleCount} triangles, scale {MechWarriorModelMeshBuilder.SourceUnitScale}).");

        var target = mech.Position + bounds.GetCenter();
        var modelSize = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        var cameraDistance = Math.Max(modelSize * 3.0f, 1.0f);
        var cameraDirection = new Vector3(0.75f, 0.4f, 1.0f).Normalized();
        var camera = new Camera3D
        {
            Position = target + cameraDirection * cameraDistance,
            Current = true,
            Far = Math.Max(cameraDistance * 4.0f, 4000.0f)
        };
        camera.LookAtFromPosition(camera.Position, target);
        AddChild(camera);
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 vector) => new(vector.X, vector.Y, vector.Z);
}
