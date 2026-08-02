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
    private const float ImplicitGroundHeight = -0.25f;
    private const int SkyTopPaletteIndex = 224;
    private const int SkyHorizonPaletteIndex = 238;
    private const string PalettePath = "PAL/YELL_DA.COL";
    private const string LevelPath = "BWD/YELLWLD1.BWD";
    private const string PlanetPath = "BWD/YELLPLT1.BWD";
    private const string LevelAreaPrefix = "YELLARE";

    public override void _Ready()
    {
        GD.Print("MechRewired: reactor online.");
        if (!TryLoadGameData(out var archive, out var palette, out var modelParts, out var level, out var planet))
        {
            return;
        }

        try
        {
            BuildScene(archive, palette, modelParts, level, planet);
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
        out MechWarriorLevel level,
        out MechWarriorWorldFile planet)
    {
        archive = null;
        palette = null;
        modelParts = null;
        level = null;
        planet = null;
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
            var planetEntry = archive.GetEntry(PlanetPath);
            planet = MechWarriorWorldFile.Load(archive.ReadEntry(planetEntry));
            GD.Print(
                $"MechRewired: loaded {planetEntry.Path} (time {planet.TimeOfDay}; " +
                $"ambient {planet.Lighting?.AmbientLevel}; light type {planet.Lighting?.Type}; " +
                $"light at {planet.Lighting?.Position}; shade distance {planet.Lighting?.ShadeDistance:F2}; " +
                $"luma {planet.LuminosityTable}).");

            level = MechWarriorLevel.Load(
                archive,
                LevelPath,
                include => include.Name.StartsWith(LevelAreaPrefix, StringComparison.OrdinalIgnoreCase));
            foreach (var source in level.Sources)
            {
                GD.Print($"MechRewired: loaded {source.Entry.Path} ({source.ObjectCount} objects).");
            }

            foreach (var actor in level.Actors)
            {
                var description = string.IsNullOrWhiteSpace(actor.Description)
                    ? actor.Components[0].ModelEntry.Name
                    : actor.Description;
                var position = actor.Components[0].Transform.Translation;
                var activeComponents = string.Join(", ", actor.Components.Select(component => component.ModelEntry.Name));
                var destroyedComponents = actor.DestroyedComponents.Count == 0
                    ? "none"
                    : string.Join(", ", actor.DestroyedComponents.Select(component => component.ModelEntry.Name));
                GD.Print(
                    $"MechRewired: discovered actor {actor.SourceEntry.Path} object {actor.ObjectId} " +
                    $"'{description}' at ({position.X:F2}, {position.Y:F2}, {position.Z:F2}) " +
                    $"(health {actor.Health}; active [{activeComponents}]; destroyed [{destroyedComponents}]).");
            }

            GD.Print(
                $"MechRewired: assembled Pyre Light world ({level.Sources.Count} BWD resources, " +
                $"{level.TerrainObjects.Count} terrain objects, {level.SceneryObjects.Count} scenery objects, " +
                $"{level.DebrisObjects.Count} debris objects, {level.Actors.Count} actors).");
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
        MechWarriorLevel level,
        MechWarriorWorldFile planet)
    {
        var skyTopColor = ToGodotColor(palette[SkyTopPaletteIndex]);
        var skyHorizonColor = ToGodotColor(palette[SkyHorizonPaletteIndex]);
        var groundColor = ToGodotColor(palette[77]);
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = skyTopColor,
            SkyHorizonColor = skyHorizonColor,
            SkyCurve = 0.35f,
            GroundBottomColor = groundColor,
            GroundHorizonColor = skyHorizonColor,
            GroundCurve = 0.2f,
            SunAngleMax = 1.5f,
            SunCurve = 0.08f,
            UseDebanding = true
        };
        var ambientEnergy = Math.Clamp((planet.Lighting?.AmbientLevel ?? 128) / 256.0f, 0.35f, 1.0f);
        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky
                {
                    SkyMaterial = skyMaterial
                },
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = skyHorizonColor,
                AmbientLightEnergy = ambientEnergy
            }
        };
        AddChild(environment);

        var sunElevation = GetSunElevation(planet.TimeOfDay);
        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-sunElevation, -25.0f, 0.0f),
            LightColor = ToGodotColor(palette[17]),
            LightEnergy = 1.6f,
            ShadowEnabled = true
        };
        AddChild(light);
        GD.Print(
            $"MechRewired: rendered Pyre Light atmosphere (time {planet.TimeOfDay}; " +
            $"palette sky {SkyTopPaletteIndex}-{SkyHorizonPaletteIndex}; ambient {ambientEnergy:F2}; " +
            $"sun elevation {sunElevation:F1} degrees).");

        var levelRoot = new Node3D
        {
            Name = "PyreLight"
        };
        AddChild(levelRoot);

        var actorRoot = new Node3D
        {
            Name = "Actors"
        };
        AddChild(actorRoot);

        var meshCache = new Dictionary<string, IReadOnlyList<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
        var wireframeCache = new Dictionary<string, IReadOnlyList<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
        var modelCache = new Dictionary<string, IReadOnlyList<MechWarriorModel>>(StringComparer.OrdinalIgnoreCase);
        var terrainTopPoints = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        var terrainPaletteCounts = new Dictionary<byte, int>();
        var debugTriangles = new List<DebugTriangle>();
        var worldBounds = new Aabb();
        var hasWorldBounds = false;
        var renderedInstanceCount = 0;
        var renderedActorComponentCount = 0;
        var renderedDebrisCount = 0;
        Vector3? mechSpawn = null;
        var renderedObjects = level.StaticObjects
            .Concat(level.Actors.SelectMany(actor => actor.Components));
        foreach (var levelObject in renderedObjects)
        {
            if (!meshCache.TryGetValue(levelObject.ModelEntry.Path, out var meshes))
            {
                try
                {
                    var models = MechWarriorModel.LoadAll(archive.ReadEntry(levelObject.ModelEntry));
                    var highestDetailIndex = Enumerable.Range(0, models.Count)
                        .MaxBy(index => models[index].Polygons.Count);
                    var highestDetailModels = new[] { models[highestDetailIndex] };
                    modelCache.Add(levelObject.ModelEntry.Path, highestDetailModels);
                    meshes = highestDetailModels
                        .Select(model => MechWarriorModelMeshBuilder.Build(model, palette))
                        .ToArray();
                    wireframeCache.Add(
                        levelObject.ModelEntry.Path,
                        highestDetailModels.Select(MechWarriorModelMeshBuilder.BuildWireframe).ToArray());
                    var highestVertex = highestDetailModels
                        .SelectMany(model => model.Vertices)
                        .MaxBy(vertex => vertex.Position.Y);
                    terrainTopPoints.Add(
                        levelObject.ModelEntry.Path,
                        ToGodot(highestVertex.Position) * MechWarriorModelMeshBuilder.SourceUnitScale);
                    if (levelObject.ModelEntry.Name.StartsWith("T_", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var polygon in highestDetailModels.SelectMany(model => model.Polygons))
                        {
                            terrainPaletteCounts.TryGetValue(polygon.PaletteIndex, out var usageCount);
                            terrainPaletteCounts[polygon.PaletteIndex] = usageCount + polygon.VertexIndices.Count - 2;
                        }
                    }
                    GD.Print(
                        $"MechRewired: loaded {levelObject.ModelEntry.Path} ({models.Count} LODs; " +
                        $"rendering LOD {highestDetailIndex}, {highestDetailModels[0].Vertices.Count} vertices, " +
                        $"{highestDetailModels[0].Polygons.Count} polygons).");
                }
                catch (InvalidDataException exception)
                {
                    meshes = Array.Empty<ArrayMesh>();
                    modelCache.Add(levelObject.ModelEntry.Path, Array.Empty<MechWarriorModel>());
                    wireframeCache.Add(levelObject.ModelEntry.Path, Array.Empty<ArrayMesh>());
                    var objectPosition = levelObject.Transform.Translation;
                    GD.PushWarning(
                        $"MechRewired: skipped unsupported {levelObject.ModelEntry.Path} object {levelObject.Id} at " +
                        $"({objectPosition.X:F2}, {objectPosition.Y:F2}, {objectPosition.Z:F2}): {exception.Message}");
                }

                meshCache.Add(levelObject.ModelEntry.Path, meshes);
            }

            if (meshes.Count == 0)
            {
                continue;
            }

            var position = ToGodot(levelObject.Transform.Translation);
            if (levelObject.Kind == MechWarriorLevelObjectKind.Debris)
            {
                var lowestVertex = meshes.Min(mesh => mesh.GetAabb().Position.Y);
                position.Y = ImplicitGroundHeight - lowestVertex;
            }

            var objectRoot = new Node3D
            {
                Name = levelObject.ModelEntry.Name,
                Position = position,
                RotationDegrees = ToGodot(levelObject.Transform.RotationDegrees),
                Scale = ToGodot(levelObject.Transform.Scale)
            };
            var parent = levelObject.Kind == MechWarriorLevelObjectKind.Actor
                ? actorRoot
                : levelRoot;
            parent.AddChild(objectRoot);
            var wireframes = wireframeCache[levelObject.ModelEntry.Path];
            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                var solidInstance = new MeshInstance3D
                {
                    Mesh = meshes[meshIndex]
                };
                objectRoot.AddChild(solidInstance);
                solidInstance.AddToGroup(DebugCamera.SolidMeshGroup);

                var wireframeInstance = new MeshInstance3D
                {
                    Mesh = wireframes[meshIndex],
                    Visible = false
                };
                objectRoot.AddChild(wireframeInstance);
                wireframeInstance.AddToGroup(DebugCamera.WireframeMeshGroup);
            }

            AddDebugTriangles(debugTriangles, levelObject, objectRoot.Transform, modelCache[levelObject.ModelEntry.Path]);

            if (mechSpawn == null && levelObject.ModelEntry.Name.StartsWith("T_", StringComparison.OrdinalIgnoreCase))
            {
                mechSpawn = objectRoot.Transform * terrainTopPoints[levelObject.ModelEntry.Path];
            }

            renderedInstanceCount++;
            if (levelObject.Kind == MechWarriorLevelObjectKind.Actor)
            {
                renderedActorComponentCount++;
            }
            else if (levelObject.Kind == MechWarriorLevelObjectKind.Debris)
            {
                renderedDebrisCount++;
            }

            var pointBounds = new Aabb(position, Vector3.Zero);
            worldBounds = hasWorldBounds ? worldBounds.Merge(pointBounds) : pointBounds;
            hasWorldBounds = true;
        }

        GD.Print(
            $"MechRewired: rendered Pyre Light world ({renderedInstanceCount} instances, " +
            $"{renderedActorComponentCount} active actor components, {renderedDebrisCount} ground-settled debris objects, " +
            $"{meshCache.Count} unique models).");

        AddImplicitGround(levelRoot, worldBounds, terrainPaletteCounts, palette, debugTriangles);

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
            modelInstance.AddToGroup(DebugCamera.SolidMeshGroup);

            var wireframeInstance = new MeshInstance3D
            {
                Name = $"{definition.Name}Wireframe",
                Mesh = MechWarriorModelMeshBuilder.BuildWireframe(model),
                Position = partPosition,
                Visible = false
            };
            mech.AddChild(wireframeInstance);
            wireframeInstance.AddToGroup(DebugCamera.WireframeMeshGroup);

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
        var camera = new DebugCamera
        {
            Position = target + cameraDirection * cameraDistance,
            Current = true,
            Far = Math.Max(cameraDistance * 4.0f, 4000.0f),
            SceneTriangles = debugTriangles.AsReadOnly()
        };
        camera.LookAtFromPosition(camera.Position, target);
        AddChild(camera);
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 vector) => new(vector.X, vector.Y, vector.Z);

    private static Color ToGodotColor(DTC.Core.Rgb color) =>
        new(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f);

    private static float GetSunElevation(int? militaryTime)
    {
        if (!militaryTime.HasValue)
        {
            return 45.0f;
        }

        var hours = militaryTime.Value / 100;
        var minutes = militaryTime.Value % 100;
        var solarTime = hours + minutes / 60.0f;
        var daylightProgress = Math.Clamp((solarTime - 6.0f) / 12.0f, 0.0f, 1.0f);
        return 10.0f + MathF.Sin(daylightProgress * MathF.PI) * 55.0f;
    }

    private static void AddDebugTriangles(
        ICollection<DebugTriangle> triangles,
        MechWarriorLevelObject levelObject,
        Transform3D transform,
        IReadOnlyList<MechWarriorModel> models)
    {
        for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
        {
            var model = models[modelIndex];
            for (var polygonIndex = 0; polygonIndex < model.Polygons.Count; polygonIndex++)
            {
                var polygon = model.Polygons[polygonIndex];
                for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
                {
                    triangles.Add(new DebugTriangle(
                        levelObject.ModelEntry.Path,
                        levelObject.Id,
                        modelIndex,
                        polygonIndex,
                        TransformVertex(transform, model.Vertices[polygon.VertexIndices[0]]),
                        TransformVertex(transform, model.Vertices[polygon.VertexIndices[triangleIndex]]),
                        TransformVertex(transform, model.Vertices[polygon.VertexIndices[triangleIndex + 1]])));
                }
            }
        }
    }

    private static Vector3 TransformVertex(Transform3D transform, MechWarriorModelVertex vertex) =>
        transform * (ToGodot(vertex.Position) * MechWarriorModelMeshBuilder.SourceUnitScale);

    private static void AddImplicitGround(
        Node3D levelRoot,
        Aabb worldBounds,
        IReadOnlyDictionary<byte, int> terrainPaletteCounts,
        MechWarriorPalette palette,
        ICollection<DebugTriangle> debugTriangles)
    {
        const float margin = 1000.0f;
        var paletteIndex = terrainPaletteCounts.MaxBy(entry => entry.Value).Key;
        var color = palette[paletteIndex];
        var center = worldBounds.GetCenter();
        var size = new Vector2(worldBounds.Size.X + margin * 2.0f, worldBounds.Size.Z + margin * 2.0f);
        var ground = new MeshInstance3D
        {
            Name = "ImplicitGround",
            Position = new Vector3(center.X, ImplicitGroundHeight, center.Z),
            Mesh = new PlaneMesh
            {
                Size = size,
                Material = new StandardMaterial3D
                {
                    AlbedoColor = new Color(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f),
                    Roughness = 0.95f
                }
            }
        };
        levelRoot.AddChild(ground);
        ground.AddToGroup(DebugCamera.SolidMeshGroup);

        var minimum = new Vector3(
            center.X - size.X / 2.0f,
            ImplicitGroundHeight,
            center.Z - size.Y / 2.0f);
        var maximum = new Vector3(
            center.X + size.X / 2.0f,
            ImplicitGroundHeight,
            center.Z + size.Y / 2.0f);
        var cornerA = new Vector3(minimum.X, ImplicitGroundHeight, minimum.Z);
        var cornerB = new Vector3(maximum.X, ImplicitGroundHeight, minimum.Z);
        var cornerC = new Vector3(maximum.X, ImplicitGroundHeight, maximum.Z);
        var cornerD = new Vector3(minimum.X, ImplicitGroundHeight, maximum.Z);
        debugTriangles.Add(new DebugTriangle("IMPLICIT/GROUND", -1, 0, 0, cornerA, cornerB, cornerC));
        debugTriangles.Add(new DebugTriangle("IMPLICIT/GROUND", -1, 0, 1, cornerA, cornerC, cornerD));
        GD.Print(
            $"MechRewired: added implicit ground plane at Y={ImplicitGroundHeight:F2} " +
            $"({size.X:F0} × {size.Y:F0}, " +
            $"palette index {paletteIndex}).");
    }
}
