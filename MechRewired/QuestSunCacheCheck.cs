// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

#if DEBUG
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;

namespace MechRewired;

/// <summary>Native GPU and image comparison of the cached sun against the previous procedural disc.</summary>
public partial class QuestSunCacheCheck : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            var main = GD.Load<PackedScene>("res://Main.tscn").Instantiate();
            main.ProcessMode = ProcessModeEnum.Pausable;
            AddChild(main);
            for (var i = 0; i < 60; i++) await Draw();
            var controller = (MissionSkyController)typeof(Main).GetField("m_debugSky", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
            if (controller == null) throw new InvalidOperationException("Mission sky missing.");
            controller.BakedSkyEnabled = true;
            await controller.WaitForSkyBakeAsync();
            GetTree().Paused = true;
            var world = (WorldEnvironment)main.FindChildren("*", "WorldEnvironment", true, false).First();
            var material = (ShaderMaterial)world.Environment.Sky.SkyMaterial;
            var cached = material.Shader;
            var reference = material.GetMeta("quest_reference_shader").As<Shader>();
            if (reference == null) throw new InvalidOperationException("Reference shader missing.");
            var viewport = new SubViewport
            {
                Size = new Vector2I(1680, 1760), OwnWorld3D = true,
                Msaa3D = Viewport.Msaa.Msaa2X,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                ProcessMode = ProcessModeEnum.Always
            };
            AddChild(viewport);
            using var environment = (Godot.Environment)world.Environment.Duplicate();
            viewport.AddChild(new WorldEnvironment { Environment = environment });
            Vector3 sun = Vector3.Up;
            foreach (var source in main.FindChildren("*", "DirectionalLight3D", true, false).OfType<DirectionalLight3D>())
            {
                if (!source.IsVisibleInTree()) continue;
                viewport.AddChild(new DirectionalLight3D
                {
                    Name = source.Name, Rotation = source.GlobalBasis.GetEuler(),
                    LightColor = source.LightColor, LightEnergy = source.LightEnergy,
                    SkyMode = source.SkyMode, LightAngularDistance = source.LightAngularDistance
                });
                if (source.Name == "SunLight") sun = source.GlobalBasis.Z;
            }
            var camera = new Camera3D { Current = true };
            viewport.AddChild(camera);
            var horizon = material.GetShaderParameter("horizon_offset").AsSingle();
            camera.LookAt((sun + Vector3.Up * horizon).Normalized(), Vector3.Up);
            RenderingServer.ViewportSetMeasureRenderTime(viewport.GetViewportRid(), true);
            var folder = ProjectSettings.GlobalizePath("user://benchmarks/sun-cache");
            Directory.CreateDirectory(folder);
            foreach (var fov in new[] { 75f, 12f })
            {
                camera.Fov = fov;
                material.Shader = reference;
                var before = await Measure(viewport);
                using var originalImage = viewport.GetTexture().GetImage();
                originalImage.SavePng(Path.Combine(folder, $"sun-{fov}-reference.png"));
                material.Shader = cached;
                var after = await Measure(viewport);
                using var cachedImage = viewport.GetTexture().GetImage();
                cachedImage.SavePng(Path.Combine(folder, $"sun-{fov}-cached.png"));
                material.Shader = reference;
                var repeat = await Measure(viewport);
                double absolute = 0, maximum = 0;
                long changed = 0;
                for (var y = 0; y < originalImage.GetHeight(); y++)
                    for (var x = 0; x < originalImage.GetWidth(); x++)
                    {
                        var a = originalImage.GetPixel(x, y);
                        var b = cachedImage.GetPixel(x, y);
                        var difference = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                        absolute += difference;
                        maximum = Math.Max(maximum, difference);
                        if (difference > .03) changed++;
                    }
                GD.Print("SUN_CACHE_CHECK: " + JsonSerializer.Serialize(new
                {
                    fov, beforeGpuMs = before, cachedGpuMs = after, repeatGpuMs = repeat,
                    savedPercent = 100 * (1 - after / ((before + repeat) * .5)),
                    meanMaxChannelError = absolute / (originalImage.GetWidth() * originalImage.GetHeight()),
                    maximumChannelError = maximum, pixelsOver003 = changed, folder
                }));
                if (maximum > .03 || before <= 0 || after <= 0 || repeat <= 0)
                    throw new InvalidOperationException("Sun image comparison or GPU timestamp validation failed.");
            }
            material.Shader = cached;
            GD.Print("SUN_CACHE_CHECK_COMPLETE");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task<double> Measure(SubViewport viewport)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 1) await Draw();
        var times = new List<double>();
        timer.Restart();
        while (timer.Elapsed.TotalSeconds < 2)
        {
            await Draw();
            times.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid()));
        }
        return times.Average();
    }

    private async Task Draw() => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
}
#endif
