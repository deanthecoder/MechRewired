// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Godot;
using MechRewired.Rendering;

namespace MechRewired;

/// <summary>
/// Opt-in rendering ablations on a frozen mission. Wall-clock samples exclude setup and report IO.
/// This is deliberately not a gameplay/AI benchmark, nor a measurement of compositor-presented FPS.
/// </summary>
public sealed partial class QuestPerformanceBenchmark : Node
{
    private const double WarmupSeconds = 3;
    private const double SampleSeconds = 6;
    private readonly Node m_world;
    private readonly PlayerMech m_player;
    private readonly PlayerHud m_hud;
    private readonly MissionSkyController m_missionSky;
    private readonly TerrainSurfaceIndex m_terrain;
    private readonly TerrainRockScatter m_rocks;
    private readonly IReadOnlyList<BattlefieldActor> m_actors;
    private readonly IReadOnlyList<EnemyMech> m_enemies;
    private readonly string m_mission;
    private readonly List<MissileEffect> m_missiles = new();
    private bool m_running;
    private bool m_cancelled;
    private Label3D m_status;
    private string m_output;

    private sealed record Fixture(string Name, Transform3D Pose, string Target, bool Sweep, bool Missiles);
    private sealed record Frame(double Milliseconds, double CpuMs, double GpuMs, double DrawCalls,
        double Primitives, double HeadTranslation, double HeadAngleDegrees);

