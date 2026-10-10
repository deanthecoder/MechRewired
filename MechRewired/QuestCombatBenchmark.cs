// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using System.Diagnostics;
using System.Text.Json;
using Godot;
using MechRewired.Rendering;

namespace MechRewired;

/// <summary>Short live encounter suite. Fresh missions isolate damage, ammunition and lazy pools.</summary>
public sealed partial class QuestCombatBenchmark : Node
{
    private const double TrialSeconds = 12;
    private const double SimulationWarmupSeconds = 3;
    private static readonly (string Name, float ChunkSizeMetres)[] Variants =
        [("baseline", 0), ("player-ground-fast", 0), ("baseline", 0)];
    private static Session s_session;
    private readonly PlayerMech m_player;
    private readonly PlayerHud m_hud;
    private readonly QuestGraphicsSettings m_settings;
    private readonly MissionSkyController m_sky;
    private readonly TerrainSurfaceIndex m_terrain;
    private readonly TerrainRockScatter m_rocks;
    private readonly IReadOnlyList<EnemyMech> m_enemies;
    private readonly string m_mission;
    private bool m_cancelled;
    private bool m_reloadRequested;
    private Label3D m_label;
    private TerrainBenchmarkGraphics m_terrainGraphics;
    private QuestCombatDiagnostics m_diagnostics;

    private sealed class Session(string target, string mission, Options options)
    {
        public readonly string RunId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        public readonly string Target = target;
        public readonly string Mission = mission;
        public readonly Options Settings = options;
        public int Trial;
        public bool Restoring;
        public string Result = "complete";
    }

    private sealed record Options(bool SunShadows, bool Glow, bool Glass, bool Smoke, bool BakedSky,
        bool QuestUvMaterials, bool BakedInteriorLighting,
        bool HudGlow, bool Radar, bool Weapons, bool Status, bool Navigation, bool Targeting);

    private readonly record struct Frame(double Seconds, double Ms, double GpuMs, double CpuMs,
        double ProcessMs, double PhysicsMs, double DrawCalls, double Primitives, long AllocatedBytes, int Gc0, int Gc1, int Gc2,
        QuestCombatTelemetrySnapshot Combat, QuestCpuTelemetry.Snapshot CpuScopes, ulong PhysicsSteps,
        float HeadTranslation, float HeadAngle);

    public static bool IsSuiteActive => s_session != null;

    public QuestCombatBenchmark(PlayerMech player, PlayerHud hud, QuestGraphicsSettings settings,
        MissionSkyController sky, TerrainSurfaceIndex terrain, TerrainRockScatter rocks,
        IReadOnlyList<EnemyMech> enemies, string mission)
    {
        m_player = player; m_hud = hud; m_settings = settings; m_sky = sky;
        m_terrain = terrain; m_rocks = rocks; m_enemies = enemies; m_mission = mission;
        Name = "QuestCombatBenchmark";
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        if (s_session != null)
        {
            GetTree().Paused = true;
            Callable.From(ContinueSuite).CallDeferred();
        }
        else if (OS.GetCmdlineUserArgs().Contains("--quest-combat-benchmark") && !s_commandLineStarted)
        {
            s_commandLineStarted = true;
            Callable.From(Start).CallDeferred();
        }
    }
    private static bool s_commandLineStarted;
    private static long s_logRecordId;

