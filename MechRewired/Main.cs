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
using MechRewired.Missions;
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
    private const float ImplicitGroundHeight = 0.15f;
    private const int SkyTopPaletteIndex = 224;
    private const int SkyHorizonPaletteIndex = 238;
    private const float DirectionalShadowDistance = 400.0f;
    private const float FallbackSunAzimuthDegrees = 25.0f;
    private const int GeneralIlluminationLevel = 12;
    private const int ObjectIlluminationLevel = 8;
    private const byte MaximumTexturedMechMaterialIndex = 63;
    private const float DefaultFogDistance = 1200.0f;
    private const float MinimumFogDistance = 300.0f;
    private const float MaximumFogDistance = 5000.0f;
    private const float FogDistanceStep = 100.0f;
    private const string PalettePath = "PAL/YELL_DA.COL";
    private const string LevelPath = "BWD/YELLWLD1.BWD";
    private const string PlanetPath = "BWD/YELLPLT1.BWD";
    private const string ScenarioPath = "BWD/YELLSCN1.BWD";
    private const string PlayerStartPath = "BWD/YELLST01.BWD";
    private const string PlayerMechPath = "MEK/TBR00STD.MEK";
    private const string LevelAreaPrefix = "YELLARE";
    private static readonly string[] ExplosionDebrisPaths =
    [
        "POLY/CHUNKER1.WTB",
        "POLY/CHUNKER2.WTB",
        "POLY/CHUNKLET.WTB"
    ];

    private Godot.Environment m_environment;
    private float m_fogDistance = DefaultFogDistance;

    public override void _Ready()
    {
        GD.Print("MechRewired: reactor online.");
        if (!TryLoadGameData(
                out var archive,
                out var palette,
                out var modelParts,
                out var level,
                out var planet,
                out var luminosityTable,
                out var playerStart,
                out var navigationPoints,
                out var missionDefinition,
                out var playerMechDefinition))
        {
            return;
        }

        try
        {
            BuildScene(
                archive,
                palette,
                modelParts,
                level,
                planet,
                luminosityTable,
                playerStart,
                navigationPoints,
                missionDefinition,
                playerMechDefinition);
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired cannot render the scene: {exception.Message}");
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            return;
        }

        var adjustment = keyEvent.Keycode switch
        {
            Key.Bracketleft => -FogDistanceStep,
            Key.Bracketright => FogDistanceStep,
            _ => 0.0f
        };
        if (adjustment == 0.0f)
        {
            return;
        }

        SetFogDistance(m_fogDistance + adjustment, true);
        GetViewport().SetInputAsHandled();
    }

    private static bool TryLoadGameData(
        out MechWarriorProjectArchive archive,
        out MechWarriorPalette palette,
        out IReadOnlyList<(MechWarriorModelPartDefinition Definition, MechWarriorModel Model)> modelParts,
        out MechWarriorLevel level,
        out MechWarriorWorldFile planet,
        out MechWarriorLuminosityTable luminosityTable,
        out MechWarriorWorldNavPoint playerStart,
        out IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        out MissionDefinition missionDefinition,
        out MechWarriorMechFile playerMechDefinition)
    {
        archive = null;
        palette = null;
        modelParts = null;
        level = null;
        planet = null;
        luminosityTable = null;
        playerStart = null;
        navigationPoints = null;
        missionDefinition = null;
        playerMechDefinition = null;
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
            var playerMechEntry = archive.GetEntry(PlayerMechPath);
            playerMechDefinition = MechWarriorMechFile.Load(archive.ReadEntry(playerMechEntry));
            GD.Print(
                $"MechRewired: loaded {playerMechEntry.Path} ({playerMechDefinition.Tonnage} tons; " +
                $"{playerMechDefinition.WalkingMovementPoints} walking movement points; " +
                $"{playerMechDefinition.CruisingSpeedKph:F1} km/h cruise; " +
                $"{playerMechDefinition.MaximumSpeedKph:F1} km/h maximum).");
            var planetEntry = archive.GetEntry(PlanetPath);
            planet = MechWarriorWorldFile.Load(archive.ReadEntry(planetEntry));
            GD.Print(
                $"MechRewired: loaded {planetEntry.Path} (time {planet.TimeOfDay}; " +
                $"ambient {planet.Lighting?.AmbientLevel}; light type {planet.Lighting?.Type}; " +
                $"light at {planet.Lighting?.Position}; shade distance {planet.Lighting?.ShadeDistance:F2}; " +
                $"view distance {planet.ViewDistance:F2}; luma {planet.LuminosityTable}).");
            var luminosityEntry = archive.GetEntry($"LUMA/{planet.LuminosityTable}.TBL");
            luminosityTable = MechWarriorLuminosityTable.Load(archive.ReadEntry(luminosityEntry));
            GD.Print(
                $"MechRewired: loaded {luminosityEntry.Path} " +
                $"({MechWarriorLuminosityTable.LevelCount} illumination levels).");

            var playerStartEntry = archive.GetEntry(PlayerStartPath);
            var playerStartWorld = MechWarriorWorldFile.Load(archive.ReadEntry(playerStartEntry));
            if (playerStartWorld.NavPoints.Count != 1)
            {
                throw new InvalidDataException(
                    $"{playerStartEntry.Path} contains {playerStartWorld.NavPoints.Count} deployment points; expected one.");
            }

            playerStart = playerStartWorld.NavPoints[0];
            GD.Print(
                $"MechRewired: loaded {playerStartEntry.Path} player deployment " +
                $"at ({playerStart.Position.X:F2}, {playerStart.Position.Y:F2}, {playerStart.Position.Z:F2}), " +
                $"heading {playerStart.StartingAngle} degrees (group {playerStart.GroupId}; " +
                $"radius {playerStart.Radius}; action 0x{playerStart.ActionFlags:X4}; " +
                $"'{playerStart.Description}').");

            var scenarioEntry = archive.GetEntry(ScenarioPath);
            var scenario = MechWarriorWorldFile.Load(archive.ReadEntry(scenarioEntry));
            missionDefinition = LoadMissionDefinition(scenarioEntry, scenario);
            navigationPoints = LoadMissionNavigationPoints(archive, scenarioEntry, scenario);

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

    private static MissionDefinition LoadMissionDefinition(
        MechWarriorProjectEntry scenarioEntry,
        MechWarriorWorldFile scenario)
    {
        var missionTable = scenario.MissionTables.SingleOrDefault(table => table.Index == 0) ??
                           throw new InvalidDataException(
                               $"{scenarioEntry.Path} does not contain the default MTBL mission table.");
        var definition = MissionDefinition.FromMissionTable(missionTable);
        if (definition.Objectives.Count == 0)
        {
            throw new InvalidDataException(
                $"{scenarioEntry.Path} MTBL {missionTable.Index} contains no supported objectives.");
        }

        GD.Print(
            $"MechRewired: decoded {scenario.MissionTables.Count} mission tables from {scenarioEntry.Path}; " +
            $"default table has {missionTable.Entries.Count} records and " +
            $"{definition.Objectives.Count} supported objectives.");
        foreach (var objective in definition.Objectives)
        {
            GD.Print(
                $"MechRewired: extracted {(objective.IsOptional ? "optional" : "required")} " +
                $"{objective.Kind} objective '{objective.Description}' targeting BWD/{objective.TargetResourceName}.BWD " +
                $"(report {objective.SuccessReport.Name}; prerequisites " +
                $"{(objective.PrerequisiteIds.Count == 0 ? "none" : string.Join(", ", objective.PrerequisiteIds))}).");
        }

        return definition;
    }

    private static IReadOnlyList<MechWarriorMissionNavigationPoint> LoadMissionNavigationPoints(
        MechWarriorProjectArchive archive,
        MechWarriorProjectEntry scenarioEntry,
        MechWarriorWorldFile scenario)
    {
        var navigationPoints = new List<MechWarriorMissionNavigationPoint>();
        foreach (var include in scenario.Includes.Where(include =>
                     include.Name.StartsWith("YELLNAV", StringComparison.OrdinalIgnoreCase)))
        {
            var navigationEntry = archive.GetEntry("BWD", include.ResourceIndex);
            var navigationWorld = MechWarriorWorldFile.Load(
                archive.ReadEntry(navigationEntry),
                include.Transform);
            if (navigationWorld.NavPoints.Count != 1)
            {
                throw new InvalidDataException(
                    $"{navigationEntry.Path} contains {navigationWorld.NavPoints.Count} navigation points; expected one.");
            }

            var navigationPoint = navigationWorld.NavPoints[0];
            if (!navigationPoint.Targetable || string.IsNullOrWhiteSpace(navigationPoint.Description))
            {
                throw new InvalidDataException(
                    $"{navigationEntry.Path} does not contain a named, targetable navigation point.");
            }

            navigationPoints.Add(new MechWarriorMissionNavigationPoint(
                Path.GetFileNameWithoutExtension(navigationEntry.Name),
                navigationPoint));
            GD.Print(
                $"MechRewired: loaded {navigationEntry.Path} navigation point " +
                $"'{navigationPoint.Description}' at ({navigationPoint.Position.X:F2}, " +
                $"{navigationPoint.Position.Y:F2}, {navigationPoint.Position.Z:F2}) " +
                $"(radius {navigationPoint.Radius}m; action 0x{navigationPoint.ActionFlags:X4}).");
        }

        if (navigationPoints.Count == 0)
        {
            throw new InvalidDataException($"{scenarioEntry.Path} contains no named navigation includes.");
        }

        GD.Print(
            $"MechRewired: loaded {navigationPoints.Count} mission navigation points from {scenarioEntry.Path}.");
        return navigationPoints.AsReadOnly();
    }

    private void BuildScene(
        MechWarriorProjectArchive archive,
        MechWarriorPalette palette,
        IReadOnlyList<(MechWarriorModelPartDefinition Definition, MechWarriorModel Model)> modelParts,
        MechWarriorLevel level,
        MechWarriorWorldFile planet,
        MechWarriorLuminosityTable luminosityTable,
        MechWarriorWorldNavPoint playerStart,
        IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        MissionDefinition missionDefinition,
        MechWarriorMechFile playerMechDefinition)
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
        m_environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = skyMaterial
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = skyHorizonColor,
            AmbientLightEnergy = ambientEnergy,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = skyHorizonColor,
            FogLightEnergy = 1.0f,
            FogDensity = 1.0f,
            FogDepthCurve = 1.0f,
            FogSkyAffect = 0.0f
        };
        SetFogDistance(planet.ViewDistance ?? DefaultFogDistance, false);
        var environment = new WorldEnvironment
        {
            Environment = m_environment
        };
        AddChild(environment);

        var sunElevation = GetSunElevation(planet.TimeOfDay);
        var light = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-sunElevation, FallbackSunAzimuthDegrees, 0.0f),
            LightColor = ToGodotColor(palette[17]),
            LightEnergy = 1.6f,
            ShadowEnabled = true,
            ShadowOpacity = 0.9f,
            ShadowBlur = 0.6f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = DirectionalShadowDistance,
            DirectionalShadowSplit1 = 0.04f,
            DirectionalShadowSplit2 = 0.12f,
            DirectionalShadowSplit3 = 0.35f,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowFadeStart = 0.9f,
            DirectionalShadowPancakeSize = 5.0f
        };
        AddChild(light);
        GD.Print(
            $"MechRewired: rendered Pyre Light atmosphere (time {planet.TimeOfDay}; " +
            $"palette sky {SkyTopPaletteIndex}-{SkyHorizonPaletteIndex}; ambient {ambientEnergy:F2}; " +
            $"sun elevation {sunElevation:F1} degrees at {FallbackSunAzimuthDegrees:F0}-degree " +
            $"mirrored fallback azimuth; 8192px 32-bit directional shadows to " +
            $"{DirectionalShadowDistance:F0}m at 90% opacity; depth fog " +
            $"{m_environment.FogDepthBegin:F0}-{m_environment.FogDepthEnd:F0}m).");

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
        var explosionDebrisMeshes = ExplosionDebrisPaths.SelectMany(path =>
        {
            var entry = archive.GetEntry(path);
            var models = MechWarriorModel.LoadAll(archive.ReadEntry(entry));
            var meshes = models.Select(model => MechWarriorModelMeshBuilder.Build(
                    model,
                    palette,
                    luminosityTable,
                    ObjectIlluminationLevel))
                .ToArray();
            GD.Print(
                $"MechRewired: loaded {entry.Path} for explosion debris " +
                $"({models.Count} pieces, {models.Sum(model => model.Vertices.Count)} vertices, " +
                $"{models.Sum(model => model.Polygons.Count)} polygons).");
            return meshes;
        }).ToArray();
        var battlefieldActors = level.Actors
            .Select(actor => new BattlefieldActor(actor, explosionDebrisMeshes))
            .ToArray();
        var actorComponents = new Dictionary<
            (string SourcePath, int ObjectId),
            (BattlefieldActor Actor, bool Destroyed)>();
        foreach (var battlefieldActor in battlefieldActors)
        {
            actorRoot.AddChild(battlefieldActor);
            foreach (var component in battlefieldActor.Definition.Components)
            {
                actorComponents.Add(
                    (component.SourceEntry.Path, component.Id),
                    (battlefieldActor, false));
            }

            foreach (var component in battlefieldActor.Definition.DestroyedComponents)
            {
                actorComponents.Add(
                    (component.SourceEntry.Path, component.Id),
                    (battlefieldActor, true));
            }
        }

        var meshCache = new Dictionary<string, IReadOnlyList<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
        var wireframeCache = new Dictionary<string, IReadOnlyList<ArrayMesh>>(StringComparer.OrdinalIgnoreCase);
        var modelCache = new Dictionary<string, IReadOnlyList<MechWarriorModel>>(StringComparer.OrdinalIgnoreCase);
        var terrainPaletteCounts = new Dictionary<byte, int>();
        var debugTriangles = new List<DebugTriangle>();
        var worldBounds = new Aabb();
        var hasWorldBounds = false;
        var renderedInstanceCount = 0;
        var renderedActorComponentCount = 0;
        var renderedDebrisCount = 0;
        var settledActors = new HashSet<BattlefieldActor>();
        var renderedObjects = level.StaticObjects
            .Concat(level.Actors.SelectMany(actor => actor.Components))
            .Concat(level.Actors.SelectMany(actor => actor.DestroyedComponents));
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
                    var illuminationLevel = levelObject.ModelEntry.Name.StartsWith(
                        "T_",
                        StringComparison.OrdinalIgnoreCase)
                        ? GeneralIlluminationLevel
                        : ObjectIlluminationLevel;
                    meshes = highestDetailModels
                        .Select(model => MechWarriorModelMeshBuilder.Build(
                            model,
                            palette,
                            luminosityTable,
                            illuminationLevel))
                        .ToArray();
                    wireframeCache.Add(
                        levelObject.ModelEntry.Path,
                        highestDetailModels.Select(MechWarriorModelMeshBuilder.BuildWireframe).ToArray());
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

            var position = MechWarriorCoordinateSystem.ToGodotPosition(levelObject.Transform.Translation);
            if (levelObject.Kind == MechWarriorLevelObjectKind.Debris)
            {
                var lowestVertex = meshes.Min(mesh => mesh.GetAabb().Position.Y);
                position.Y = ImplicitGroundHeight - lowestVertex;
            }

            var objectRoot = new Node3D
            {
                Name = levelObject.ModelEntry.Name,
                Position = position,
                RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(levelObject.Transform.RotationDegrees),
                Scale = MechWarriorCoordinateSystem.ToGodotScale(levelObject.Transform.Scale)
            };
            var isDestroyedRepresentation = false;
            BattlefieldActor battlefieldActor = null;
            if (levelObject.Kind == MechWarriorLevelObjectKind.Actor &&
                actorComponents.TryGetValue(
                    (levelObject.SourceEntry.Path, levelObject.Id),
                    out var actorComponent))
            {
                isDestroyedRepresentation = actorComponent.Destroyed;
                battlefieldActor = actorComponent.Actor;
                battlefieldActor.AddRepresentation(objectRoot, isDestroyedRepresentation);
            }
            else
            {
                levelRoot.AddChild(objectRoot);
            }

            if (battlefieldActor != null &&
                battlefieldActor.IsDamageable &&
                !isDestroyedRepresentation &&
                settledActors.Add(battlefieldActor))
            {
                SettleActorOnTerrain(
                    battlefieldActor,
                    objectRoot,
                    modelCache[levelObject.ModelEntry.Path],
                    debugTriangles);
            }
            var wireframes = wireframeCache[levelObject.ModelEntry.Path];
            for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                var solidInstance = new MeshInstance3D
                {
                    Mesh = meshes[meshIndex],
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided
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

            if (!isDestroyedRepresentation)
            {
                AddDebugTriangles(
                    debugTriangles,
                    levelObject,
                    objectRoot.GlobalTransform,
                    modelCache[levelObject.ModelEntry.Path]);
            }

            if (!isDestroyedRepresentation)
            {
                renderedInstanceCount++;
                if (levelObject.Kind == MechWarriorLevelObjectKind.Actor)
                {
                    renderedActorComponentCount++;
                }
                else if (levelObject.Kind == MechWarriorLevelObjectKind.Debris)
                {
                    renderedDebrisCount++;
                }
            }

            var pointBounds = new Aabb(position, Vector3.Zero);
            worldBounds = hasWorldBounds ? worldBounds.Merge(pointBounds) : pointBounds;
            hasWorldBounds = true;
        }

        GD.Print(
            $"MechRewired: rendered Pyre Light world ({renderedInstanceCount} instances, " +
            $"{renderedActorComponentCount} active actor components, {renderedDebrisCount} ground-settled debris objects, " +
            $"{meshCache.Count} unique models; luminosity levels {GeneralIlluminationLevel} terrain / " +
            $"{ObjectIlluminationLevel} objects).");
        AddImplicitGround(levelRoot, worldBounds, terrainPaletteCounts, palette, debugTriangles);

        var playerMechSounds = PlayerMechSounds.Load(archive);
        var playerMech = new PlayerMech(
            playerMechDefinition.CruisingSpeedKph,
            playerMechDefinition.MaximumSpeedKph,
            playerMechSounds);
        AddChild(playerMech);

        var bounds = new Aabb();
        var hasBounds = false;
        var triangleCount = 0;
        var vertexCount = 0;
        var materialMapEntry = archive.GetEntry("BWD/MW2_MAP1.BWD");
        var materialMap = MechWarriorMaterialMap.Load(archive.ReadEntry(materialMapEntry), 1);
        var usedMaterialIndices = modelParts
            .SelectMany(part => part.Model.Polygons)
            .Select(polygon => polygon.MaterialIndex)
            .Distinct()
            .Order()
            .ToArray();
        var materialImages = new Dictionary<byte, MechWarriorIndexedImage>();
        foreach (var materialIndex in usedMaterialIndices)
        {
            // Values above the DOS mech texture slots are polygon rendering flags. Their low byte can
            // collide with unrelated later entries in the wider material table (for example 240/0x1f0).
            if (materialIndex > MaximumTexturedMechMaterialIndex ||
                !materialMap.Images.TryGetValue(materialIndex, out var materialImage))
            {
                continue;
            }

            var imageEntry = archive.GetEntry("CEL", materialImage.ImageResourceIndex);
            var indexedImage = MechWarriorIndexedImage.Load(archive.ReadEntry(imageEntry));
            materialImages.Add(materialIndex, indexedImage);
            GD.Print(
                $"MechRewired: mapped WTB material {materialIndex} through {materialMapEntry.Path} to " +
                $"{imageEntry.Path} ({indexedImage.Width}x{indexedImage.Height} indexed texture; " +
                $"'{materialImage.Name}').");
        }

        foreach (var (definition, model) in modelParts)
        {
            var renderMesh = MechWarriorModelMeshBuilder.Build(
                model,
                palette,
                luminosityTable,
                GeneralIlluminationLevel,
                materialImages);
            var partPosition = MechWarriorCoordinateSystem.ToGodotPosition(definition.Translation);
            var modelInstance = new MeshInstance3D
            {
                Name = definition.Name,
                Mesh = renderMesh,
                Position = partPosition,
                Layers = PlayerMech.ExteriorRenderLayer,
                CastShadow = definition.Name.EndsWith("Decal", StringComparison.Ordinal)
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.DoubleSided
            };
            playerMech.GetPartParent(definition.Name).AddChild(modelInstance);
            modelInstance.AddToGroup(DebugCamera.SolidMeshGroup);

            var wireframeInstance = new MeshInstance3D
            {
                Name = $"{definition.Name}Wireframe",
                Mesh = MechWarriorModelMeshBuilder.BuildWireframe(model),
                Position = partPosition,
                Visible = false,
                Layers = PlayerMech.ExteriorRenderLayer
            };
            playerMech.GetPartParent(definition.Name).AddChild(wireframeInstance);
            wireframeInstance.AddToGroup(DebugCamera.WireframeMeshGroup);

            var partBounds = renderMesh.GetAabb();
            partBounds.Position += partPosition;
            bounds = hasBounds ? bounds.Merge(partBounds) : partBounds;
            hasBounds = true;
            vertexCount += model.Vertices.Count;
            triangleCount += model.Polygons.Sum(polygon => polygon.VertexIndices.Count - 2);
        }

        var deploymentPosition = MechWarriorCoordinateSystem.ToGodotPosition(playerStart.Position);
        var surfaceHeight = FindDeploymentSurfaceHeight(debugTriangles, deploymentPosition);
        playerMech.Position = new Vector3(
            deploymentPosition.X,
            surfaceHeight - bounds.Position.Y,
            deploymentPosition.Z);
        playerMech.RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(
            new System.Numerics.Vector3(0.0f, playerStart.StartingAngle, 0.0f));
        playerMech.Configure(bounds, debugTriangles.AsReadOnly());
        var playerNavigation = new PlayerNavigation(
            playerMech,
            navigationPoints,
            playerMechSounds.NavigationPointTone,
            playerMechSounds.NavigationPointReports);
        AddChild(playerNavigation);
        var playerMission = new PlayerMission(archive, missionDefinition);
        AddChild(playerMission);
        playerNavigation.NavigationPointReached += index => playerMission.Apply(new MissionEvent(
            MissionEventKind.NavigationPointReached,
            navigationPoints[index].ResourceName));
        var playerTargeting = new PlayerTargeting(
            playerMech,
            playerMission,
            debugTriangles.AsReadOnly(),
            battlefieldActors,
            playerMechSounds.MediumLaser);
        AddChild(playerTargeting);

        var hudLayer = new CanvasLayer
        {
            Name = "PlayerHudLayer",
            Layer = 10
        };
        AddChild(hudLayer);
        var playerHud = new PlayerHud(playerMech, playerNavigation, playerTargeting, playerMission)
        {
            Name = "PlayerHud",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        hudLayer.AddChild(playerHud);

        GD.Print(
            $"MechRewired: deployed PlayerMech Timber Wolf at MW2 " +
            $"({playerStart.Position.X:F2}, {playerStart.Position.Y:F2}, {playerStart.Position.Z:F2}), " +
            $"heading {playerStart.StartingAngle} degrees, feet at rendered Y={surfaceHeight:F2} " +
            $"({modelParts.Count} parts, {vertexCount} source vertices, " +
            $"{triangleCount} triangles, scale {MechWarriorModelMeshBuilder.SourceUnitScale}).");

        var target = playerMech.ToGlobal(bounds.GetCenter());
        var modelSize = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        var cameraDistance = Math.Max(modelSize * 3.0f, 1.0f);
        var cameraDirection = new Vector3(0.75f, 0.4f, 1.0f).Normalized();
        var camera = new DebugCamera
        {
            Position = target + cameraDirection * cameraDistance,
            Current = false,
            Far = Math.Max(cameraDistance * 4.0f, 8000.0f),
            CullMask = 1u | PlayerMech.ExteriorRenderLayer,
            SceneTriangles = debugTriangles.AsReadOnly(),
            CockpitCamera = playerMech.CockpitCamera,
            ExternalCamera = playerMech.ExternalCamera,
            PlayerMech = playerMech
        };
        camera.LookAtFromPosition(camera.Position, target);
        AddChild(camera);
    }

    private void SetFogDistance(float distance, bool logChange)
    {
        m_fogDistance = Mathf.Clamp(distance, MinimumFogDistance, MaximumFogDistance);
        if (m_environment == null)
        {
            return;
        }

        m_environment.FogDepthBegin = m_fogDistance * 0.25f;
        m_environment.FogDepthEnd = m_fogDistance;
        if (logChange)
        {
            GD.Print(
                $"MechRewired: depth fog adjusted to {m_environment.FogDepthBegin:F0}-" +
                $"{m_environment.FogDepthEnd:F0}m ([ nearer; ] farther).");
        }
    }

    private static float FindDeploymentSurfaceHeight(
        IEnumerable<DebugTriangle> debugTriangles,
        Vector3 deploymentPosition)
    {
        if (TryFindTerrainSurfaceHeight(debugTriangles, deploymentPosition, out var surfaceHeight))
        {
            return surfaceHeight;
        }

        GD.PushWarning("MechRewired: no rendered surface found beneath the player deployment; using NAVP Y.");
        return deploymentPosition.Y;
    }

    private static bool TryFindTerrainSurfaceHeight(
        IEnumerable<DebugTriangle> debugTriangles,
        Vector3 position,
        out float surfaceHeight)
    {
        const float rayHeight = 10000.0f;
        var terrainTriangles = debugTriangles.Where(triangle =>
            triangle.ResourcePath == "IMPLICIT/GROUND" ||
            triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal));
        var origin = new Vector3(position.X, rayHeight, position.Z);
        if (!DebugTriangleRaycaster.TryFindNearest(
                terrainTriangles,
                origin,
                Vector3.Down,
                out _,
                out var distance))
        {
            surfaceHeight = 0.0f;
            return false;
        }

        surfaceHeight = origin.Y - distance;
        return true;
    }

    private static void SettleActorOnTerrain(
        BattlefieldActor actor,
        Node3D rootRepresentation,
        IReadOnlyList<MechWarriorModel> models,
        IReadOnlyList<DebugTriangle> sceneTriangles)
    {
        var lowestY = models
            .SelectMany(model => model.Vertices)
            .Select(vertex => rootRepresentation.GlobalTransform *
                              (MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) *
                               MechWarriorModelMeshBuilder.SourceUnitScale))
            .Min(position => position.Y);
        var surfaceHeight = TryFindTerrainSurfaceHeight(
            sceneTriangles,
            rootRepresentation.GlobalPosition,
            out var terrainHeight)
            ? terrainHeight
            : ImplicitGroundHeight;
        var adjustment = surfaceHeight - lowestY;
        actor.Position += Vector3.Up * adjustment;
        if (Mathf.Abs(adjustment) >= 0.01f)
        {
            GD.Print(
                $"MechRewired: settled {actor.Description} object {actor.Definition.ObjectId} in " +
                $"BWD/{actor.SourceResourceName}.BWD " +
                $"onto rendered terrain by {adjustment:F2}m.");
        }
    }

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
                        levelObject.SourceEntry.Path,
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
        transform * (MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) *
                     MechWarriorModelMeshBuilder.SourceUnitScale);

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
        debugTriangles.Add(new DebugTriangle("IMPLICIT/GROUND", "IMPLICIT/GROUND", -1, 0, 0, cornerA, cornerB, cornerC));
        debugTriangles.Add(new DebugTriangle("IMPLICIT/GROUND", "IMPLICIT/GROUND", -1, 0, 1, cornerA, cornerC, cornerD));
        GD.Print(
            $"MechRewired: added implicit ground plane at Y={ImplicitGroundHeight:F2} " +
            $"({size.X:F0} × {size.Y:F0}, " +
            $"palette index {paletteIndex}).");
    }
}