    public QuestPerformanceBenchmark(Node world, PlayerMech player, PlayerHud hud, MissionSkyController missionSky,
        TerrainSurfaceIndex terrain, TerrainRockScatter rocks,
        IReadOnlyList<BattlefieldActor> actors, IReadOnlyList<EnemyMech> enemies, string mission)
    {
        m_world = world;
        m_player = player;
        m_hud = hud;
        m_missionSky = missionSky;
        m_terrain = terrain;
        m_rocks = rocks;
        m_actors = actors;
        m_enemies = enemies;
        m_mission = mission;
        Name = "QuestPerformanceBenchmark";
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--quest-benchmark") &&
            !OS.GetCmdlineUserArgs().Contains("--quest-combat-benchmark") && !QuestCombatBenchmark.IsSuiteActive)
            Callable.From(Start).CallDeferred();
    }

    public void Cancel() => m_cancelled = true;

    public async void Start()
    {
        if (m_running || m_player.IsDestroyed || QuestCombatBenchmark.IsSuiteActive) return;
        m_running = true;
        m_cancelled = false;
        var originalPose = m_player.GlobalTransform;
        var originalHudProcess = m_hud.ProcessMode;
        var rig = m_player.VrRig;
        var menu = rig.Menu;
        var viewport = GetViewport();
        QuestBenchmarkGraphics graphics = null;
        var completed = false;
        try
        {
            menu.Close();
            GetTree().Paused = true;
            rig.BenchmarkActive = true;
            rig.BenchmarkCancelRequested = Cancel;
            m_hud.ProcessMode = ProcessModeEnum.Always;
            m_status = new Label3D
            {
                Name = "BenchmarkStatus", Position = new Vector3(0, 0.30f, -1.2f),
                FontSize = 22, PixelSize = 0.00065f, NoDepthTest = true,
                Text = "BENCHMARK: keep head still; Menu cancels"
            };
            rig.Camera.AddChild(m_status);
            m_output = ProjectSettings.GlobalizePath("user://benchmarks/" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(m_output);
            await m_missionSky.WaitForSkyBakeAsync();
            CheckCancelled();
            graphics = new QuestBenchmarkGraphics(m_world.GetParent(), m_player, m_hud, m_missionSky.ProceduralSky);
            var fixtures = BuildFixtures(originalPose);
            var xr = XRServer.FindInterface("OpenXR") as OpenXRInterface;
            var refreshRate = xr?.IsInitialized() == true ? xr.DisplayRefreshRate : 0;
            var frameBudget = 1000.0 / (refreshRate > 0 ? refreshRate : 72);
            var runMetadata = JsonSerializer.Serialize(new
            {
                schema = 3, skyCache = "hdr-2048x1024-separate-sun-v1", runId = Path.GetFileName(m_output), startedUtc = DateTime.UtcNow, mission = m_mission,
                engine = Engine.GetVersionInfo()["string"].ToString(),
                build = OS.HasFeature("debug") ? "debug" : "release",
                assembly = typeof(QuestPerformanceBenchmark).Assembly.FullName,
                platform = OS.GetName(), device = OS.GetModelName(),
                gpu = RenderingServer.GetVideoAdapterName(),
                renderer = RenderingServer.GetCurrentRenderingMethod(),
                preview = QuestVrRuntime.Preview, xr = viewport.UseXR,
                refreshRate, frameBudgetMs = frameBudget,
                viewportSize = viewport.GetVisibleRect().Size.ToString(),
                xrRenderTargetSize = xr?.IsInitialized() == true ? xr.GetRenderTargetSize().ToString() : "unavailable",
                msaa = viewport.Msaa3D.ToString(), renderScale = viewport.Scaling3DScale,
                warmupSeconds = WarmupSeconds, sampleSeconds = SampleSeconds,
                engineMaxFps = Engine.MaxFps,
                hudOptions = new { m_hud.ShowRadar, m_hud.ShowWeapons, m_hud.ShowStatus, m_hud.ShowNavigation, m_hud.ShowTargeting, m_hud.HudGlow },
                note = "Frozen gameplay rendering ablations; native head tracking live. Samples are wall-clock app frame intervals, not compositor FPS. GPU timings may be unavailable. Restart mission before each run.",
                fixtures = fixtures.Select(f => new { f.Name, f.Target, position = f.Pose.Origin.ToString(), basis = f.Pose.Basis.ToString(), f.Sweep, f.Missiles }),
                variants = QuestBenchmarkGraphics.VariantNames
            });
            File.WriteAllText(Path.Combine(m_output, "run.json"), runMetadata);
            GD.Print("QUEST_BENCHMARK_RUN: " + runMetadata);
            using var summary = new StreamWriter(Path.Combine(m_output, "summary.csv"));
            using var raw = new StreamWriter(Path.Combine(m_output, "frames.csv"));
            const string header = "trial,fixture,target,variant,frames,mean_ms,median_ms,p95_ms,p99_ms,average_fps,one_percent_low_fps,over_budget_percent,renderer_cpu_ms,renderer_gpu_ms,draw_calls,primitives,max_head_translation_m,max_head_angle_deg";
            summary.WriteLine(header);
            GD.Print("QUEST_BENCHMARK_CSV: " + header);
            raw.WriteLine("trial,frame,wall_ms,renderer_cpu_ms,renderer_gpu_ms,draw_calls,primitives,head_translation_m,head_angle_deg");
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);
            var trial = 0;
            foreach (var fixture in fixtures)
            {
                CheckCancelled();
                // Generate the same rock neighbourhood before timing, without moving or simulating enemies.
                m_player.GlobalTransform = fixture.Pose;
                m_rocks.ConfigureObserver(m_player);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var variants = QuestBenchmarkGraphics.VariantNames.Where(v => v != "baseline").ToArray();
                if (fixtures.IndexOf(fixture) % 2 != 0) Array.Reverse(variants);
                foreach (var variant in new[] { "baseline" }.Concat(variants).Concat(new[] { "baseline" }))
                {
                    CheckCancelled();
                    trial++;
                    try { await graphics.ApplyAsync(variant); CheckCancelled(); }
                    catch (InvalidOperationException error)
                    {
                        GD.PushWarning($"QUEST_BENCHMARK_SKIP {fixture.Name}/{variant}: {error.Message}");
                        File.AppendAllText(Path.Combine(m_output, "skipped.txt"),
                            $"{fixture.Name}/{variant}: {error.Message}{System.Environment.NewLine}");
                        continue;
                    }
                    ResetMissiles();
                    PrepareMissiles(fixture);
                    m_player.GlobalTransform = fixture.Pose;
                    m_status.Text = $"{fixture.Name} / {variant}\nKeep head still — Menu cancels";
                    graphics.ResetAnimation();
                    await Observe(fixture, WarmupSeconds, false);
                    // Reset the fixture clock and effects after pipeline/shader warm-up.
                    ResetMissiles();
                    PrepareMissiles(fixture);
                    m_player.GlobalTransform = fixture.Pose;
                    graphics.ResetAnimation();
                    var headAtStart = rig.Camera.Transform;
                    var hudDrawsAtStart = m_hud.VrInstrumentDrawCount;
                    var frames = await Observe(fixture, SampleSeconds, true);
                    var stats = BenchmarkFrameStatistics.Calculate(frames.Select(f => f.Milliseconds), frameBudget);
                    var row = string.Join(",", new[]
                    {
                        Number(trial), Csv(fixture.Name), Csv(fixture.Target), Csv(variant), Number(stats.Frames),
                        Number(stats.MeanFrameMs), Number(stats.MedianFrameMs), Number(stats.P95FrameMs),
                        Number(stats.P99FrameMs), Number(stats.AverageFps), Number(stats.OnePercentLowFps),
                        Number(stats.OverBudgetPercent), AvailableAverage(frames.Select(f => f.CpuMs)),
                        AvailableAverage(frames.Select(f => f.GpuMs)), Number(frames.Average(f => f.DrawCalls)),
                        Number(frames.Average(f => f.Primitives)), Number(frames.Max(f => f.HeadTranslation)),
                        Number(frames.Max(f => f.HeadAngleDegrees))
                    });
                    summary.WriteLine(row);
                    GD.Print("QUEST_BENCHMARK_CSV: " + row);
                    var trialMetadata = JsonSerializer.Serialize(new
                    {
                        trial, fixture = fixture.Name, variant, headAtStart = headAtStart.ToString(),
                        headAtEnd = rig.Camera.Transform.ToString(), playerAtEnd = m_player.GlobalTransform.ToString(),
                        hudInstrumentDraws = m_hud.VrInstrumentDrawCount - hudDrawsAtStart,
                        finishedUtc = DateTime.UtcNow, stats
                    });
                    File.WriteAllText(Path.Combine(m_output, $"{trial:D3}-trial.json"), trialMetadata);
                    GD.Print("QUEST_BENCHMARK_TRIAL: " + trialMetadata);
                    for (var i = 0; i < frames.Count; i++)
                    {
                        var f = frames[i];
                        raw.WriteLine(string.Join(",", new[] { Number(trial), Number(i), Number(f.Milliseconds),
                            Number(f.CpuMs), Number(f.GpuMs), Number(f.DrawCalls), Number(f.Primitives),
                            Number(f.HeadTranslation), Number(f.HeadAngleDegrees) }));
                    }
                    summary.Flush();
                    raw.Flush();
                    GD.Print($"QUEST_BENCHMARK {trial}: {fixture.Name}/{variant}: {stats.AverageFps:F1} app FPS, p95 {stats.P95FrameMs:F2} ms");
                }
            }
            completed = true;
        }
        catch (OperationCanceledException) { GD.Print("QUEST_BENCHMARK_CANCELLED"); }
        catch (Exception error) { GD.PushError("QUEST_BENCHMARK_FAILED: " + error); }
        finally
        {
            ResetMissiles();
            graphics?.Dispose();
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), false);
            m_player.GlobalTransform = originalPose;
            m_rocks.ConfigureObserver(m_player);
            m_hud.ProcessMode = originalHudProcess;
            m_status?.QueueFree();
            rig.BenchmarkActive = false;
            rig.BenchmarkCancelRequested = null;
            m_running = false;
            if (m_output != null)
            {
                try { File.WriteAllText(Path.Combine(m_output, "status.txt"), completed ? "complete" : m_cancelled ? "cancelled" : "failed"); }
                catch (IOException error) { GD.PushWarning(error.Message); }
            }
            var result = completed ? "complete" : m_cancelled ? "cancelled" : "failed";
            GD.Print("QUEST_BENCHMARK_STATUS: " + result);
            menu.ShowMissionResult(completed ? "BENCHMARK COMPLETE / RESULTS LOGGED" : "BENCHMARK STOPPED / PARTIAL LOGS");
            GD.Print("QUEST_BENCHMARK_OUTPUT: " + m_output);
        }
    }

    private List<Fixture> BuildFixtures(Transform3D deployment)
    {
        var result = new List<Fixture>
        {
            new("terrain-sweep", deployment, "deployment", true, false)
        };
        var enemy = m_enemies.Where(e => !e.IsDestroyed).OrderBy(e => e.Name.ToString(), StringComparer.Ordinal).FirstOrDefault();
        if (enemy != null) result.Add(new Fixture("enemy-front", Face(enemy.TargetPosition, 65), enemy.Name, false, false));
        else GD.PushWarning("Benchmark: no enemy available; enemy-front omitted.");
        var building = m_actors.Where(a => !a.IsDestroyed && a.IsDamageable && a.WorldBounds.Size.Length() > 10)
            .OrderByDescending(a => a.Description.Contains("chemical", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name.ToString(), StringComparer.Ordinal).FirstOrDefault();
        if (building != null)
            result.Add(new Fixture("building-sweep", Face(building.WorldBounds.GetCenter(), Math.Max(65, building.WorldBounds.Size.Length())), building.Name, true, false));
        else GD.PushWarning("Benchmark: no building candidate available; building-sweep omitted.");
        result.Add(new Fixture("missile-salvo", deployment, "scripted non-damaging salvo", false, true));
        return result;
    }

    private Transform3D Face(Vector3 target, float distance)
    {
        var position = target + new Vector3(0, 0, distance);
        if (!m_terrain.TryGetHeight(position, out var height)) height = DerivedTerrainSurfaceBuilder.ImplicitGroundHeight;
        position.Y = height + m_player.GlobalPosition.Y - m_player.FeetElevation;
        return new Transform3D(Basis.LookingAt(new Vector3(target.X - position.X, 0, target.Z - position.Z), Vector3.Up), position);
    }

    private async System.Threading.Tasks.Task<List<Frame>> Observe(Fixture fixture, double seconds, bool collect)
    {
        var frames = new List<Frame>(collect ? 1200 : 0);
        var head = m_player.VrRig.Camera.Transform;
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed.TotalSeconds;
        var salvo = -1;
        // Discard the transition interval: all shader swaps and pool allocations precede this await.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        clock.Restart();
        previous = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            CheckCancelled();
            var time = clock.Elapsed.TotalSeconds;
            var phase = (float)(time / SampleSeconds * Mathf.Tau);
            var pose = fixture.Pose;
            if (fixture.Sweep)
            {
                pose.Basis = new Basis(Vector3.Up, Mathf.Sin(phase) * 0.20f) * fixture.Pose.Basis;
                pose.Origin += fixture.Pose.Basis.X * (Mathf.Sin(phase) * 3.0f);
            }
            m_player.GlobalTransform = pose;
            if (fixture.Missiles && (int)(time / 2) > salvo)
            {
                salvo = (int)(time / 2);
                LaunchSalvo(salvo);
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var now = clock.Elapsed.TotalSeconds;
            if (collect)
            {
                var camera = m_player.VrRig.Camera.Transform;
                frames.Add(new Frame((now - previous) * 1000,
                    RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()),
                    RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
                    Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                    Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
                    camera.Origin.DistanceTo(head.Origin),
                    Mathf.RadToDeg(camera.Basis.GetRotationQuaternion().AngleTo(head.Basis.GetRotationQuaternion()))));
            }
            previous = now;
        }
        return frames;
    }

    private void PrepareMissiles(Fixture fixture)
    {
        if (!fixture.Missiles) return;
        for (var i = 0; i < 18; i++)
        {
            var missile = new MissileEffect(i % 6 == 0) { ProcessMode = ProcessModeEnum.Always };
            AddChild(missile);
            foreach (var particles in missile.FindChildren("*", "GPUParticles3D", true, false).OfType<GpuParticles3D>())
            {
                particles.UseFixedSeed = true;
                particles.Seed = (uint)(100 + i);
            }
            m_missiles.Add(missile);
        }
    }

    private void LaunchSalvo(int index)
    {
        if (index >= 3) return;
        var direction = -m_player.GlobalBasis.Z;
        var origin = m_player.TargetPosition + direction * 3;
        for (var i = 0; i < 6; i++)
            m_missiles[index * 6 + i].Launch(origin + m_player.GlobalBasis.X * ((i % 3 - 1) * 1.5f) + Vector3.Up * (i / 3),
                direction, 900, null, _ => { });
    }

    private void ResetMissiles()
    {
        // Immediate release occurs only between trials, never in a measured sample window.
        foreach (var missile in m_missiles) missile.Free();
        m_missiles.Clear();
    }

    private void CheckCancelled()
    {
        if (m_cancelled) throw new OperationCanceledException();
    }

    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string Csv(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    private static string AvailableAverage(IEnumerable<double> values)
    {
        var available = values.Where(v => v > 0 && double.IsFinite(v)).ToArray();
        return available.Length == 0 ? "" : Number(available.Average());
    }
}
