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
using MechRewired.Simulation;

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
    private const byte CamoMechMaterialIndex = 0;
    private const byte FlaggedCamoMechMaterialIndex = 0x70;
    private const float DefaultFogDistance = 1200.0f;
    private const float MinimumFogDistance = 300.0f;
    private const float MaximumFogDistance = 5000.0f;
    private const float MinimumSceneryObstacleHeight = 5.0f;
    private const string DefaultScenarioPath = "BWD/YELLSCN1.BWD";
    private const string DefaultPlayerMechPath = "MEK/MDG00STD.MEK";
    private static readonly IReadOnlyDictionary<string, string> DamageShapePrefixes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["battlemaster"] = "BTTL",
            ["direwolf"] = "DIRE",
            ["elemental"] = "ELEM",
            ["firemoth"] = "FIRE",
            ["gargoyle"] = "GARG",
            ["hellbringer"] = "HELL",
            ["jenner"] = "JENN",
            ["kitfox"] = "KITF",
            ["maddog"] = "MADD",
            ["marauder"] = "MARA",
            ["nova"] = "NOVA",
            ["rifleman"] = "RIFL",
            ["stormcrow"] = "STRM",
            ["summoner"] = "SUMM",
            ["tarantula"] = "TARA",
            ["timberwolf"] = "TIMB",
            ["warhammer"] = "WARH",
            ["warhawk"] = "WARK"
        };
    private static readonly string[] ExplosionDebrisPaths =
    [
        "POLY/CHUNKER1.WTB",
        "POLY/CHUNKER2.WTB",
        "POLY/CHUNKLET.WTB"
    ];

    private Godot.Environment m_environment;
    private BattlefieldEffects m_battlefieldEffects;

    public override void _Ready()
    {
        GD.Print("MechRewired: reactor online.");
        GD.Print(
            $"MechRewired: rendering with {RenderingServer.GetCurrentRenderingMethod()} " +
            $"on {RenderingServer.GetCurrentRenderingDriverName()}.");
        if (!TryLoadGameData(
                out var archive,
                out var palette,
                out var playerChassis,
                out var playerChassisName,
                out var level,
                out var planet,
                out var luminosityTable,
                out var playerStart,
                out var navigationPoints,
                out var missionDefinition,
                out var playerMechDefinition,
                out var missionGamePieces,
                out var missionResources))
        {
            return;
        }

        try
        {
            BuildScene(
                archive,
                palette,
                playerChassis,
                playerChassisName,
                level,
                planet,
                luminosityTable,
                playerStart,
                navigationPoints,
                missionDefinition,
                playerMechDefinition,
                missionGamePieces,
                missionResources);
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

#if DEBUG
        if (m_battlefieldEffects?.TryHandleDebugInput(keyEvent) == true)
        {
            GetViewport().SetInputAsHandled();
        }
#endif
    }

    private static bool TryLoadGameData(
        out MechWarriorProjectArchive archive,
        out MechWarriorPalette palette,
        out MechWarriorMechChassis playerChassis,
        out string playerChassisName,
        out MechWarriorLevel level,
        out MechWarriorWorldFile planet,
        out MechWarriorLuminosityTable luminosityTable,
        out MechWarriorWorldNavPoint playerStart,
        out IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        out MissionDefinition missionDefinition,
        out MechWarriorMechFile playerMechDefinition,
        out IReadOnlyList<MechWarriorMissionGamePiece> missionGamePieces,
        out MechWarriorMissionResources missionResources)
    {
        archive = null;
        palette = null;
        playerChassis = null;
        playerChassisName = null;
        level = null;
        planet = null;
        luminosityTable = null;
        playerStart = null;
        navigationPoints = null;
        missionDefinition = null;
        playerMechDefinition = null;
        missionGamePieces = null;
        missionResources = null;
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

            var resolvedMissionResources = MechWarriorMissionResources.Load(archive, DefaultScenarioPath);
            missionResources = resolvedMissionResources;
            GD.Print(
                $"MechRewired: resolved mission {missionResources.ScenarioEntry.Path} " +
                $"(prefix {missionResources.MissionPrefix}; palette {missionResources.PaletteEntry.Path}; " +
                $"world {missionResources.Level.Entry.Path}; planet {missionResources.Planet.Entry.Path}; " +
                $"deployment {missionResources.PlayerStart.Entry.Path}; " +
                $"{missionResources.NavigationPoints.Count} navigation references).");

            var paletteEntry = missionResources.PaletteEntry;
            palette = MechWarriorPalette.Load(archive.ReadEntry(paletteEntry));
            GD.Print($"MechRewired: loaded {paletteEntry.Path} ({palette.Colors.Count} colors).");

            var playerMechEntry = archive.GetEntry(DefaultPlayerMechPath);
            var mechCatalog = MechWarriorMechCatalog.Load(archive);
            var playerChassisIdentity = mechCatalog.ResolveConfiguration(playerMechEntry.Name);
            var playerChassisEntry = archive.GetEntry(
                $"BWD/{playerChassisIdentity.ResourceName.ToUpperInvariant()}.BWD");
            playerChassis = MechWarriorMechChassis.Load(archive.ReadEntry(playerChassisEntry));
            playerChassisName = playerChassisIdentity.DisplayName;
            GD.Print(
                $"MechRewired: resolved player configuration {playerMechEntry.Path} through MECH.MTB " +
                $"as {playerChassisName} ({playerChassisIdentity.Tonnage} tons; " +
                $"{playerChassisEntry.Path}; {playerChassis.Objects.Count} authored objects, " +
                $"{playerChassis.PointsOfFire.Count} firing points).");
            playerMechDefinition = MechWarriorMechFile.Load(archive.ReadEntry(playerMechEntry));
            GD.Print(
                $"MechRewired: loaded {playerMechEntry.Path} ({playerMechDefinition.Tonnage} tons; " +
                $"{playerMechDefinition.WalkingMovementPoints} walking movement points; " +
                $"{playerMechDefinition.CruisingSpeedKph:F1} km/h cruise; " +
                $"{playerMechDefinition.MaximumSpeedKph:F1} km/h maximum; authored armor/internal " +
                $"{string.Join(", ", playerMechDefinition.Sections.Select(section =>
                    $"{section.Key} {section.Value.FrontArmor}/{section.Value.RearArmor}/{section.Value.InternalStructure}"))}; " +
                $"{playerMechDefinition.HeatSinkCount} heat sinks, {playerMechDefinition.Weapons.Count} supported weapons, " +
                $"{playerMechDefinition.AmmoBinCount} ammo bins" +
                (playerMechDefinition.UnsupportedWeaponIds.Count == 0
                    ? string.Empty
                    : $", unsupported weapon IDs [{string.Join(", ", playerMechDefinition.UnsupportedWeaponIds)}]") +
                ").");
            var planetEntry = missionResources.Planet.Entry;
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

            var playerStartEntry = missionResources.PlayerStart.Entry;
            var playerStartWorld = MechWarriorWorldFile.Load(
                archive.ReadEntry(playerStartEntry),
                missionResources.PlayerStart.Include.Transform);
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

            var scenarioEntry = missionResources.ScenarioEntry;
            var scenario = missionResources.Scenario;
            missionDefinition = LoadMissionDefinition(scenarioEntry, scenario);
            navigationPoints = LoadMissionNavigationPoints(archive, missionResources);
            missionGamePieces = MechWarriorMissionGamePieceLoader.Load(archive, scenario);
            foreach (var gamePiece in missionGamePieces)
            {
                var specification = gamePiece.Specification;
                var spawn = gamePiece.SpawnPoint;
                GD.Print(
                    $"MechRewired: resolved {gamePiece.Star.Disposition.ToString().ToLowerInvariant()} " +
                    $"game piece group {specification.GroupId}: {specification.DisplayName} " +
                    $"({specification.ConfigurationName}; pilot {specification.PilotName}) at " +
                    $"({spawn.Position.X:F2}, {spawn.Position.Y:F2}, {spawn.Position.Z:F2}), " +
                    $"heading {spawn.StartingAngle:F1} degrees; target/sleep/rubberband " +
                    $"{specification.TargetRange}/{specification.SleepRange}/{specification.RubberbandRange}m.");
            }

            level = MechWarriorLevel.Load(
                archive,
                missionResources.Level.Entry.Path,
                include => include.Name.StartsWith(resolvedMissionResources.AreaPrefix, StringComparison.OrdinalIgnoreCase));
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
                $"MechRewired: assembled {missionResources.MissionPrefix} mission world ({level.Sources.Count} BWD resources, " +
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
        MechWarriorMissionResources missionResources)
    {
        var navigationPoints = new List<MechWarriorMissionNavigationPoint>();
        foreach (var navigationResource in missionResources.NavigationPoints)
        {
            var navigationEntry = navigationResource.Entry;
            var navigationWorld = MechWarriorWorldFile.Load(
                archive.ReadEntry(navigationEntry),
                navigationResource.Include.Transform);
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
            throw new InvalidDataException(
                $"{missionResources.ScenarioEntry.Path} contains no named navigation includes.");
        }

        GD.Print(
            $"MechRewired: loaded {navigationPoints.Count} mission navigation points from " +
            $"{missionResources.ScenarioEntry.Path}.");
        return navigationPoints.AsReadOnly();
    }

    private void BuildScene(
        MechWarriorProjectArchive archive,
        MechWarriorPalette palette,
        MechWarriorMechChassis playerChassis,
        string playerChassisName,
        MechWarriorLevel level,
        MechWarriorWorldFile planet,
        MechWarriorLuminosityTable luminosityTable,
        MechWarriorWorldNavPoint playerStart,
        IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        MissionDefinition missionDefinition,
        MechWarriorMechFile playerMechDefinition,
        IReadOnlyList<MechWarriorMissionGamePiece> missionGamePieces,
        MechWarriorMissionResources missionResources)
    {
        var skyTopColor = ToGodotColor(palette[SkyTopPaletteIndex]);
        var skyHorizonColor = ToGodotColor(palette[SkyHorizonPaletteIndex]);
        var skyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = skyTopColor,
            SkyHorizonColor = skyHorizonColor,
            SkyCurve = 0.35f,
            GroundBottomColor = skyHorizonColor,
            GroundHorizonColor = skyHorizonColor,
            GroundCurve = 0.2f,
            SunAngleMax = 1.5f,
            SunCurve = 0.08f,
            UseDebanding = true
        };
        var ambientEnergy = Math.Clamp((planet.Lighting?.AmbientLevel ?? 50) / 100.0f, 0.0f, 1.0f);
        var directionalEnergy = 1.0f - ambientEnergy;
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
            FogSkyAffect = 0.0f,
            GlowEnabled = true,
            GlowIntensity = 0.8f,
            GlowStrength = 1.0f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.5f
        };
        ConfigureDepthCue(planet.Lighting?.ShadeDistance, planet.ViewDistance);
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
            LightEnergy = directionalEnergy,
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
            $"MechRewired: rendered mission atmosphere (time {planet.TimeOfDay}; " +
            $"palette sky {SkyTopPaletteIndex}-{SkyHorizonPaletteIndex}; ambient {ambientEnergy:F2}; " +
            $"directional {directionalEnergy:F2}; " +
            $"sun elevation {sunElevation:F1} degrees at {FallbackSunAzimuthDegrees:F0}-degree " +
            $"mirrored fallback azimuth; 8192px 32-bit directional shadows to " +
            $"{DirectionalShadowDistance:F0}m at 90% opacity; depth cue " +
            $"{m_environment.FogDepthBegin:F0}-{m_environment.FogDepthEnd:F0}m).");

        var levelRoot = new Node3D
        {
            Name = "MissionWorld"
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
        var battlefieldEffectSounds = BattlefieldEffectSounds.Load(archive);
        var battlefieldEffects = new BattlefieldEffects(battlefieldEffectSounds.Explosions)
        {
            Name = "BattlefieldEffects"
        };
        AddChild(battlefieldEffects);
        m_battlefieldEffects = battlefieldEffects;
        var actorComponents = new Dictionary<
            (string SourcePath, int ObjectId),
            (BattlefieldActor Actor, bool Destroyed)>();
        foreach (var battlefieldActor in battlefieldActors)
        {
            actorRoot.AddChild(battlefieldActor);
            battlefieldActor.Destroyed += battlefieldEffects.SpawnDestruction;
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
        var staticSceneryObstacles = new List<SceneryObstacle>();
        var collisionWallsByObject = new Dictionary<
            (string SourcePath, int ObjectId),
            IReadOnlyList<SceneryWallTriangle>>();
        var renderedBoundsByObject = new Dictionary<(string SourcePath, int ObjectId), Aabb>();
        var authoredColorTasks = LoadAuthoredColorTasks(archive, level.Sources, palette);
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
                if (authoredColorTasks.TryGetValue((levelObject.SourceEntry.Path, levelObject.Id), out var colors))
                {
                    var locatorPosition = MechWarriorCoordinateSystem.ToGodotPosition(levelObject.Transform.Translation);
                    if (levelObject.RelativeToId >= 0 &&
                        renderedBoundsByObject.TryGetValue(
                            (levelObject.SourceEntry.Path, levelObject.RelativeToId),
                            out var parentBounds))
                    {
                        // DUMMY locators carry the horizontal attachment point, but their vertical coordinate
                        // is not a physical mesh origin. Mount them on the authored parent assembly instead.
                        locatorPosition.Y = parentBounds.End.Y;
                    }

                    var locator = new Node3D
                    {
                        Name = $"{levelObject.ModelEntry.Name}LightLocator",
                        Position = locatorPosition
                    };
                    locator.AddChild(CreateAnimatedLocatorLight(colors));
                    levelRoot.AddChild(locator);
                    GD.Print(
                        $"MechRewired: rendered map-authored colour locator {levelObject.SourceEntry.Path} " +
                        $"object {levelObject.Id} ({levelObject.ModelEntry.Name}; {colors.Length} frames).");
                }

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

            var collisionWalls = BuildSceneryWalls(
                objectRoot.GlobalTransform,
                modelCache[levelObject.ModelEntry.Path]);
            var objectBounds = meshes
                .Select(mesh => objectRoot.GlobalTransform * mesh.GetAabb())
                .Aggregate((combined, next) => combined.Merge(next));
            renderedBoundsByObject[(levelObject.SourceEntry.Path, levelObject.Id)] = objectBounds;
            collisionWallsByObject[(levelObject.SourceEntry.Path, levelObject.Id)] = collisionWalls;
            if (levelObject.Kind == MechWarriorLevelObjectKind.Scenery &&
                TryCreateSceneryObstacle(
                    $"{levelObject.ModelEntry.Name} object {levelObject.Id}",
                    collisionWalls,
                    out var sceneryObstacle))
            {
                staticSceneryObstacles.Add(sceneryObstacle);
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

        foreach (var battlefieldActor in battlefieldActors)
        {
            var activeWalls = battlefieldActor.Definition.Components
                .SelectMany(component => collisionWallsByObject.GetValueOrDefault(
                    (component.SourceEntry.Path, component.Id),
                    Array.Empty<SceneryWallTriangle>()))
                .ToArray();
            var destroyedWalls = battlefieldActor.Definition.DestroyedComponents
                .SelectMany(component => collisionWallsByObject.GetValueOrDefault(
                    (component.SourceEntry.Path, component.Id),
                    Array.Empty<SceneryWallTriangle>()))
                .ToArray();
            TryCreateSceneryObstacle(
                $"{battlefieldActor.Description} object {battlefieldActor.Definition.ObjectId}",
                activeWalls,
                out var activeObstacle);
            TryCreateSceneryObstacle(
                $"{battlefieldActor.Description} wreckage {battlefieldActor.Definition.ObjectId}",
                destroyedWalls,
                out var destroyedObstacle);
            battlefieldActor.ConfigureSceneryObstacles(activeObstacle, destroyedObstacle);
        }

        GD.Print(
            $"MechRewired: rendered mission world ({renderedInstanceCount} instances, " +
            $"{renderedActorComponentCount} active actor components, {renderedDebrisCount} ground-settled debris objects, " +
            $"{meshCache.Count} unique models; luminosity levels {GeneralIlluminationLevel} terrain / " +
            $"{ObjectIlluminationLevel} objects).");
        AddImplicitGround(
            levelRoot,
            worldBounds,
            terrainPaletteCounts,
            palette,
            luminosityTable,
            debugTriangles);
        BattlefieldPhysics.AddTerrainCollision(levelRoot, debugTriangles);
        battlefieldEffects.ConfigureTerrain(debugTriangles.AsReadOnly());
        LoadAmbientEffects(
            archive,
            missionResources.Level.Entry.Path,
            battlefieldEffects,
            battlefieldEffectSounds.AmbientFire);
        var playerRotation = MechWarriorCoordinateSystem.ToGodotRotation(
            new System.Numerics.Vector3(0.0f, playerStart.StartingAngle, 0.0f));
        var playerBasis = Basis.FromEuler(playerRotation * (Mathf.Pi / 180.0f));
        var deploymentDirection = (-playerBasis.Z).Normalized();
        var deploymentLeft = -playerBasis.X.Normalized();
        var playerDeploymentPosition = MechWarriorCoordinateSystem.ToGodotPosition(playerStart.Position);
        var deploymentAnchor = playerDeploymentPosition +
                               deploymentDirection * 55.0f +
                               deploymentLeft * 40.0f;
        deploymentAnchor.Y = FindDeploymentSurfaceHeight(debugTriangles, deploymentAnchor);
        var dropShipDepartureDirection = playerDeploymentPosition - deploymentAnchor;
        dropShipDepartureDirection.Y = 0.0f;
        dropShipDepartureDirection = dropShipDepartureDirection.Normalized();
        var extractionResourceName = missionDefinition.Objectives
            .LastOrDefault(objective => objective.Kind == MissionObjectiveKind.Extract)
            ?.TargetResourceName;
        var extractionPoint = navigationPoints.FirstOrDefault(point => string.Equals(
                                  point.ResourceName,
                                  extractionResourceName,
                                  StringComparison.OrdinalIgnoreCase)) ??
                              navigationPoints.LastOrDefault();
        var extractionPosition = extractionPoint != null
            ? MechWarriorCoordinateSystem.ToGodotPosition(extractionPoint.Point.Position)
            : deploymentAnchor;
        var extractionApproach = navigationPoints.Count > 1
            ? (extractionPosition -
               MechWarriorCoordinateSystem.ToGodotPosition(navigationPoints[^2].Point.Position))
            : deploymentDirection;
        extractionApproach.Y = 0.0f;
        extractionApproach = extractionApproach.Normalized();
        var extractionLeft = -extractionApproach.Cross(Vector3.Up).Normalized();
        var extractionAnchor = extractionPosition + extractionApproach * 55.0f + extractionLeft * 40.0f;
        extractionAnchor.Y = FindDeploymentSurfaceHeight(debugTriangles, extractionAnchor);
        var missionDropShips = LoadMissionDropShips(
            archive,
            missionResources.Level.Entry.Path,
            levelRoot,
            palette,
            luminosityTable,
            deploymentAnchor,
            extractionAnchor,
            dropShipDepartureDirection);

        var playerMechSounds = PlayerMechSounds.Load(archive);
        var playerMech = new PlayerMech(
            playerMechDefinition,
            playerMechSounds);
        AddChild(playerMech);

        var bounds = new Aabb();
        var hasBounds = false;
        var triangleCount = 0;
        var vertexCount = 0;
        var renderedPartCount = 0;
        var materialMapEntry = archive.GetEntry("BWD/MW2_MAP1.BWD");
        var materialMap = MechWarriorMaterialMap.Load(archive.ReadEntry(materialMapEntry), 1);
        var materialImages = new Dictionary<byte, MechWarriorIndexedImage>();
        var playerObjectsById = playerChassis.Objects.ToDictionary(chassisObject => chassisObject.Id);
        var playerTorsoObjectId = playerChassis.ThingObjectIds
            .FirstOrDefault(id => playerObjectsById.ContainsKey(id));
        var playerTorsoPivot = playerTorsoObjectId != 0
            ? MechWarriorCoordinateSystem.ToGodotPosition(
                playerObjectsById[playerTorsoObjectId].Transform.Translation)
            : Vector3.Zero;
        playerMech.Torso.Position = playerTorsoPivot;
        foreach (var chassisObject in playerChassis.Objects.Where(chassisObject =>
                     chassisObject.ModelResourceIndex >= 0))
        {
            var modelEntry = archive.GetEntry("POLY", chassisObject.ModelResourceIndex);
            if (modelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var model = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry))
                .MaxBy(candidate => candidate.Polygons.Count) ??
                throw new InvalidDataException($"{modelEntry.Path} contains no mech model.");
            LoadMechMaterialImages(
                archive,
                materialMapEntry,
                materialMap,
                materialImages,
                model.Polygons.Select(polygon => polygon.MaterialIndex));
            var isTorsoPart = playerTorsoObjectId != 0 &&
                              IsDescendantOf(chassisObject.Id, playerTorsoObjectId, playerObjectsById);
            var partPosition = MechWarriorCoordinateSystem.ToGodotPosition(
                chassisObject.Transform.Translation);
            var localPosition = isTorsoPart ? partPosition - playerTorsoPivot : partPosition;
            var partRotation = MechWarriorCoordinateSystem.ToGodotRotation(
                chassisObject.Transform.RotationDegrees);
            var partScale = MechWarriorCoordinateSystem.ToGodotScale(chassisObject.Transform.Scale);
            var partParent = isTorsoPart ? playerMech.Torso : playerMech.Legs;
            var renderMesh = MechWarriorModelMeshBuilder.Build(
                model,
                palette,
                luminosityTable,
                GeneralIlluminationLevel,
                materialImages);
            var modelInstance = new MeshInstance3D
            {
                Name = modelEntry.Name,
                Mesh = renderMesh,
                Position = localPosition,
                RotationDegrees = partRotation,
                Scale = partScale,
                Layers = PlayerMech.ExteriorRenderLayer,
                CastShadow = modelEntry.Name.Contains("DEC", StringComparison.OrdinalIgnoreCase)
                    ? GeometryInstance3D.ShadowCastingSetting.Off
                    : GeometryInstance3D.ShadowCastingSetting.DoubleSided
            };
            partParent.AddChild(modelInstance);
            playerMech.RegisterGaitPart(modelInstance, modelEntry.Name);
            playerMech.RegisterDestructiblePart(modelInstance, modelEntry.Name);
            modelInstance.AddToGroup(DebugCamera.SolidMeshGroup);

            var wireframeInstance = new MeshInstance3D
            {
                Name = $"{modelEntry.Name}Wireframe",
                Mesh = MechWarriorModelMeshBuilder.BuildWireframe(model),
                Position = localPosition,
                RotationDegrees = partRotation,
                Scale = partScale,
                Visible = false,
                Layers = PlayerMech.ExteriorRenderLayer
            };
            partParent.AddChild(wireframeInstance);
            playerMech.RegisterGaitPart(wireframeInstance, modelEntry.Name);
            wireframeInstance.AddToGroup(DebugCamera.WireframeMeshGroup);

            var absoluteTransform = isTorsoPart
                ? new Transform3D(Basis.Identity, playerTorsoPivot) * modelInstance.Transform
                : modelInstance.Transform;
            var partBounds = absoluteTransform * renderMesh.GetAabb();
            bounds = hasBounds ? bounds.Merge(partBounds) : partBounds;
            hasBounds = true;
            renderedPartCount++;
            vertexCount += model.Vertices.Count;
            triangleCount += model.Polygons.Sum(polygon => polygon.VertexIndices.Count - 2);
            GD.Print(
                $"MechRewired: loaded player {modelEntry.Path} (subtype {model.Subtype}, " +
                $"{model.Vertices.Count} vertices, {model.Polygons.Count} polygons).");
        }

        if (!hasBounds)
        {
            throw new InvalidDataException($"The {playerChassisName} chassis contains no supported renderable mech parts.");
        }

        var deploymentPosition = MechWarriorCoordinateSystem.ToGodotPosition(playerStart.Position);
        var surfaceHeight = FindDeploymentSurfaceHeight(debugTriangles, deploymentPosition);
        playerMech.Position = new Vector3(
            deploymentPosition.X,
            surfaceHeight - bounds.Position.Y,
            deploymentPosition.Z);
        playerMech.RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(
            new System.Numerics.Vector3(0.0f, playerStart.StartingAngle, 0.0f));
        playerMech.Configure(
            bounds,
            playerTorsoPivot,
            BuildWeaponMounts(playerChassis, playerObjectsById, playerTorsoObjectId),
            debugTriangles.AsReadOnly(),
            () => GetSceneryObstacles(staticSceneryObstacles, battlefieldActors));
        var playerMission = new PlayerMission(archive, missionDefinition);
        AddChild(playerMission);
        AddChild(new PlayerDeathSequence(
            playerMech,
            battlefieldEffects,
            playerMechSounds.DeathExplosion,
            playerMission.Fail));
        battlefieldEffects.ConfigureObserver(playerMech);
        foreach (var battlefieldActor in battlefieldActors)
        {
            battlefieldActor.ConfigureEffectPersistence(playerMech);
        }

        var enemyMechs = LoadEnemyMechs(
            archive,
            palette,
            luminosityTable,
            materialMapEntry,
            materialMap,
            materialImages,
            missionGamePieces,
            playerMech,
            playerMechSounds.WeaponFireSounds,
            battlefieldEffects,
            () => GetSceneryObstacles(staticSceneryObstacles, battlefieldActors),
            debugTriangles.AsReadOnly());
        GD.Print(
            $"MechRewired: configured {staticSceneryObstacles.Count} static and " +
            $"{battlefieldActors.Length} actor scenery obstacles.");
        var playerNavigation = new PlayerNavigation(
            playerMech,
            navigationPoints,
            playerMechSounds.NavigationPointTone,
            playerMechSounds.NavigationPointReports);
        AddChild(playerNavigation);
        playerNavigation.NavigationPointReached += index => playerMission.Apply(new MissionEvent(
            MissionEventKind.NavigationPointReached,
            navigationPoints[index].ResourceName));
        var playerTargeting = new PlayerTargeting(
            playerMech,
            playerMission,
            debugTriangles.AsReadOnly(),
            battlefieldActors,
            enemyMechs,
            playerMechDefinition,
            playerMechSounds,
            battlefieldEffects);
        AddChild(playerTargeting);

        var hudLayer = new CanvasLayer
        {
            Name = "PlayerHudLayer",
            Layer = 10
        };
        AddChild(hudLayer);
        var playerDamageSilhouette = LoadDamageSilhouette(
            archive,
            playerChassisName,
            playerChassis);
        var playerHud = new PlayerHud(
            playerMech,
            playerDamageSilhouette,
            playerNavigation,
            playerTargeting,
            playerMission)
        {
            Name = "PlayerHud",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        hudLayer.AddChild(playerHud);
        var missionDebrief = new MissionDebrief(playerMission);
        AddChild(missionDebrief);
        playerMission.MissionResolved += outcome =>
        {
            if (outcome == MissionOutcome.Failed)
            {
                missionDebrief.Present(outcome);
            }
        };
        playerMission.MissionCompleted += () =>
        {
            playerMech.LockMovementForExtraction();
            if (missionDropShips.Count == 0)
            {
                missionDebrief.Present(MissionOutcome.Successful);
                return;
            }

            var landedDropShips = 0;
            foreach (var dropShip in missionDropShips)
            {
                dropShip.ExtractionLanded += () =>
                {
                    landedDropShips++;
                    if (landedDropShips == missionDropShips.Count)
                    {
                        missionDebrief.Present(MissionOutcome.Successful);
                    }
                };
                dropShip.BeginExtraction();
            }
        };

        GD.Print(
            $"MechRewired: deployed PlayerMech {playerChassisName} at MW2 " +
            $"({playerStart.Position.X:F2}, {playerStart.Position.Y:F2}, {playerStart.Position.Z:F2}), " +
            $"heading {playerStart.StartingAngle} degrees, feet at rendered Y={surfaceHeight:F2} " +
            $"({renderedPartCount} parts, {vertexCount} source vertices, " +
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
            PlayerMech = playerMech,
            PlayerTargeting = playerTargeting
        };
        camera.LookAtFromPosition(camera.Position, target);
        AddChild(camera);
    }

    private IReadOnlyList<EnemyMech> LoadEnemyMechs(
        MechWarriorProjectArchive archive,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        MechWarriorProjectEntry materialMapEntry,
        MechWarriorMaterialMap materialMap,
        Dictionary<byte, MechWarriorIndexedImage> materialImages,
        IReadOnlyList<MechWarriorMissionGamePiece> missionGamePieces,
        PlayerMech playerMech,
        IReadOnlyDictionary<string, AudioStreamWav> weaponSounds,
        BattlefieldEffects battlefieldEffects,
        Func<IReadOnlyList<SceneryObstacle>> sceneryObstacleProvider,
        IReadOnlyList<DebugTriangle> debugTriangles)
    {
        var enemyRoot = new Node3D { Name = "EnemyMechs" };
        AddChild(enemyRoot);
        var enemies = new List<EnemyMech>();
        var damageSilhouettes = new Dictionary<string, MechDamageSilhouette>(StringComparer.OrdinalIgnoreCase);
        foreach (var gamePiece in missionGamePieces.Where(gamePiece =>
                     gamePiece.Star.Disposition == MechWarriorMissionDisposition.Hostile))
        {
            var chassis = MechWarriorMechChassis.Load(archive.ReadEntry(gamePiece.ChassisEntry));
            var mechDefinition = MechWarriorMechFile.Load(archive.ReadEntry(gamePiece.ConfigurationEntry));
            if (!damageSilhouettes.TryGetValue(gamePiece.Specification.ChassisName, out var damageSilhouette))
            {
                damageSilhouette = LoadDamageSilhouette(
                    archive,
                    gamePiece.Specification.ChassisName,
                    chassis);
                damageSilhouettes.Add(gamePiece.Specification.ChassisName, damageSilhouette);
            }

            var enemy = new EnemyMech(
                gamePiece,
                mechDefinition,
                playerMech,
                battlefieldEffects,
                weaponSounds,
                damageSilhouette,
                position => FindDeploymentSurfaceHeight(debugTriangles, position),
                sceneryObstacleProvider,
                debugTriangles);
            enemyRoot.AddChild(enemy);

            var objectsById = chassis.Objects.ToDictionary(mechObject => mechObject.Id);
            var torsoObjectId = chassis.ThingObjectIds.FirstOrDefault(id => objectsById.ContainsKey(id));
            var torsoPivot = torsoObjectId != 0
                ? MechWarriorCoordinateSystem.ToGodotPosition(objectsById[torsoObjectId].Transform.Translation)
                : Vector3.Zero;
            var bounds = new Aabb();
            var hasBounds = false;
            var renderedParts = 0;
            var animatedGaitParts = 0;
            var renderedModelNames = new List<string>();
            var renderedPolygons = 0;
            foreach (var chassisObject in chassis.Objects.Where(chassisObject => chassisObject.ModelResourceIndex >= 0))
            {
                var modelEntry = archive.GetEntry("POLY", chassisObject.ModelResourceIndex);
                renderedModelNames.Add(modelEntry.Name);
                if (modelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var models = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry));
                var model = models.MaxBy(candidate => candidate.Polygons.Count) ??
                            throw new InvalidDataException($"{modelEntry.Path} contains no mech model.");
                LoadMechMaterialImages(
                    archive,
                    materialMapEntry,
                    materialMap,
                    materialImages,
                    model.Polygons.Select(polygon => polygon.MaterialIndex));
                var mesh = MechWarriorModelMeshBuilder.Build(
                    model,
                    palette,
                    luminosityTable,
                    GeneralIlluminationLevel,
                    materialImages);
                var absolutePosition = MechWarriorCoordinateSystem.ToGodotPosition(
                    chassisObject.Transform.Translation);
                var isTorsoPart = torsoObjectId != 0 &&
                                  IsDescendantOf(chassisObject.Id, torsoObjectId, objectsById);
                var modelInstance = new MeshInstance3D
                {
                    Name = modelEntry.Name,
                    Mesh = mesh,
                    Position = isTorsoPart ? absolutePosition - torsoPivot : absolutePosition,
                    RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(
                        chassisObject.Transform.RotationDegrees),
                    Scale = MechWarriorCoordinateSystem.ToGodotScale(chassisObject.Transform.Scale),
                    CastShadow = modelEntry.Name.Contains("DEC", StringComparison.OrdinalIgnoreCase)
                        ? GeometryInstance3D.ShadowCastingSetting.Off
                        : GeometryInstance3D.ShadowCastingSetting.DoubleSided
                };
                (isTorsoPart ? enemy.Torso : enemy.Legs).AddChild(modelInstance);
                if (enemy.RegisterGaitPart(modelInstance, modelEntry.Name))
                {
                    animatedGaitParts++;
                }
                enemy.RegisterDestructiblePart(modelInstance, modelEntry.Name);
                modelInstance.AddToGroup(DebugCamera.SolidMeshGroup);

                var wireframe = new MeshInstance3D
                {
                    Name = $"{modelEntry.Name}Wireframe",
                    Mesh = MechWarriorModelMeshBuilder.BuildWireframe(model),
                    Position = modelInstance.Position,
                    RotationDegrees = modelInstance.RotationDegrees,
                    Scale = modelInstance.Scale,
                    Visible = false
                };
                (isTorsoPart ? enemy.Torso : enemy.Legs).AddChild(wireframe);
                enemy.RegisterGaitPart(wireframe, modelEntry.Name);
                wireframe.AddToGroup(DebugCamera.WireframeMeshGroup);

                var absoluteTransform = isTorsoPart
                    ? new Transform3D(Basis.Identity, torsoPivot) * modelInstance.Transform
                    : modelInstance.Transform;
                var partBounds = absoluteTransform * mesh.GetAabb();
                bounds = hasBounds ? bounds.Merge(partBounds) : partBounds;
                hasBounds = true;
                renderedParts++;
                renderedPolygons += model.Polygons.Count;
            }

            if (!hasBounds)
            {
                throw new InvalidDataException(
                    $"{gamePiece.ChassisEntry.Path} contains no supported renderable mech parts.");
            }

            var configuredWeaponSections = mechDefinition.Weapons
                .Select(weapon => weapon.Section)
                .ToHashSet();
            var authoredWeaponMounts = BuildWeaponMounts(chassis, objectsById, torsoObjectId);
            var weaponMounts = authoredWeaponMounts
                .Where(mount => configuredWeaponSections.Contains(mount.Section))
                .ToArray();
            if (weaponMounts.Length == 0)
            {
                weaponMounts = authoredWeaponMounts
                    .Where(mount => mount.Section is MechDamageSection.LeftArm or MechDamageSection.RightArm)
                    .ToArray();
            }
            enemy.ConfigureVisuals(bounds, torsoPivot, weaponMounts);
            var spawnPosition = MechWarriorCoordinateSystem.ToGodotPosition(gamePiece.SpawnPoint.Position);
            spawnPosition.Y = FindDeploymentSurfaceHeight(debugTriangles, spawnPosition) - bounds.Position.Y;
            enemy.Position = spawnPosition;
            enemy.RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(
                new System.Numerics.Vector3(0.0f, gamePiece.SpawnPoint.StartingAngle, 0.0f));
            enemies.Add(enemy);
            if (animatedGaitParts == 0)
            {
                GD.PushWarning(
                    $"MechRewired: {enemy.Description} has no recognized gait parts among " +
                    $"[{string.Join(", ", renderedModelNames)}].");
            }

            GD.Print(
                $"MechRewired: deployed hostile {enemy.Description} from {gamePiece.ChassisEntry.Path}/" +
                $"{gamePiece.ConfigurationEntry.Name} at rendered ({enemy.Position.X:F2}, {enemy.Position.Y:F2}, " +
                $"{enemy.Position.Z:F2}); {renderedParts} parts, {renderedPolygons} polygons, " +
                $"{animatedGaitParts} articulated gait parts, {weaponMounts.Length} firing points, " +
                $"weapons [{enemy.WeaponLoadout}], {enemy.Health} whole-mech health, " +
                $"{mechDefinition.CruisingSpeedKph:F1} km/h tactical speed.");
        }

        GD.Print(
            $"MechRewired: hostile force deployed dormant ({enemies.Count} data-driven mechs; " +
            "GPS acquire ranges, sensor cone/line of sight, chassis/torso tracking, MEK movement and weapons; " +
            "shared procedural gait).");
        return enemies.AsReadOnly();
    }

    private static MechDamageSilhouette LoadDamageSilhouette(
        MechWarriorProjectArchive archive,
        string chassisName,
        MechWarriorMechChassis chassis)
    {
        if (!DamageShapePrefixes.TryGetValue(chassisName.Replace(" ", string.Empty), out var prefix))
        {
            throw new InvalidDataException(
                $"No original damage silhouette is mapped for chassis '{chassisName}'.");
        }

        var entry = archive.GetEntry($"SHP/{prefix}DMG6.SHP");
        var shape = MechWarriorShapeImage.Load(archive.ReadEntry(entry));
        var silhouette = MechDamageSilhouetteBuilder.Build(archive, shape, chassis);
        var authoredSections = chassis.DamageSectionsByObjectId.Values.Distinct().Order().ToArray();
        GD.Print(
            $"MechRewired: decompressed {entry.Path} damage silhouette " +
            $"for {chassisName} ({shape.Width}x{shape.Height}); mapped " +
            $"{chassis.DamageSectionsByObjectId.Count} OBJL objects across " +
            $"[{string.Join(", ", authoredSections)}].");
        return silhouette;
    }

    private static void LoadMechMaterialImages(
        MechWarriorProjectArchive archive,
        MechWarriorProjectEntry materialMapEntry,
        MechWarriorMaterialMap materialMap,
        Dictionary<byte, MechWarriorIndexedImage> materialImages,
        IEnumerable<byte> materialIndices)
    {
        foreach (var materialIndex in materialIndices.Distinct())
        {
            var textureMaterialIndex = ResolveMechTextureMaterialIndex(materialIndex);
            if (textureMaterialIndex > MaximumTexturedMechMaterialIndex ||
                materialImages.ContainsKey(materialIndex) ||
                !materialMap.Images.TryGetValue(textureMaterialIndex, out var materialImage))
            {
                continue;
            }

            var imageEntry = archive.GetEntry("CEL", materialImage.ImageResourceIndex);
            materialImages.Add(materialIndex, MechWarriorIndexedImage.Load(archive.ReadEntry(imageEntry)));
            GD.Print(
                $"MechRewired: mapped enemy WTB material {materialIndex} through {materialMapEntry.Path} " +
                $"to {imageEntry.Path} ('{materialImage.Name}').");
        }
    }

    // Timber Wolf arm barrels use 0x70 for camouflaged housing sides; their separate end caps retain
    // material 15 (V1DGNHOL), which supplies the original twin gun openings.
    private static byte ResolveMechTextureMaterialIndex(byte materialIndex) =>
        materialIndex == FlaggedCamoMechMaterialIndex
            ? CamoMechMaterialIndex
            : materialIndex;

    private static IReadOnlyList<MechWeaponMountDefinition> BuildWeaponMounts(
        MechWarriorMechChassis chassis,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById,
        int torsoObjectId)
    {
        var mounts = new List<MechWeaponMountDefinition>(chassis.PointsOfFire.Count);
        foreach (var point in chassis.PointsOfFire)
        {
            if (!objectsById.TryGetValue(point.ObjectId, out var chassisObject))
            {
                GD.PushWarning(
                    $"MechRewired: chassis POFO {point.Id} refers to missing object {point.ObjectId}.");
                continue;
            }

            mounts.Add(new MechWeaponMountDefinition(
                point.Id,
                point.Section,
                MechWarriorCoordinateSystem.ToGodotPosition(chassisObject.Transform.Translation),
                torsoObjectId != 0 && IsDescendantOf(chassisObject.Id, torsoObjectId, objectsById)));
        }

        return mounts.AsReadOnly();
    }

    private static bool IsDescendantOf(
        int objectId,
        int ancestorId,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById)
    {
        for (var currentId = objectId; objectsById.TryGetValue(currentId, out var current); currentId = current.RelativeToId)
        {
            if (currentId == ancestorId)
            {
                return true;
            }

            if (current.RelativeToId < 0)
            {
                return false;
            }
        }

        return false;
    }

    private static IReadOnlyList<MissionDropShipSetPiece> LoadMissionDropShips(
        MechWarriorProjectArchive archive,
        string levelPath,
        Node3D levelRoot,
        MechWarriorPalette palette,
        MechWarriorLuminosityTable luminosityTable,
        Vector3 deploymentAnchor,
        Vector3 extractionAnchor,
        Vector3 deploymentDirection)
    {
        var levelEntry = archive.GetEntry(levelPath);
        var levelWorld = MechWarriorWorldFile.Load(archive.ReadEntry(levelEntry));
        var dropShips = new List<MissionDropShipSetPiece>();
        foreach (var include in levelWorld.Includes)
        {
            var setPieceEntry = archive.GetEntry("BWD", include.ResourceIndex);
            var setPieceWorld = MechWarriorWorldFile.Load(archive.ReadEntry(setPieceEntry));
            if (!setPieceWorld.Tasks.Any(task =>
                    task.Command.Split([';', ','], StringSplitOptions.TrimEntries)
                        .Any(argument => argument.Equals("drop", StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            var dropShipSound = LoadDropShipTaskSound(archive, setPieceWorld);
            var animatedColors = LoadDropShipColorTasks(setPieceWorld, palette);
            var dropShip = new MissionDropShipSetPiece(
                setPieceEntry.Name,
                deploymentAnchor,
                extractionAnchor,
                deploymentDirection,
                dropShipSound)
            {
                Name = $"DropShip-{setPieceEntry.Name}"
            };
            var renderedObjectCount = 0;
            var assemblyBounds = new Aabb();
            var hasAssemblyBounds = false;
            foreach (var worldObject in setPieceWorld.Objects)
            {
                var modelEntry = archive.GetEntry("POLY", worldObject.ModelResourceIndex);
                if (modelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase) &&
                    animatedColors.TryGetValue(worldObject.Id, out var locatorColors))
                {
                    var locator = new Node3D
                    {
                        Name = $"{modelEntry.Name}LightLocator",
                        Position = MechWarriorCoordinateSystem.ToGodotPosition(worldObject.Transform.Translation)
                    };
                    locator.AddChild(CreateAnimatedLocatorLight(locatorColors));
                    dropShip.AddChild(locator);
                    renderedObjectCount++;
                    continue;
                }

                try
                {
                    var models = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry));
                    var highestDetailModel = models.MaxBy(model => model.Polygons.Count);
                    var objectRoot = new Node3D
                    {
                        Name = modelEntry.Name,
                        Position = MechWarriorCoordinateSystem.ToGodotPosition(worldObject.Transform.Translation),
                        RotationDegrees = MechWarriorCoordinateSystem.ToGodotRotation(worldObject.Transform.RotationDegrees),
                        Scale = MechWarriorCoordinateSystem.ToGodotScale(worldObject.Transform.Scale)
                    };
                    var renderMesh = MechWarriorModelMeshBuilder.Build(
                        highestDetailModel,
                        palette,
                        luminosityTable,
                        ObjectIlluminationLevel);
                    MakeMeshDoubleSided(renderMesh);
                    var meshInstance = new MeshInstance3D
                    {
                        Mesh = renderMesh,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided
                    };
                    objectRoot.AddChild(meshInstance);
                    var objectBounds = objectRoot.Transform * renderMesh.GetAabb();
                    assemblyBounds = hasAssemblyBounds ? assemblyBounds.Merge(objectBounds) : objectBounds;
                    hasAssemblyBounds = true;
                    if (animatedColors.TryGetValue(worldObject.Id, out var colors))
                    {
                        var light = new OmniLight3D
                        {
                            Name = "EngineFlameLight",
                            LightColor = colors[0],
                            LightEnergy = 4.0f,
                            OmniRange = 28.0f,
                            ShadowEnabled = false
                        };
                        objectRoot.AddChild(light);
                        dropShip.ConfigureAnimatedColor(meshInstance, light, colors);
                    }
                    dropShip.AddChild(objectRoot);
                    renderedObjectCount++;
                }
                catch (InvalidDataException exception)
                {
                    GD.PushWarning(
                        $"MechRewired: skipped unsupported dropship model {modelEntry.Path} object {worldObject.Id}: " +
                        exception.Message);
                }
            }

            if (renderedObjectCount == 0)
            {
                dropShip.QueueFree();
                continue;
            }

            dropShip.ConfigureAssemblyBounds(assemblyBounds);
            levelRoot.AddChild(dropShip);
            dropShip.BeginDeployment();
            dropShips.Add(dropShip);
            var taskSummary = string.Join("; ", setPieceWorld.Tasks.Select(task => task.Command));
            GD.Print(
                $"MechRewired: staged dropship {setPieceEntry.Path} from map include at " +
                $"({include.Transform.Translation.X:F2}, {include.Transform.Translation.Y:F2}, " +
                $"{include.Transform.Translation.Z:F2}) ({renderedObjectCount}/{setPieceWorld.Objects.Count} models; " +
                $"deployment {deploymentAnchor}; extraction {extractionAnchor}; tasks: {taskSummary}).");
        }

        GD.Print($"MechRewired: staged {dropShips.Count} map-authored dropship set pieces.");
        return dropShips.AsReadOnly();
    }

    private static void MakeMeshDoubleSided(ArrayMesh mesh)
    {
        for (var surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
        {
            if (mesh.SurfaceGetMaterial(surfaceIndex) is BaseMaterial3D material)
            {
                material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            }
        }
    }

    private static IReadOnlyDictionary<int, Color[]> LoadDropShipColorTasks(
        MechWarriorWorldFile setPieceWorld,
        MechWarriorPalette palette)
    {
        var colors = new Dictionary<int, Color[]>();
        foreach (var task in setPieceWorld.Tasks.Where(IsColorAnimationTask))
        {
            var arguments = task.Command.Split([';', ','], StringSplitOptions.TrimEntries);
            if (arguments.Length < 2 || !int.TryParse(arguments[0], out var objectId))
            {
                continue;
            }

            var paletteIndices = arguments[1..]
                .Select(argument => byte.TryParse(argument, out var index) ? index : (byte?)null)
                .Where(index => index.HasValue)
                .Select(index => index.Value)
                .ToArray();
            if (paletteIndices.Length == 0)
            {
                continue;
            }

            colors[objectId] = paletteIndices
                .Select(index => ToGodotColor(palette[index]))
                .ToArray();
            GD.Print(
                $"MechRewired: decoded map object {objectId} color animation " +
                $"({string.Join(", ", paletteIndices)}).");
        }

        return colors;
    }

    /// <summary>
    /// Reads original BWD palette-cycle tasks, including locator-only DUMMY objects.
    /// </summary>
    private static IReadOnlyDictionary<(string SourcePath, int ObjectId), Color[]> LoadAuthoredColorTasks(
        MechWarriorProjectArchive archive,
        IReadOnlyList<MechWarriorLevelSource> sources,
        MechWarriorPalette palette)
    {
        var animations = new Dictionary<(string SourcePath, int ObjectId), Color[]>();
        foreach (var source in sources)
        {
            var world = MechWarriorWorldFile.Load(archive.ReadEntry(source.Entry));
            foreach (var (objectId, colors) in LoadDropShipColorTasks(world, palette))
            {
                animations[(source.Entry.Path, objectId)] = colors;
            }
        }

        return animations;
    }

    private static bool IsColorAnimationTask(MechWarriorWorldTask task) =>
        (task.Type & 0xffff) == 1;

    private static AnimatedLocatorLight CreateAnimatedLocatorLight(Color[] colors)
    {
        var light = new AnimatedLocatorLight(colors)
        {
            Name = "AuthoredIndicatorLight",
            LightEnergy = 5.0f,
            OmniRange = 14.0f,
            ShadowEnabled = false
        };
        return light;
    }

    private static AudioStreamWav LoadDropShipTaskSound(
        MechWarriorProjectArchive archive,
        MechWarriorWorldFile setPieceWorld)
    {
        var soundName = setPieceWorld.Tasks
            .Where(task => task.Type == 4)
            .SelectMany(task => task.Command.Split([';', ','], StringSplitOptions.TrimEntries))
            .FirstOrDefault(argument => archive.Entries.Any(entry =>
                entry.DirectoryName.Equals("SNDS", StringComparison.OrdinalIgnoreCase) &&
                entry.Name.Equals($"{argument}.WAV", StringComparison.OrdinalIgnoreCase)));
        return string.IsNullOrWhiteSpace(soundName)
            ? null
            : PlayerMechSounds.LoadWaveResource(
                archive,
                $"SNDS/{soundName}.WAV",
                true,
                "map-authored dropship engine");
    }

    private static void LoadAmbientEffects(
        MechWarriorProjectArchive archive,
        string levelPath,
        BattlefieldEffects battlefieldEffects,
        IReadOnlyDictionary<string, AudioStreamWav> ambientSounds)
    {
        var levelEntry = archive.GetEntry(levelPath);
        var levelWorld = MechWarriorWorldFile.Load(archive.ReadEntry(levelEntry));
        var totalLoadedCount = 0;
        foreach (var include in levelWorld.Includes)
        {
            var effectsEntry = archive.GetEntry("BWD", include.ResourceIndex);
            var effectsWorld = MechWarriorWorldFile.Load(archive.ReadEntry(effectsEntry), include.Transform);
            var flameObjects = effectsWorld.Objects
                .Where(effectObject => effectObject.ObjectType == 0x10)
                .ToArray();
            if (flameObjects.Length == 0)
            {
                continue;
            }

            var soundNamesByObject = new Dictionary<int, string>();
            foreach (var task in effectsWorld.Tasks.Where(task => task.Type == 4))
            {
                var semicolon = task.Command.IndexOf(';');
                var arguments = semicolon >= 0
                    ? task.Command[(semicolon + 1)..].Split(',', StringSplitOptions.TrimEntries)
                    : Array.Empty<string>();
                if (semicolon > 0 &&
                    int.TryParse(task.Command.AsSpan(0, semicolon), out var soundObjectId) &&
                    arguments.Length >= 2)
                {
                    soundNamesByObject[soundObjectId] = arguments[1];
                }
            }

            var effectDefinitions = flameObjects
                .Select(effectObject =>
                {
                    var modelEntry = archive.GetEntry("POLY", effectObject.ModelResourceIndex);
                    var effectModel = MechWarriorModel.LoadAll(archive.ReadEntry(modelEntry))[0];
                    return (
                        Object: effectObject,
                        ModelEntry: modelEntry,
                        Bounds: GetEffectWorldBounds(effectObject, effectModel));
                })
                .ToArray();
            var fireDefinitions = effectDefinitions
                .Where(effect => !effect.ModelEntry.Name.StartsWith("SMO", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var elevatedSmokeByFireId = new Dictionary<int, List<(MechWarriorWorldObject Object, Aabb Bounds)>>();
            var foldedSmokeIds = new HashSet<int>();
            foreach (var smoke in effectDefinitions.Where(effect =>
                         effect.ModelEntry.Name.StartsWith("SMO", StringComparison.OrdinalIgnoreCase)))
            {
                var supportingFire = fireDefinitions
                    .Where(fire => IsElevatedSmokeAboveFire(smoke.Bounds, fire.Bounds))
                    .OrderBy(fire => HorizontalDistance(smoke.Bounds, fire.Bounds))
                    .FirstOrDefault();
                if (supportingFire.Object == null)
                {
                    continue;
                }

                if (!elevatedSmokeByFireId.TryGetValue(supportingFire.Object.Id, out var smokeVolumes))
                {
                    smokeVolumes = [];
                    elevatedSmokeByFireId.Add(supportingFire.Object.Id, smokeVolumes);
                }

                smokeVolumes.Add((smoke.Object, smoke.Bounds));
                foldedSmokeIds.Add(smoke.Object.Id);
            }

            var renderedCount = 0;
            foreach (var effect in effectDefinitions)
            {
                var effectObject = effect.Object;
                var modelEntry = effect.ModelEntry;
                var effectBounds = effect.Bounds;
                if (modelEntry.Name.StartsWith("SMO", StringComparison.OrdinalIgnoreCase) &&
                    foldedSmokeIds.Contains(effectObject.Id))
                {
                    GD.Print(
                        $"MechRewired: folded elevated {modelEntry.Name} object {effectObject.Id} " +
                        "into its lower fire emitter while preserving its authored plume volume.");
                    continue;
                }

                var heightOffset = effectObject.Transform.Translation.Y - include.Transform.Translation.Y;
                var ambientSound = soundNamesByObject.TryGetValue(effectObject.Id, out var soundName) &&
                                   ambientSounds.TryGetValue(soundName, out var mappedSound)
                    ? mappedSound
                    : null;
                if (modelEntry.Name.StartsWith("SMO", StringComparison.OrdinalIgnoreCase))
                {
                    battlefieldEffects.AddAmbientSmoke(
                        effectBounds,
                        include.Transform.Translation.Y,
                        $"{effectsEntry.Name}-{effectObject.Id}",
                        ambientSound);
                }
                else
                {
                    var plumeBounds = effectBounds;
                    if (elevatedSmokeByFireId.TryGetValue(effectObject.Id, out var smokeVolumes))
                    {
                        foreach (var smokeVolume in smokeVolumes)
                        {
                            plumeBounds = MergeBounds(plumeBounds, smokeVolume.Bounds);
                        }
                    }

                    battlefieldEffects.AddAmbientFire(
                        effectBounds,
                        plumeBounds,
                        include.Transform.Translation.Y,
                        $"{effectsEntry.Name}-{effectObject.Id}",
                        ambientSound);
                }

                GD.Print(
                    $"MechRewired: placed {modelEntry.Name} effect from {effectsEntry.Path} " +
                    $"object {effectObject.Id} (relative {effectObject.RelativeToId}; scale " +
                    $"{effectObject.Transform.Scale.X:F2}, {effectObject.Transform.Scale.Y:F2}, " +
                    $"{effectObject.Transform.Scale.Z:F2}; visual size " +
                    $"{effectBounds.Size.X:F1} x {effectBounds.Size.Y:F1} x {effectBounds.Size.Z:F1}m; " +
                    $"height offset {heightOffset:F2}m) at " +
                    $"({effectObject.Transform.Translation.X:F2}, {effectObject.Transform.Translation.Y:F2}, " +
                    $"{effectObject.Transform.Translation.Z:F2}).");
                totalLoadedCount++;
                renderedCount++;
            }

            GD.Print(
                $"MechRewired: loaded {renderedCount}/{flameObjects.Length} scaled fire-and-smoke objects from " +
                $"{effectsEntry.Path} ({effectsWorld.Tasks.Count} ambient-audio tasks).");
        }

        GD.Print($"MechRewired: loaded {totalLoadedCount} authored battlefield effect objects.");
    }

    private static bool IsElevatedSmokeAboveFire(Aabb smoke, Aabb fire)
    {
        var horizontalDistance = HorizontalDistance(smoke, fire);
        return horizontalDistance <= Math.Max(smoke.Size.X, fire.Size.X) * 0.65f &&
               smoke.GetCenter().Y > fire.GetCenter().Y + fire.Size.Y * 0.12f;
    }

    private static float HorizontalDistance(Aabb first, Aabb second) =>
        new Vector2(
            first.GetCenter().X - second.GetCenter().X,
            first.GetCenter().Z - second.GetCenter().Z).Length();

    private static Aabb MergeBounds(Aabb first, Aabb second)
    {
        var minimum = first.Position.Min(second.Position);
        var maximum = first.End.Max(second.End);
        return new Aabb(minimum, maximum - minimum);
    }

    private static Aabb GetEffectWorldBounds(
        MechWarriorWorldObject effectObject,
        MechWarriorModel model)
    {
        var position = MechWarriorCoordinateSystem.ToGodotPosition(effectObject.Transform.Translation);
        var scale = MechWarriorCoordinateSystem.ToGodotScale(effectObject.Transform.Scale);
        var rotation = MechWarriorCoordinateSystem.ToGodotRotation(effectObject.Transform.RotationDegrees);
        var basis = Basis.FromEuler(rotation * (Mathf.Pi / 180.0f)).Scaled(scale);
        var transform = new Transform3D(basis, position);
        var hasBounds = false;
        var bounds = new Aabb();
        foreach (var vertex in model.Vertices)
        {
            var transformed = transform *
                (MechWarriorCoordinateSystem.ToGodotPosition(vertex.Position) *
                 MechWarriorModelMeshBuilder.SourceUnitScale);
            var point = new Aabb(transformed, Vector3.Zero);
            bounds = hasBounds ? bounds.Merge(point) : point;
            hasBounds = true;
        }

        return bounds;
    }

    private void ConfigureDepthCue(float? shadeDistance, float? viewDistance)
    {
        if (m_environment == null)
        {
            return;
        }

        var visibleDistance = Mathf.Clamp(
            viewDistance ?? DefaultFogDistance,
            MinimumFogDistance,
            MaximumFogDistance);
        var depthCueDistance = shadeDistance is > 0.0f
            ? Mathf.Min(shadeDistance.Value, visibleDistance)
            : visibleDistance;

        // MW2 applies its palette depth cue from the viewer outward. The authored LITE shade
        // distance controls when terrain has fully converged on the horizon colour; VDIST is
        // the farther visibility limit, rather than the point where the colour shift starts.
        m_environment.FogDepthBegin = 0.0f;
        m_environment.FogDepthEnd = depthCueDistance;
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

    private static IReadOnlyList<SceneryObstacle> GetSceneryObstacles(
        IReadOnlyList<SceneryObstacle> staticObstacles,
        IEnumerable<BattlefieldActor> actors)
    {
        var obstacles = new List<SceneryObstacle>(staticObstacles);
        foreach (var actor in actors)
        {
            if (actor.SceneryObstacle == null)
            {
                continue;
            }

            obstacles.Add(actor.SceneryObstacle);
        }

        return obstacles;
    }

    private static bool TryCreateSceneryObstacle(
        string name,
        IReadOnlyList<SceneryWallTriangle> walls,
        out SceneryObstacle obstacle)
    {
        if (walls.Count == 0)
        {
            obstacle = null;
            return false;
        }

        var points = walls.SelectMany(wall => new[] { wall.A, wall.B, wall.C }).ToArray();
        obstacle = new SceneryObstacle(
            name,
            new System.Numerics.Vector2(points.Min(point => point.X), points.Min(point => point.Y)),
            new System.Numerics.Vector2(points.Max(point => point.X), points.Max(point => point.Y)),
            walls);
        return true;
    }

    private static IReadOnlyList<SceneryWallTriangle> BuildSceneryWalls(
        Transform3D transform,
        IReadOnlyList<MechWarriorModel> models)
    {
        const float maximumFloorNormal = 0.8f;
        var walls = new List<SceneryWallTriangle>();
        foreach (var model in models)
        {
            if (model.Vertices.Count == 0)
            {
                continue;
            }

            var transformedVertices = model.Vertices
                .Select(vertex => TransformVertex(transform, vertex))
                .ToArray();
            var modelHeight = transformedVertices.Max(vertex => vertex.Y) -
                              transformedVertices.Min(vertex => vertex.Y);
            if (modelHeight < MinimumSceneryObstacleHeight)
            {
                continue;
            }

            foreach (var polygon in model.Polygons)
            {
                for (var triangleIndex = 1; triangleIndex < polygon.VertexIndices.Count - 1; triangleIndex++)
                {
                    var first = transformedVertices[polygon.VertexIndices[0]];
                    var second = transformedVertices[polygon.VertexIndices[triangleIndex]];
                    var third = transformedVertices[polygon.VertexIndices[triangleIndex + 1]];
                    var normal = (second - first).Cross(third - first);
                    if (normal.LengthSquared() <= 0.000001f ||
                        Mathf.Abs(normal.Normalized().Y) > maximumFloorNormal)
                    {
                        continue;
                    }

                    walls.Add(new SceneryWallTriangle(
                        new System.Numerics.Vector2(first.X, first.Z),
                        new System.Numerics.Vector2(second.X, second.Z),
                        new System.Numerics.Vector2(third.X, third.Z)));
                }
            }
        }

        return walls.AsReadOnly();
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
        MechWarriorLuminosityTable luminosityTable,
        ICollection<DebugTriangle> debugTriangles)
    {
        const float margin = 1000.0f;
        var sourcePaletteIndex = terrainPaletteCounts.MaxBy(entry => entry.Value).Key;
        var litPaletteIndex = luminosityTable.GetPaletteIndex(
            sourcePaletteIndex,
            GeneralIlluminationLevel);
        var groundColor = ToGodotColor(palette[litPaletteIndex]);
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
                    AlbedoColor = groundColor,
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
            $"terrain palette index {sourcePaletteIndex} -> luminosity palette index {litPaletteIndex}).");
    }

    /// <summary>
    /// Presents map-authored dropship geometry for deployment and extraction.
    /// </summary>
    /// <summary>
    /// Applies an original palette-cycle task to a small point light.
    /// </summary>
    private sealed partial class AnimatedLocatorLight : OmniLight3D
    {
        private const float FrameSeconds = 0.09f;
        private readonly Color[] m_colors;
        private readonly StandardMaterial3D m_bulbMaterial;
        private float m_elapsed;

        public AnimatedLocatorLight(Color[] colors)
        {
            ArgumentNullException.ThrowIfNull(colors);
            if (colors.Length == 0)
            {
                throw new ArgumentException("An animated light needs at least one palette colour.", nameof(colors));
            }

            m_colors = colors;
            LightColor = colors[0];
            m_bulbMaterial = new StandardMaterial3D
            {
                AlbedoColor = colors[0],
                EmissionEnabled = true,
                Emission = colors[0],
                EmissionEnergyMultiplier = 5.0f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            AddChild(new MeshInstance3D
            {
                Name = "IndicatorBulb",
                Mesh = new SphereMesh { Radius = 0.35f, Height = 0.70f },
                MaterialOverride = m_bulbMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }

        public override void _Process(double delta)
        {
            m_elapsed += (float)delta;
            var color = m_colors[(int)(m_elapsed / FrameSeconds) % m_colors.Length];
            LightColor = color;
            m_bulbMaterial.AlbedoColor = color;
            m_bulbMaterial.Emission = color;
        }
    }

    /// <remarks>
    /// The set piece is discovered from a BWD <c>drop</c> task rather than a
    /// mission name. Its child models retain their offsets around the authored
    /// include anchor, while mission start and final-navigation data provide
    /// the deployment and extraction anchors.
    /// </remarks>
    private sealed partial class MissionDropShipSetPiece : Node3D
    {
        private const float FlightHeight = 65.0f;
        private const float LiftSeconds = 4.0f;
        private const float DepartureAcceleration = 30.0f;
        private const float DepartureInitialSpeed = 18.0f;
        private const float DepartureCullDistance = 1400.0f;
        private const float DepartureBankDegrees = -7.0f;
        private const float DepartureBankSeconds = 1.5f;
        private const float ExtractionApproachDistance = 1600.0f;
        private const float ExtractionApproachHeight = 300.0f;
        private const float ExtractionFinalHeight = 80.0f;
        private const float ExtractionApproachSpeed = 110.0f;
        private const float ExtractionDescentSpeed = 10.0f;
        private const float ColorFrameSeconds = 0.09f;

        private readonly string m_sourceName;
        private readonly Vector3 m_deploymentAnchor;
        private readonly Vector3 m_extractionAnchor;
        private readonly Vector3 m_deploymentDirection;
        private readonly Vector3 m_flightRotation;
        private readonly AudioStreamPlayer3D m_engine;
        private readonly List<(
            MeshInstance3D Mesh,
            Light3D Light,
            StandardMaterial3D Material,
            Color[] Colors)> m_animatedColors = [];
        private float m_elapsed;
        private float m_colorElapsed;
        private float m_landingOffset;
        private bool m_extracting;
        private bool m_active;

        public event Action ExtractionLanded;

        public MissionDropShipSetPiece(
            string sourceName,
            Vector3 deploymentAnchor,
            Vector3 extractionAnchor,
            Vector3 deploymentDirection,
            AudioStreamWav engineSound)
        {
            m_sourceName = sourceName;
            m_deploymentAnchor = deploymentAnchor;
            m_extractionAnchor = extractionAnchor;
            m_deploymentDirection = deploymentDirection.Normalized();
            m_flightRotation = new Vector3(
                0.0f,
                Mathf.RadToDeg(Mathf.Atan2(m_deploymentDirection.X, m_deploymentDirection.Z)),
                0.0f);
            RotationDegrees = m_flightRotation;
            if (engineSound != null)
            {
                m_engine = new AudioStreamPlayer3D
                {
                    Name = "Engine",
                    Stream = engineSound,
                    UnitSize = 30.0f,
                    MaxDistance = 1200.0f,
                    VolumeDb = -3.0f,
                    AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance
                };
                AddChild(m_engine);
            }
        }

        public void BeginDeployment()
        {
            Activate(false);
            GD.Print($"MechRewired: started map-authored deployment dropship {m_sourceName}.");
        }

        public void BeginExtraction()
        {
            Activate(true);
            GD.Print($"MechRewired: started map-authored extraction dropship {m_sourceName}.");
        }

        public void ConfigureAssemblyBounds(Aabb bounds)
        {
            m_landingOffset = Math.Max(0.0f, -bounds.Position.Y) + 0.15f;
            GD.Print(
                $"MechRewired: dropship {m_sourceName} landing offset {m_landingOffset:F2}m " +
                $"from assembly bounds {bounds}.");
        }

        public void ConfigureAnimatedColor(
            MeshInstance3D meshInstance,
            Light3D light,
            Color[] colors)
        {
            ArgumentNullException.ThrowIfNull(meshInstance);
            ArgumentNullException.ThrowIfNull(light);
            ArgumentNullException.ThrowIfNull(colors);
            if (colors.Length == 0)
            {
                return;
            }

            var material = new StandardMaterial3D
            {
                AlbedoColor = colors[0],
                EmissionEnabled = true,
                Emission = colors[0],
                EmissionEnergyMultiplier = 3.5f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
            meshInstance.MaterialOverride = material;
            meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            m_animatedColors.Add((meshInstance, light, material, colors));
        }

        public override void _Process(double delta)
        {
            if (!m_active)
            {
                return;
            }

            m_elapsed += (float)delta;
            UpdateAnimatedColors((float)delta);
            if (!m_extracting)
            {
                UpdateDeparture();
                return;
            }

            UpdateExtraction();
        }

        private void Activate(bool extracting)
        {
            m_extracting = extracting;
            m_elapsed = 0.0f;
            m_active = true;
            Visible = true;
            SetEngineFlamesVisible(true);
            RotationDegrees = m_flightRotation;
            var anchor = GetLandingPosition(extracting ? m_extractionAnchor : m_deploymentAnchor);
            Position = extracting
                ? anchor - m_deploymentDirection * ExtractionApproachDistance +
                  Vector3.Up * ExtractionApproachHeight
                : anchor;
            m_engine?.Play();
        }

        private void UpdateExtraction()
        {
            var approachSeconds = ExtractionApproachDistance / ExtractionApproachSpeed;
            var landingPosition = GetLandingPosition(m_extractionAnchor);
            if (m_elapsed <= approachSeconds)
            {
                var approachProgress = m_elapsed / approachSeconds;
                Position = landingPosition -
                           m_deploymentDirection * ExtractionApproachDistance * (1.0f - approachProgress) +
                           Vector3.Up * Mathf.Lerp(
                               ExtractionApproachHeight,
                               ExtractionFinalHeight,
                               approachProgress);
                return;
            }

            var descentSeconds = (m_elapsed - approachSeconds);
            var height = Math.Max(0.0f, ExtractionFinalHeight - descentSeconds * ExtractionDescentSpeed);
            Position = landingPosition + Vector3.Up * height;
            if (height > 0.0f)
            {
                return;
            }

            m_active = false;
            m_engine?.Stop();
            SetEngineFlamesVisible(false);
            GD.Print($"MechRewired: extraction dropship {m_sourceName} landed.");
            ExtractionLanded?.Invoke();
        }

        private void UpdateAnimatedColors(float delta)
        {
            m_colorElapsed += delta;
            foreach (var (_, _, material, colors) in m_animatedColors)
            {
                var index = (int)(m_colorElapsed / ColorFrameSeconds) % colors.Length;
                material.AlbedoColor = colors[index];
                material.Emission = colors[index];
            }
        }

        private void UpdateDeparture()
        {
            var landingPosition = GetLandingPosition(m_deploymentAnchor);
            if (m_elapsed <= LiftSeconds)
            {
                var liftProgress = Mathf.SmoothStep(0.0f, 1.0f, m_elapsed / LiftSeconds);
                Position = landingPosition + Vector3.Up * (FlightHeight * liftProgress);
                RotationDegrees = m_flightRotation;
                return;
            }

            var flightSeconds = m_elapsed - LiftSeconds;
            var bankProgress = Mathf.SmoothStep(
                0.0f,
                1.0f,
                Math.Clamp(flightSeconds / DepartureBankSeconds, 0.0f, 1.0f));
            RotationDegrees = m_flightRotation +
                              new Vector3(0.0f, 0.0f, DepartureBankDegrees * bankProgress);
            var departureDistance = DepartureInitialSpeed * flightSeconds +
                                    0.5f * DepartureAcceleration * flightSeconds * flightSeconds;
            Position = landingPosition + Vector3.Up * FlightHeight +
                       m_deploymentDirection * departureDistance;
            if (departureDistance < DepartureCullDistance)
            {
                return;
            }

            Visible = false;
            m_active = false;
            m_engine?.Stop();
            GD.Print($"MechRewired: deployment dropship {m_sourceName} departed beyond view range.");
        }

        private Vector3 GetLandingPosition(Vector3 groundAnchor) =>
            groundAnchor + Vector3.Up * m_landingOffset;

        private void SetEngineFlamesVisible(bool visible)
        {
            foreach (var (mesh, light, _, _) in m_animatedColors)
            {
                mesh.Visible = visible;
                light.Visible = visible;
            }
        }
    }
}