    /// <summary>Menu action explicitly labelled as a mission reset. Never resumes the old damaged mission.</summary>
    public void Start()
    {
        if (IsSuiteActive || m_player.VrRig.BenchmarkActive) return;
        var target = m_enemies.Where(e => !e.IsDestroyed)
            .OrderBy(e => e.IsStationaryEmplacement).ThenByDescending(e => e.HasMissileWeapons)
            .ThenByDescending(e => e.MaximumHealth).ThenBy(e => e.Name.ToString(), StringComparer.Ordinal).FirstOrDefault();
        if (target == null)
        {
            m_player.VrRig.Menu.ShowMissionResult("COMBAT TEST: NO ENEMY AVAILABLE");
            return;
        }
        s_session = new Session(target.Name, m_mission, CaptureOptions());
        if (!QuestCombatLogArchive.BeginRun(s_session.RunId))
        {
            s_session = null;
            m_player.VrRig.Menu.ShowMissionResult("COMBAT TEST: CANNOT SAVE LOG");
            QuestCombatLogArchive.EndRun();
            return;
        }
        PrintJson("RUN", JsonSerializer.Serialize(new
        {
            runId = s_session.RunId, schema = 12, graphicsProfile = "baked-profile", poolPolicy = "quest-prewarmed-per-mech-v1", mission = m_mission, target = s_session.Target,
            effectPolicy = "player-grounding-v1",
            skyCache = QuestCachedSky.CacheVersion,
            shadowPolicy = "quest-objects-500m-4096-two-cascades-v1",
            buildingSmokePolicy = "quest-ambient-sooty-unshaded-v1",
            weaponEffectPoolPolicy = "mission64-per-family-limit128-v1",
            missileQueryPolicy = "pooled-parameters-disposed-results-v1",
            missileGuidancePolicy = "salvo-target-velocity-per-frame-v1",
            missileVisualPolicy = "quest-per-mech-launch-smoke3-light8-v1",
            targetingPolicy = "segment-bounds-quest-250ms-v1",
            variants = Variants.Select(stage => stage.Name).ToArray(), secondsPerTrial = TrialSeconds, simulationWarmupSeconds = SimulationWarmupSeconds,
            physicsTicksPerSecond = Engine.PhysicsTicksPerSecond, archivePath = QuestCombatLogArchive.PrimaryPath, enemyCount = m_enemies.Count,
            options = s_session.Settings,
            baselineSettings = new { bakedProfile = true, smoke = true, weaponLights = true,
                terrainVertexLighting = true, terrainSpecularDisabled = true, terrainChunkSizeMetres = 0,
                playerGroundingPolicy = "legacy-foot-vertices" },
            build = OS.HasFeature("debug") ? "debug" : "release", engine = Engine.GetVersionInfo()["string"].ToString(),
            device = OS.GetModelName(), gpu = RenderingServer.GetVideoAdapterName(),
            renderer = RenderingServer.GetCurrentRenderingMethod(), msaa = GetViewport().Msaa3D.ToString(),
            terrainTriplanar = false, savedTerrainTriplanar = m_settings.TerrainTriplanarEnabled,
            note = "Fresh mission per trial; 3s identical live warmup then 12s measured. Bracketing baselines use legacy player foot vertices; candidate skips the hidden rig and caches stationary surface queries. XR tracking stays active. Production Quest pools prewarmed; runtime pool creation is unexpected. Driver/shader caches may remain warm. Final mission also resets."
        }));
        if (QuestCombatLogArchive.PrimaryError != null)
        {
            s_session = null;
            QuestCombatLogArchive.EndRun();
            m_player.VrRig.Menu.ShowMissionResult("COMBAT TEST: CANNOT SAVE LOG");
            return;
        }
        Reload();
    }

