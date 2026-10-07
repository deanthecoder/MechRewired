// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

#if DEBUG
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;

namespace MechRewired;

/// <summary>Opt-in desktop GPU ablations. Single-view Mobile rendering, not a Quest FPS predictor.</summary>
public partial class LaptopRenderProbe : Node
{
    private StreamWriter m_log;
    private Viewport m_viewport;
    private SubViewport m_hudViewport;
    private sealed record Frame(double WallMs, double MainGpuMs, double HudGpuMs, double RenderCpuMs, double Draws, double Primitives);

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            if (!QuestVrRuntime.Preview) throw new InvalidOperationException("Run with -- --vr-preview.");
            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
            GetWindow().Size = new Vector2I(1680, 1760);
            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate();
            // The diagnostic root runs while paused; the mission must not inherit Always.
            main.ProcessMode = ProcessModeEnum.Pausable;
            AddChild(main);
            for (var i = 0; i < 60; i++) await NextFrame();
            var sky = (MissionSkyController)typeof(Main).GetField("m_debugSky", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
            if (sky == null) throw new InvalidOperationException("Mission did not start: original game data required.");
            await sky.WaitForSkyBakeAsync();
            var nodes = Descendants(main).ToArray();
            var player = nodes.OfType<PlayerMech>().Single();
            var hud = nodes.OfType<PlayerHud>().Single();
            m_hudViewport = nodes.OfType<SubViewport>().Single(v => v.Name == "VrInstrumentViewport");
            var enemy = nodes.OfType<EnemyMech>().Where(e => !e.IsDestroyed).OrderBy(e => e.Name.ToString(), StringComparer.Ordinal).FirstOrDefault();
            var benchmark = nodes.OfType<QuestPerformanceBenchmark>().Single();
            if (enemy != null)
                player.GlobalTransform = (Transform3D)typeof(QuestPerformanceBenchmark).GetMethod("Face", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(benchmark, [enemy.TargetPosition, 65f]);
            player.VrRig.Menu.Close();
            player.VrRig.BenchmarkActive = true;
            GetTree().Paused = true;
            // Keep the instrument texture populated, then freeze particle simulation identically for every stage.
            hud.ProcessMode = ProcessModeEnum.Always;
            await Wait(2);
            nodes = Descendants(main).ToArray();
            foreach (var particle in nodes.OfType<GpuParticles3D>()) particle.SpeedScale = 0;
            m_viewport = GetViewport();
            m_viewport.Msaa3D = Viewport.Msaa.Msaa2X;
            m_viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
            m_viewport.UseTaa = false;
            m_viewport.Scaling3DScale = 1;
            RenderingServer.ViewportSetMeasureRenderTime(m_viewport.GetViewportRid(), true);
            RenderingServer.ViewportSetMeasureRenderTime(m_hudViewport.GetViewportRid(), true);
            var path = ProjectSettings.GlobalizePath("user://benchmarks/laptop-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            m_log = new StreamWriter(path) { AutoFlush = true };
            Emit("RUN", new { path, fixture = enemy == null ? "deployment-frozen" : "enemy-front-frozen", target = enemy?.Name.ToString(), position = player.GlobalPosition.ToString(), gpu = RenderingServer.GetVideoAdapterName(), renderer = RenderingServer.GetCurrentRenderingMethod(), viewport = m_viewport.GetVisibleRect().Size.ToString(), msaa = m_viewport.Msaa3D.ToString(), screenSpaceAA = m_viewport.ScreenSpaceAA.ToString(), taa = m_viewport.UseTaa, scale = m_viewport.Scaling3DScale, preview = QuestVrRuntime.Preview, stereo = false, warmupSeconds = 1, sampleSeconds = 2, note = "Debug build, frozen gameplay/particles, uncapped desktop Mobile renderer. GPU times are separate viewport scopes; not Quest timings or live combat." });
            Emit("INVENTORY", new
            {
                meshes = nodes.OfType<MeshInstance3D>().Where(mesh => mesh.IsVisibleInTree() && mesh.Mesh != null)
                    .Select(mesh => new { name = mesh.GetPath().ToString(), triangles = mesh.Mesh.GetFaces().Length / 3, surfaces = mesh.Mesh.GetSurfaceCount(), bounds = mesh.GetAabb().ToString(), materials = Materials(mesh).Select(material => material is BaseMaterial3D standard ? standard.GetClass() + "/" + standard.Transparency : material?.GetClass()).ToArray() })
                    .OrderByDescending(mesh => mesh.triangles).Take(12).ToArray(),
                lights = nodes.OfType<Light3D>().Where(light => light.IsVisibleInTree()).Select(light => new { name = light.GetPath().ToString(), type = light.GetClass(), shadows = light.ShadowEnabled, range = light is OmniLight3D omni ? omni.OmniRange : light is SpotLight3D spot ? spot.SpotRange : 0 }).ToArray()
            });

            var terrainMaterials = nodes.OfType<MeshInstance3D>().SelectMany(Materials).OfType<ShaderMaterial>().Where(TerrainSurfaceMaterial.SupportsTriplanarToggle).Distinct().ToArray();
            foreach (var material in terrainMaterials) TerrainSurfaceMaterial.SetTriplanarEnabled(material, false);
            var terrain = nodes.OfType<MeshInstance3D>().Where(mesh => Materials(mesh).OfType<ShaderMaterial>().Any(TerrainSurfaceMaterial.SupportsTriplanarToggle)).ToArray();
            var effects = nodes.OfType<BattlefieldEffects>().ToArray();
            var lights = nodes.OfType<Light3D>().Where(light => light is not DirectionalLight3D).ToArray();
            var environment = nodes.OfType<WorldEnvironment>().First().Environment;
            var terrainOnly = System.Environment.GetCommandLineArgs().Contains("--terrain-only", StringComparer.Ordinal);
            var variants = terrainOnly
                ? new[] { "terrain-chunked", "terrain-unshaded", "terrain-vertex-lit" }
                : new[] { "resolution90", "terrain-hidden", "cockpit-hidden", "hud-hidden", "effects-hidden", "plain-sky", "terrain-unshaded", "local-lights-off" };
            for (var round = 0; round < 2; round++)
            {
                var ordered = round == 0 ? variants : variants.Reverse().ToArray();
                await Measure(round, "baseline");
                foreach (var variant in ordered)
                {
                    var restore = new List<Action>();
                    try
                    {
                        switch (variant)
                        {
                            case "resolution90":
                                m_viewport.Scaling3DScale = .9f;
                                restore.Add(() => m_viewport.Scaling3DScale = 1);
                                break;
                            case "terrain-hidden": Hide(terrain, restore); break;
                            case "terrain-unshaded":
                                var albedoCode = (string)typeof(QuestBenchmarkGraphics).GetField("AlbedoOnlyShaderCode", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue();
                                foreach (var material in terrainMaterials)
                                {
                                    var original = material.Shader;
                                    material.Shader = new Shader { Code = original.Code.Contains("cull_disabled", StringComparison.Ordinal) ? albedoCode.Replace("cull_back", "cull_disabled") : albedoCode };
                                    restore.Add(() => material.Shader = original);
                                }
                                break;
                            case "terrain-chunked":
                            {
                                using var graphics = new TerrainBenchmarkGraphics(main, chunked: true, vertexLit: false);
                                Emit("TERRAIN", new { round, variant, graphics.ChunkCount, graphics.SourceTriangles, graphics.ChunkTriangles });
                                await Measure(round, variant);
                                break;
                            }
                            case "terrain-vertex-lit":
                            {
                                using var graphics = new TerrainBenchmarkGraphics(main, chunked: false, vertexLit: true);
                                Emit("TERRAIN", new { round, variant, graphics.ChunkCount, graphics.SourceTriangles, graphics.ChunkTriangles });
                                await Measure(round, variant);
                                break;
                            }
                            case "cockpit-hidden": Hide([player.Cockpit], restore); break;
                            case "effects-hidden": Hide(effects, restore); break;
                            case "local-lights-off": Hide(lights, restore); break;
                            case "hud-hidden":
                                Hide([hud.VrSurface], restore);
                                var update = m_hudViewport.RenderTargetUpdateMode;
                                m_hudViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                                restore.Add(() => m_hudViewport.RenderTargetUpdateMode = update);
                                break;
                            case "plain-sky":
                                var mode = environment.BackgroundMode;
                                var ambient = environment.AmbientLightSource;
                                var reflected = environment.ReflectedLightSource;
                                // Retain the Sky resource for ambient/reflected lighting while removing visible sky shading.
                                environment.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
                                environment.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;
                                environment.BackgroundMode = Godot.Environment.BGMode.Color;
                                restore.Add(() => { environment.BackgroundMode = mode; environment.AmbientLightSource = ambient; environment.ReflectedLightSource = reflected; });
                                break;
                        }
                        if (variant is not ("terrain-chunked" or "terrain-vertex-lit"))
                            await Measure(round, variant);
                    }
                    finally { foreach (var action in restore) action(); }
                    await Measure(round, "baseline");
                }
            }
            Emit("DONE", new { path });
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
        finally { m_log?.Dispose(); }
    }

    private async System.Threading.Tasks.Task Measure(int round, string variant)
    {
        Emit("STAGE", new { round, variant, phase = "warmup" });
        await Wait(1);
        Emit("STAGE", new { round, variant, phase = "sample" });
        var samples = new List<Frame>();
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed.TotalMilliseconds;
        while (clock.Elapsed.TotalSeconds < 2)
        {
            await NextFrame();
            var now = clock.Elapsed.TotalMilliseconds;
            samples.Add(new Frame(now - previous,
                RenderingServer.ViewportGetMeasuredRenderTimeGpu(m_viewport.GetViewportRid()),
                variant == "hud-hidden" ? 0 : RenderingServer.ViewportGetMeasuredRenderTimeGpu(m_hudViewport.GetViewportRid()),
                RenderingServer.ViewportGetMeasuredRenderTimeCpu(m_viewport.GetViewportRid()),
                Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
                Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)));
            previous = now;
        }
        Emit("SUMMARY", new { round, variant, frames = samples.Count, wallMs = samples.Average(f => f.WallMs), mainGpuMs = samples.Average(f => f.MainGpuMs), hudGpuMs = samples.Average(f => f.HudGpuMs), renderCpuMs = samples.Average(f => f.RenderCpuMs), draws = samples.Average(f => f.Draws), primitives = samples.Average(f => f.Primitives) });
        // Serialize outside the sample interval so allocation and stdout do not contaminate timings.
        foreach (var frame in samples) m_log.WriteLine("LAPTOP_GPU_SAMPLE: " + JsonSerializer.Serialize(new { round, variant, frame }));
        Emit("STAGE", new { round, variant, phase = "end" });
    }

    private void Emit(string kind, object value)
    {
        var line = "LAPTOP_GPU_" + kind + ": " + JsonSerializer.Serialize(value);
        GD.Print(line);
        m_log?.WriteLine(line);
    }

    private async System.Threading.Tasks.Task Wait(double seconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds) await NextFrame();
    }
    private async System.Threading.Tasks.Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private static void Hide(IEnumerable<Node3D> nodes, List<Action> restore)
    {
        foreach (var node in nodes)
        {
            var visible = node.Visible;
            node.Visible = false;
            restore.Add(() => node.Visible = visible);
        }
    }

    private static IEnumerable<Godot.Material> Materials(MeshInstance3D mesh)
    {
        if (mesh.MaterialOverride != null) yield return mesh.MaterialOverride;
        if (mesh.Mesh == null) yield break;
        for (var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++) yield return mesh.GetSurfaceOverrideMaterial(i) ?? mesh.Mesh.SurfaceGetMaterial(i);
    }
    private static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren()) foreach (var node in Descendants(child)) yield return node;
    }
}
#endif