    private async void ContinueSuite()
    {
        var session = s_session;
        if (session == null) return;
        var rig = m_player.VrRig;
        var viewport = GetViewport();
        var restoring = session.Restoring;
        var tree = GetTree();
        var previousActive = QuestCombatTelemetry.Active;
        var previousLights = QuestCombatTelemetry.WeaponLightsDisabled;
        var previousSmoke = QuestCombatTelemetry.SmokeDisabled;
        var previousReducedMissileSmoke = QuestCombatTelemetry.ReducedMissileSmoke;
        var previousSmallProjectileLights = QuestCombatTelemetry.SmallProjectileLights;
        var previousDetailedBuildingSmoke = m_settings.DetailedBuildingSmokeEnabled;
        try
        {
            ApplyOptions(session.Settings);
            if (!session.Restoring)
            {
                m_settings.BakedProfileEnabled = true;
                m_settings.SmokeAndDustEnabled = true;
            }
            await m_sky.WaitForSkyBakeAsync();
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
            if (m_settings.BakedSkyEnabled != (session.Restoring ? session.Settings.BakedSky : true))
                throw new InvalidOperationException("Could not restore the requested sky setting for this trial.");
            if (session.Restoring)
            {
                s_session = null;
                PrintJson("STATUS", JsonSerializer.Serialize(new { runId = session.RunId, status = session.Result }));
                QuestCombatLogArchive.EndRun();
                if (QuestCombatLogArchive.PrimaryError != null) session.Result = "archive-failed";
                if (session.Result == "complete")
                {
                    rig.Menu.Close();
                    GD.Print($"QUEST_COMBAT_RESUMED: mission={m_mission} paused={GetTree().Paused}");
                }
                else
                    rig.Menu.ShowMissionResult("COMBAT TEST " + session.Result.ToUpperInvariant() + " / LOGGED");
                return;
            }
            if (session.Mission != m_mission) throw new InvalidOperationException("Mission changed during the combat suite.");
            var enemy = m_enemies.FirstOrDefault(e => e.Name == session.Target);
            if (enemy == null) throw new InvalidOperationException("The selected enemy is missing after mission restart.");
            rig.Menu.Close();
            GetTree().Paused = true;
            rig.BenchmarkActive = true;
            rig.BenchmarkCancelRequested = () => m_cancelled = true;
            var stage = Variants[session.Trial];
            var variant = stage.Name;
            // Select grounding before pose setup and both warmups, not after measurement starts.
            QuestCombatDiagnostics.LegacyPlayerGrounding = variant == "baseline";
            m_player.StopVrMovement();
            m_player.GlobalTransform = FindEncounterPose(enemy);
            m_player.SetVrTorsoAim(0, 0);
            m_rocks.ConfigureObserver(m_player);
            // Keep global smoke and weapon lights enabled in every measured stage.
            // Hold terrain rendering fixed while isolating callback costs. All changes are trial-local.
            m_settings.SmokeAndDustEnabled = true;
            QuestCombatTelemetry.SmokeDisabled = false;
            QuestCombatTelemetry.WeaponLightsDisabled = false;
            QuestCombatTelemetry.ReducedMissileSmoke = false;
            QuestCombatTelemetry.SmallProjectileLights = false;
            m_settings.DetailedBuildingSmokeEnabled = false;
            m_terrainGraphics = new TerrainBenchmarkGraphics(GetTree().CurrentScene,
                stage.ChunkSizeMetres > 0, vertexLit: true,
                chunkSizeMetres: stage.ChunkSizeMetres > 0 ? stage.ChunkSizeMetres : TerrainBenchmarkGraphics.ChunkSizeMetres);
            m_label = new Label3D { Text = "COMBAT / " + variant + "\nKeep head still; Menu cancels", FontSize = 22,
                Position = new Vector3(0, .30f, -1.2f), PixelSize = .00065f, NoDepthTest = true };
            rig.Camera.AddChild(m_label);
            // Warm the static scene without simulating combat. Production Quest pools are already prepared.
            var warmup = Stopwatch.StartNew();
            while (warmup.Elapsed.TotalSeconds < 1 && !m_cancelled)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
            if (m_cancelled) throw new OperationCanceledException();
            var xr = XRServer.FindInterface("OpenXR") as OpenXRInterface;
            var hz = xr?.IsInitialized() == true ? xr.DisplayRefreshRate : 72;
            PrintJson("TRIAL", JsonSerializer.Serialize(new { schema = 12, effectPolicy = "player-grounding-v1", runId = session.RunId, trial = session.Trial+1,
                terrainChunked = stage.ChunkSizeMetres > 0,
                terrainVertexLighting = true,
                terrainSpecularDisabled = true,
                terrainChunkSizeMetres = stage.ChunkSizeMetres,
                terrainChunkCount = m_terrainGraphics.ChunkCount,
                terrainSourceTriangles = m_terrainGraphics.SourceTriangles,
                terrainChunkTriangles = m_terrainGraphics.ChunkTriangles, terrainTriplanar = false,
                buildingSmokeMaterial = m_settings.DetailedBuildingSmokeEnabled ? "detailed-lit" : "simple-unshaded",
                buildingSmokeEmitters = m_settings.BuildingSmokeEmitterCount,
                playerGroundingPolicy = variant == "baseline" ? "legacy-foot-vertices" : "quest-hidden-rig-cached-surface-v1",
                enemyUpdateStride = 1,
                hudFrozen = false, simulationFrozen = false,
                diagnostic = false,
                simulationWarmupSeconds = SimulationWarmupSeconds, physicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
                variant, graphicsProfile = "baked-profile", target = session.Target, pose = m_player.GlobalTransform.ToString(), hz,
                missilePoolCount = m_enemies.Sum(e => e.MissilePoolCount),
                missilePoolsReady = m_enemies.Where(e => e.HasMissileWeapons).All(e => e.MissilePoolReady),
                bakedSky = m_settings.BakedSkyEnabled, smoke = m_settings.SmokeAndDustEnabled,
                missileSmoke = !QuestCombatTelemetry.SmokeDisabled, weaponLights = !QuestCombatTelemetry.WeaponLightsDisabled,
                enemyMissileSmoke = !QuestCombatTelemetry.ReducedMissileSmoke,
                missileSmokeScale = QuestCombatTelemetry.ReducedMissileSmoke ? 0.75 : 1.0,
                projectileLightScale = QuestCombatTelemetry.SmallProjectileLights ? 0.5 : 1.0,
                cockpitUv = m_settings.QuestUvMaterialsEnabled,
                bakedInteriorLighting = m_settings.QuestUvMaterialsEnabled && m_settings.BakedInteriorLightingEnabled }));
            EnsureArchive();
            // Start every trial from the same amount of live combat under its grounding policy.
            // Track warmup projectile lifetimes; reset interval counters before timing. No file I/O here.
            GetTree().Paused = false;
            QuestCombatTelemetry.Active = true;
            var simulationWarmup = Stopwatch.StartNew();
            var warmupShot = 0.5;
            while (simulationWarmup.Elapsed.TotalSeconds < SimulationWarmupSeconds && !m_cancelled)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
                if (simulationWarmup.Elapsed.TotalSeconds >= warmupShot)
                {
                    m_player.VrFire();
                    warmupShot += 2;
                }
            }
            GetTree().Paused = true;
            if (m_cancelled) throw new OperationCanceledException();
            m_diagnostics = new QuestCombatDiagnostics();
            m_diagnostics.Apply(tree.CurrentScene, variant);
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);
            var frames = new List<Frame>(2400);
            var hudDrawsAtStart = m_hud.VrInstrumentDrawCount;
            var head = rig.Camera.Transform;
            QuestCombatTelemetry.Reset();
            QuestCpuTelemetry.Reset();
            QuestCombatTelemetry.Active = true;
            var allocated = GC.GetTotalAllocatedBytes(false);
            var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
            var clock = Stopwatch.StartNew();
            var previous = clock.Elapsed.TotalSeconds;
            var previousPhysicsFrame = Engine.GetPhysicsFrames();
            var nextShot = 0.5;
            GetTree().Paused = false;
            while (clock.Elapsed.TotalSeconds < TrialSeconds && !m_cancelled && !m_player.IsDestroyed && !enemy.IsDestroyed)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
                var now = clock.Elapsed.TotalSeconds;
                var physicsFrame = Engine.GetPhysicsFrames();
                var totalAllocated = GC.GetTotalAllocatedBytes(false);
                var c0 = GC.CollectionCount(0); var c1 = GC.CollectionCount(1); var c2 = GC.CollectionCount(2);
                frames.Add(new Frame(now, (now-previous)*1000,
                    RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid()),
                    RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport.GetViewportRid()),
                    Performance.GetMonitor(Performance.Monitor.TimeProcess)*1000,
                    Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess)*1000,
                    Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                    Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
                    Math.Max(0,totalAllocated-allocated), c0-gc0,c1-gc1,c2-gc2, QuestCombatTelemetry.SnapshotAndReset(),
                    QuestCpuTelemetry.SnapshotAndReset(), physicsFrame - previousPhysicsFrame,
                    rig.Camera.Position.DistanceTo(head.Origin),
                    Mathf.RadToDeg(rig.Camera.Basis.GetRotationQuaternion().AngleTo(head.Basis.GetRotationQuaternion()))));
                previousPhysicsFrame = physicsFrame;
                previous = now; allocated = totalAllocated; gc0=c0; gc1=c1; gc2=c2;
                if (variant != "simulation-frozen" && now >= nextShot)
                {
                    m_player.VrFire();
                    nextShot += 2;
                }
            }
            GetTree().Paused = true;
            QuestCombatTelemetry.Active = false;
            var status = m_cancelled ? "cancelled" : m_player.IsDestroyed ? "player-destroyed" : enemy.IsDestroyed ? "target-destroyed" : "complete";
            Report(session, variant, status, frames, hz > 0 ? 1000.0/hz : 1000.0/72, m_hud.VrInstrumentDrawCount - hudDrawsAtStart);
            EnsureArchive();
            if (status != "complete") { session.Result = status; session.Restoring = true; }
            else if (++session.Trial == Variants.Length) session.Restoring = true;
        }
        catch (Exception error)
        {
            session.Result = error is OperationCanceledException ? "cancelled" : "failed";
            session.Restoring = true;
            PrintJson("ERROR", JsonSerializer.Serialize(new { runId = session.RunId, trial = session.Trial + 1, error = error.ToString() }));
            GD.PushWarning("QUEST_COMBAT_ERROR: " + error.Message);
            if (restoring)
            {
                PrintJson("STATUS", JsonSerializer.Serialize(new { runId = session.RunId, status = "restore-failed" }));
                QuestCombatLogArchive.EndRun();
                s_session = null;
                if (GodotObject.IsInstanceValid(rig)) rig.Menu.ShowMissionResult("COMBAT TEST: RESTORE FAILED");
            }
            if (s_session == null) return;
        }
        finally
        {
            QuestCombatDiagnostics.LegacyPlayerGrounding = false;
            m_diagnostics?.Dispose();
            m_diagnostics = null;
            m_terrainGraphics?.Dispose();
            m_terrainGraphics = null;
            QuestCombatTelemetry.Active = previousActive;
            QuestCombatTelemetry.WeaponLightsDisabled = previousLights;
            QuestCombatTelemetry.SmokeDisabled = previousSmoke;
            QuestCombatTelemetry.ReducedMissileSmoke = previousReducedMissileSmoke;
            QuestCombatTelemetry.SmallProjectileLights = previousSmallProjectileLights;
            m_settings.DetailedBuildingSmokeEnabled = previousDetailedBuildingSmoke;
            if (GodotObject.IsInstanceValid(rig)) { rig.BenchmarkActive = false; rig.BenchmarkCancelRequested = null; }
            if (GodotObject.IsInstanceValid(m_label)) m_label.QueueFree();
            if (GodotObject.IsInstanceValid(viewport)) RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), false);
        }
        if (s_session != null && GodotObject.IsInstanceValid(this) && IsInsideTree()) Reload();
    }

    private void Report(Session session, string variant, string status, List<Frame> frames, double budget, long hudInstrumentDraws)
    {
        if (frames.Count == 0) return;
        var stats = BenchmarkFrameStatistics.Calculate(frames.Select(f => f.Ms), budget);
        double? Gpu(IEnumerable<Frame> values) { var usable = values.Select(f => f.GpuMs).Where(v => v > 0).ToArray(); return usable.Length == 0 ? null : usable.Average(); }
        var first = frames.FindIndex(f => f.Combat.WeaponLaunches > 0 || f.Combat.MissileLaunches > 0);
        var firstTime = first >= 0 ? frames[first].Seconds : double.NaN;
        var firstVolley = frames.Where(f => f.Seconds >= firstTime && f.Seconds < firstTime + 1).ToArray();
        var repeats = frames.Where(f => f.Seconds >= firstTime + 1).ToArray();
        var firstImpact = frames.FindIndex(f => f.Combat.Impacts > 0);
        var summary = new
        {
            schema = 12, effectPolicy = "player-grounding-v1",
            terrainChunked = Variants[session.Trial].ChunkSizeMetres > 0,
            terrainVertexLighting = true,
            terrainSpecularDisabled = true,
            terrainChunkSizeMetres = Variants[session.Trial].ChunkSizeMetres,
            terrainChunkCount = m_terrainGraphics.ChunkCount,
            terrainSourceTriangles = m_terrainGraphics.SourceTriangles,
            terrainChunkTriangles = m_terrainGraphics.ChunkTriangles, terrainTriplanar = false,
            buildingSmokeMaterial = m_settings.DetailedBuildingSmokeEnabled ? "detailed-lit" : "simple-unshaded",
            buildingSmokeEmitters = m_settings.BuildingSmokeEmitterCount,
            sunShadows = m_settings.SunShadowsEnabled, sunShadowDistance = m_sky.SunShadowDistance,
            shadowPolicy = "quest-objects-500m-4096-two-cascades-v1",
            graphicsProfile = "baked-profile", bakedSky = m_settings.BakedSkyEnabled,
            cockpitUv = m_settings.QuestUvMaterialsEnabled,
            bakedInteriorLighting = m_settings.QuestUvMaterialsEnabled && m_settings.BakedInteriorLightingEnabled,
            smoke = m_settings.SmokeAndDustEnabled, missileSmoke = !QuestCombatTelemetry.SmokeDisabled,
            weaponLights = !QuestCombatTelemetry.WeaponLightsDisabled,
            enemyMissileSmoke = !QuestCombatTelemetry.ReducedMissileSmoke,
            missileSmokeScale = QuestCombatTelemetry.ReducedMissileSmoke ? 0.75 : 1.0,
            projectileLightScale = QuestCombatTelemetry.SmallProjectileLights ? 0.5 : 1.0,
            missileSmokeStride = MechRewired.Simulation.MissileVisualCadence.SmokeStride,
            missileLightStride = MechRewired.Simulation.MissileVisualCadence.LightStride,
            playerGroundingPolicy = variant == "baseline" ? "legacy-foot-vertices" : "quest-hidden-rig-cached-surface-v1",
            enemyUpdateStride = 1,
            hudFrozen = false, simulationFrozen = false,
            diagnostic = false,
            simulationWarmupSeconds = SimulationWarmupSeconds, physicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
            physicsSteps = frames.Average(f => (double)f.PhysicsSteps), maxPhysicsSteps = frames.Max(f => f.PhysicsSteps),
            slowFrameCount = frames.Count(f => f.Ms > budget), gcFrameCount = frames.Count(f => f.Gc0 + f.Gc1 + f.Gc2 > 0),
            cpuScopes = SummarizeCpuScopes(frames),
            runId = session.RunId, trial = session.Trial+1, variant, status, target = session.Target, frames = frames.Count,
            hudInstrumentDraws, meanMs = stats.MeanFrameMs, p95Ms = stats.P95FrameMs, p99Ms = stats.P99FrameMs,
            averageFps = stats.AverageFps, onePercentLowFps = stats.OnePercentLowFps,
            firstVolleyMeanMs = firstVolley.Length > 0 ? (double?)firstVolley.Average(f => f.Ms) : null,
            firstVolleyMaxMs = firstVolley.Length > 0 ? (double?)firstVolley.Max(f => f.Ms) : null,
            repeatedVolleyMeanMs = repeats.Length > 0 ? (double?)repeats.Average(f => f.Ms) : null,
            firstImpactFrameMs = firstImpact >= 0 ? (double?)frames[firstImpact].Ms : null,
            cpuMs = frames.Average(f => f.CpuMs), gpuMs = Gpu(frames), physicsMs = frames.Average(f=>f.PhysicsMs), processMs = frames.Average(f=>f.ProcessMs),
            renderCounterScope = "all-viewports-last-rendered-frame",
            drawCalls = frames.Average(f => f.DrawCalls), maxDrawCalls = frames.Max(f => f.DrawCalls),
            primitives = frames.Average(f => f.Primitives), maxPrimitives = frames.Max(f => f.Primitives),
            allocatedBytes = frames.Sum(f=>f.AllocatedBytes), gc0=frames.Sum(f=>f.Gc0), gc1=frames.Sum(f=>f.Gc1), gc2=frames.Sum(f=>f.Gc2),
            enemyAiMs=frames.Sum(f=>f.Combat.EnemyAiMilliseconds), losMs=frames.Sum(f=>f.Combat.LineOfSightMilliseconds),
            losCalls=frames.Sum(f=>f.Combat.LineOfSightCalls), poolBuilds=frames.Sum(f=>f.Combat.MissilePoolsCreated),
            poolBuildMs=frames.Sum(f=>f.Combat.MissilePoolCreationMilliseconds), weaponShots=frames.Sum(f=>f.Combat.WeaponLaunches),
            playerDirectRaycastCalls=frames.Sum(f=>f.Combat.PlayerDirectRaycastCalls),
            playerDirectRaycastMs=frames.Sum(f=>f.Combat.PlayerDirectRaycastMilliseconds),
            playerDirectDamageCalls=frames.Sum(f=>f.Combat.PlayerDirectDamageCalls),
            playerDirectDamageMs=frames.Sum(f=>f.Combat.PlayerDirectDamageMilliseconds),
            playerWeaponVisualCalls=frames.Sum(f=>f.Combat.PlayerWeaponVisualCalls),
            playerWeaponVisualMs=frames.Sum(f=>f.Combat.PlayerWeaponVisualMilliseconds),
            missileTerrainQueryCalls=frames.Sum(f=>f.Combat.MissileTerrainQueryCalls),
            missileTerrainQueryMs=frames.Sum(f=>f.Combat.MissileTerrainQueryMilliseconds),
            playerWorldRaycastMs=frames.Sum(f=>f.Combat.PlayerWorldRaycastMilliseconds),
            playerMechRaycastMs=frames.Sum(f=>f.Combat.PlayerMechRaycastMilliseconds),
            weaponEffectPoolBuilds=frames.Sum(f=>f.Combat.WeaponEffectPoolBuilds),
            weaponEffectPoolFallbacks=frames.Sum(f=>f.Combat.WeaponEffectPoolFallbacks),
            enemyShots=frames.Sum(f=>f.Combat.EnemyWeaponLaunches), missileLaunches=frames.Sum(f=>f.Combat.MissileLaunches), impacts=frames.Sum(f=>f.Combat.Impacts),
            maxHeadTranslation=frames.Max(f=>f.HeadTranslation), maxHeadAngle=frames.Max(f=>f.HeadAngle)
        };
        PrintJson("SUMMARY", JsonSerializer.Serialize(summary));
        // Keep every frame in the archive for later correlation; serialize only after timing.
        foreach (var frame in frames)
            QuestCombatLogArchive.AppendTaggedLine("QUEST_COMBAT_FRAME: " + JsonSerializer.Serialize(new { runId = session.RunId, trial = session.Trial + 1, frame }));
        QuestCombatLogArchive.Flush();
        // Also expose bounded diagnostic records through logcat.
        foreach (var frame in frames.OrderByDescending(f=>f.Ms).Take(20).OrderBy(f=>f.Seconds))
            PrintJson("SPIKE", JsonSerializer.Serialize(new {runId=session.RunId, trial=session.Trial+1, frame}));
        foreach (var bucket in frames.GroupBy(f=>(int)f.Seconds))
            PrintJson("SECOND", JsonSerializer.Serialize(new {runId=session.RunId, trial=session.Trial+1, second=bucket.Key,
                meanMs=bucket.Average(f=>f.Ms), maxMs=bucket.Max(f=>f.Ms), gpuMs=Gpu(bucket),
                drawCalls=bucket.Average(f=>f.DrawCalls), maxDrawCalls=bucket.Max(f=>f.DrawCalls),
                primitives=bucket.Average(f=>f.Primitives), maxPrimitives=bucket.Max(f=>f.Primitives),
                weaponShots=bucket.Sum(f=>f.Combat.WeaponLaunches), missileLaunches=bucket.Sum(f=>f.Combat.MissileLaunches),
                missileTerrainQueryCalls=bucket.Sum(f=>f.Combat.MissileTerrainQueryCalls),
                missileTerrainQueryMs=bucket.Sum(f=>f.Combat.MissileTerrainQueryMilliseconds),
                impacts=bucket.Sum(f=>f.Combat.Impacts), activeMissiles=bucket.Max(f=>f.Combat.ActiveMissiles), activeLasers=bucket.Max(f=>f.Combat.ActiveLasers)}));
        PrintJson("EVENTS", JsonSerializer.Serialize(new {runId=session.RunId, trial=session.Trial+1,
            firstVolleySeconds=first >= 0 ? (double?)frames[first].Seconds : null}));
        foreach (var frame in frames.Where(f=>f.Combat.WeaponLaunches>0 || f.Combat.MissileLaunches>0 || f.Combat.Impacts>0 || f.Combat.MissilePoolsCreated>0))
            PrintJson("EVENT", JsonSerializer.Serialize(new {runId=session.RunId, trial=session.Trial+1, frame.Seconds, frame.Ms, frame.DrawCalls, frame.Primitives, frame.Combat}));

    }

    private static Dictionary<string, QuestCpuTelemetry.Measurement> SummarizeCpuScopes(List<Frame> frames)
    {
        var totals = new Dictionary<string, QuestCpuTelemetry.Measurement>();
        foreach (var property in typeof(QuestCpuTelemetry.Snapshot).GetProperties())
        {
            var calls = 0; var ms = 0.0; long bytes = 0;
            foreach (var frame in frames)
            {
                var value = (QuestCpuTelemetry.Measurement)property.GetValue(frame.CpuScopes);
                calls += value.Calls; ms += value.ElapsedMs; bytes += value.AllocatedBytes;
            }
            totals[property.Name] = new QuestCpuTelemetry.Measurement(calls, ms, bytes);
        }
        return totals;
    }

    private Transform3D FindEncounterPose(EnemyMech enemy)
    {
        var target = enemy.TargetPosition;
        var feetOffset = m_player.GlobalPosition.Y - m_player.FeetElevation;
        var targetOffset = m_player.TargetPosition - m_player.GlobalPosition;
        foreach (var radius in new[] { 65.0f, 45.0f, 90.0f })
        {
            for (var direction = 0; direction < 8; direction++)
            {
                var angle = direction * Mathf.Tau / 8;
                var position = target + new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                if (!m_terrain.TryGetHeight(position, out var height)) continue;
                position.Y = height + feetOffset;
                if (!enemy.HasClearSightTo(position + targetOffset)) continue;
                return new Transform3D(Basis.LookingAt(new Vector3(target.X - position.X, 0, target.Z - position.Z), Vector3.Up), position);
            }
        }
        throw new InvalidOperationException("No terrain-supported position with clear enemy line of sight; combat trial not measured.");
    }

    private static void PrintJson(string kind, string json)
    {
        foreach (var line in QuestCombatLogChunker.Format(kind, json, Interlocked.Increment(ref s_logRecordId)))
        {
            QuestCombatLogArchive.AppendTaggedLine(line);
            GD.Print(line);
        }
        QuestCombatLogArchive.Flush();
    }

    private static void EnsureArchive()
    {
        if (QuestCombatLogArchive.PrimaryError != null)
            throw new IOException("Combat benchmark archive failed: " + QuestCombatLogArchive.PrimaryError);
    }

    private Options CaptureOptions() => new(m_settings.SunShadowsEnabled, m_settings.GlowEnabled,
        m_settings.CockpitGlassEnabled,m_settings.SmokeAndDustEnabled,m_settings.BakedSkyEnabled,
        m_settings.QuestUvMaterialsEnabled,m_settings.BakedInteriorLightingEnabled,
        m_settings.HudGlowEnabled,m_hud.ShowRadar,m_hud.ShowWeapons,m_hud.ShowStatus,m_hud.ShowNavigation,m_hud.ShowTargeting);
    private void ApplyOptions(Options o)
    {
        m_settings.SunShadowsEnabled=o.SunShadows; m_settings.GlowEnabled=o.Glow; m_settings.CockpitGlassEnabled=o.Glass;
        m_settings.QuestUvMaterialsEnabled=o.QuestUvMaterials;
        m_settings.BakedInteriorLightingEnabled=o.BakedInteriorLighting;
        m_settings.SmokeAndDustEnabled=o.Smoke; m_settings.HudGlowEnabled=o.HudGlow;
        m_hud.ShowRadar=o.Radar; m_hud.ShowWeapons=o.Weapons; m_hud.ShowStatus=o.Status;
        m_hud.ShowNavigation=o.Navigation; m_hud.ShowTargeting=o.Targeting;
        m_settings.BakedSkyEnabled=o.BakedSky;
    }
    private void Reload()
    {
        m_reloadRequested = true;
        GetTree().Paused = true;
        var tree = GetTree();
        Callable.From(() =>
        {
            var error = tree.ReloadCurrentScene();
            if (error == Error.Ok) return;
            m_reloadRequested = false;
            PrintJson("STATUS", JsonSerializer.Serialize(new { runId = s_session?.RunId, status = "reload-failed", error = error.ToString() }));
            QuestCombatLogArchive.EndRun();
            s_session = null;
            m_player.VrRig.Menu.ShowMissionResult("COMBAT TEST: RESTART FAILED");
            GD.PushError("QUEST_COMBAT_RELOAD_FAILED: " + error);
        }).CallDeferred();
    }
    public override void _ExitTree()
    {
        if (!m_reloadRequested && s_session != null)
        {
            PrintJson("STATUS", JsonSerializer.Serialize(new {runId=s_session.RunId,status="scene-exited"}));
            QuestCombatLogArchive.EndRun();
            s_session=null;
        }
        QuestCombatDiagnostics.EnemyHalfRate = false;
        QuestCombatDiagnostics.LegacyPlayerGrounding = false;
        QuestCombatTelemetry.Active=false;
        QuestCombatTelemetry.WeaponLightsDisabled=false;
        QuestCombatTelemetry.SmokeDisabled=false;
        QuestCombatTelemetry.ReducedMissileSmoke=false;
        QuestCombatTelemetry.SmallProjectileLights=false;
    }
}
