// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

#if DEBUG
using Godot;
using System.Diagnostics;
using System.Text.Json;

namespace MechRewired;

/// <summary>
/// Recreates the README gallery using the game's renderer and original mission scenes.
/// </summary>
/// <remarks>
/// Separate capture processes keep camera staging and paused simulation out of the pilot's session.
/// The missile fixture uses the same projectile and particle implementation as combat.
/// </remarks>
public partial class DebugFeatureGallery : Node
{
    private static readonly string[] ImageNames =
    [
        "cockpit", "desert-terrain", "external-mech", "lens-flare",
        "chemical-plant", "missile-trails", "jade-falcon"
    ];
    private bool m_running;
    private Process m_renderer;

    public DebugFeatureGallery()
    {
        Name = "DebugFeatureGallery";
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Refreshes both campaigns' gallery images in an editable source checkout.</summary>
    public async void Refresh()
    {
        if (m_running)
        {
            GD.Print("MechRewired: gallery capture is already running.");
            return;
        }
        var project = ProjectSettings.GlobalizePath("res://");
        var output = Path.GetFullPath(Path.Combine(project, "..", "img", "gallery"));
        if (!File.Exists(Path.Combine(project, "MechRewired.csproj")))
        {
            GD.PushWarning("MechRewired: visual.gallery requires a source checkout running in Godot.");
            return;
        }
        m_running = true;
        var staging = Path.Combine(OS.GetUserDataDir(), "gallery-staging", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            GD.Print("MechRewired: capturing seven README images; two temporary game windows will open.");
            foreach (var campaign in new[] { "wolf", "jade" })
            {
                GD.Print($"MechRewired: capturing {campaign} gallery fixtures...");
                var start = new ProcessStartInfo(OS.GetExecutablePath()) { UseShellExecute = false };
                foreach (var argument in new[] { "--path", project, "--resolution", "1920x1080",
                             "--fixed-fps", "60", "--", "--campaign", campaign, "--gallery-worker", "--gallery-output", staging })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start)
                    ?? throw new IOException("Could not start the gallery renderer.");
                m_renderer = process;
                var deadline = DateTime.UtcNow.AddMinutes(10);
                while (!process.HasExited)
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        process.Kill(true);
                        throw new TimeoutException($"The {campaign} gallery renderer timed out.");
                    }
                    await ToSignal(GetTree().CreateTimer(0.5, processAlways: true), SceneTreeTimer.SignalName.Timeout);
                }
                m_renderer = null;
                if (process.ExitCode != 0)
                    throw new IOException($"The {campaign} gallery renderer exited with {process.ExitCode}.");
            }
            // Publish only after every expected image and manifest has been produced successfully.
            foreach (var name in ImageNames)
                foreach (var extension in new[] { ".png", ".json" })
                    if (!File.Exists(Path.Combine(staging, name + extension)))
                        throw new IOException($"Missing gallery output: {name}{extension}");
            Directory.CreateDirectory(output);
            foreach (var name in ImageNames)
                foreach (var extension in new[] { ".png", ".json" })
                    File.Copy(Path.Combine(staging, name + extension), Path.Combine(output, name + extension), true);
            GD.Print($"MechRewired: README gallery refreshed in {output}.");
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired: gallery capture failed: {exception.Message}");
        }
        finally
        {
            m_renderer = null;
            m_running = false;
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }

    public override void _ExitTree()
    {
        if (m_renderer is { HasExited: false })
            m_renderer.Kill(true);
    }

    /// <summary>Captures a fresh mission in a disposable renderer process, then exits.</summary>
    public async void CaptureMission(PlayerMech player, PlayerHud hud, MissionSkyController sky,
        IReadOnlyList<BattlefieldActor> actors, IReadOnlyList<EnemyMech> enemies, string mission)
    {
        try
        {
            GetWindow().ContentScaleSize = new Vector2I(1920, 1080);
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            foreach (var button in Descendants(GetTree().Root).OfType<MenuButton>())
                button.Hide();
            // Let the deployment DropShip depart before framing the battlefield. The fixed
            // timestep makes this independent of the machine's actual rendering speed.
            await Frames(1200);
            GetTree().Paused = true;
            foreach (var effect in Descendants(GetTree().Root).OfType<SunLensFlare>())
                effect.ProcessMode = ProcessModeEnum.Always;
            var jade = mission.StartsWith("PINK", StringComparison.OrdinalIgnoreCase);
            if (jade)
            {
                hud.Hide();
                var mountainCamera = new Camera3D
                {
                    Name = "GalleryMountainCamera", Fov = 75, Far = 8000,
                    CullMask = 1u | PlayerMech.ExteriorRenderLayer
                };
                AddChild(mountainCamera);
                mountainCamera.Current = true;
                mountainCamera.LookAtFromPosition(new Vector3(150, 75, -550),
                    new Vector3(-300, 250, 700));
                await Save("jade-falcon", mountainCamera, sky, mission);
            }
            else
            {
                var deploymentRotation = player.Rotation;
                player.Rotation = Vector3.Zero;
                hud.QueueRedraw();
                await Save("cockpit", player.CockpitCamera, sky, mission);
                player.Rotation = deploymentRotation;
                hud.Hide();
                var camera = new Camera3D
                {
                    Name = "GalleryCamera", Fov = 65, Far = 8000,
                    CullMask = 1u | PlayerMech.ExteriorRenderLayer
                };
                AddChild(camera);
                camera.Current = true;
                Frame(camera, new Vector3(3665.61f, 8.52f, -3357.93f),
                    new Vector3(0.4836f, -0.3709f, 0.7928f));
                await Save("desert-terrain", camera, sky, mission);

                var center = player.WorldBounds.GetCenter();
                var size = player.WorldBounds.Size.Length();
                camera.LookAtFromPosition(center + new Vector3(0.8f, 0.25f, 1).Normalized() * size * 1.25f, center);
                await Save("external-mech", camera, sky, mission);

                var sun = Descendants(GetTree().Root).OfType<DirectionalLight3D>()
                    .First(light => light.Name.ToString().Contains("Sun", StringComparison.OrdinalIgnoreCase));
                var flareDirection = sun.GlobalBasis.Z.Normalized().Rotated(Vector3.Up, 0.40f);
                flareDirection = flareDirection.Rotated(flareDirection.Cross(Vector3.Up).Normalized(), -0.18f);
                Frame(camera, new Vector3(3665.61f, 20, -3357.93f), flareDirection);
                await Save("lens-flare", camera, sky, mission);

                var plant = actors.First(actor => actor.Description.Contains("chemical", StringComparison.OrdinalIgnoreCase));
                center = plant.WorldBounds.GetCenter();
                camera.Fov = 55;
                camera.LookAtFromPosition(center + new Vector3(90, 35, 100), center + new Vector3(18, -4, -15));
                await Save("chemical-plant", camera, sky, mission);

                camera.Fov = 65;
                // A repeatable staged salvo, using unmodified in-game missile meshes and smoke.
                player.Rotation = Vector3.Zero;
                var origin = player.TargetPosition + new Vector3(0, 0, -2);
                var direction = new Vector3(0, 0.12f, -1).Normalized();
                camera.LookAtFromPosition(origin + new Vector3(30, 10, 34), origin + direction * 20);
                var missiles = new List<MissileEffect>();
                for (var index = 0; index < 6; index++)
                {
                    var missile = new MissileEffect(index == 0) { ProcessMode = ProcessModeEnum.Always };
                    AddChild(missile);
                    missile.Launch(origin + new Vector3((index % 3 - 1) * 2, index / 3 * 2, index * 4), direction, 900, null, _ => { });
                    missiles.Add(missile);
                }
                await Frames(32);
                foreach (var missile in missiles)
                    missile.SetProcess(false);
                await Save("missile-trails", camera, sky, mission, settleFrames: 1);
            }
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"MechRewired: gallery fixture failed: {exception}");
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task Save(string name, Camera3D camera,
        MissionSkyController sky, string mission, int settleFrames = 60)
    {
        await Frames(settleFrames);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var arguments = OS.GetCmdlineUserArgs();
        var outputArgument = Array.IndexOf(arguments, "--gallery-output");
        var output = outputArgument >= 0 && outputArgument + 1 < arguments.Length
            ? Path.GetFullPath(arguments[outputArgument + 1])
            : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "img", "gallery"));
        Directory.CreateDirectory(output);
        using var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(output, name + ".png");
        var result = image.SavePng(path);
        if (result != Error.Ok)
            throw new IOException($"Could not write {path}: {result}");
        var manifest = new
        {
            mission, fixture = name, width = image.GetWidth(), height = image.GetHeight(),
            position = new[] { camera.GlobalPosition.X, camera.GlobalPosition.Y, camera.GlobalPosition.Z },
            rotation = new[] { camera.GlobalRotationDegrees.X, camera.GlobalRotationDegrees.Y, camera.GlobalRotationDegrees.Z },
            fov = camera.Fov, sky = sky.Describe(),
            stagedSalvo = name == "missile-trails", engine = Engine.GetVersionInfo()["string"].ToString()
        };
        File.WriteAllText(Path.ChangeExtension(path, ".json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"MechRewired: gallery saved {path}");
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (var frame = 0; frame < count; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Frame(Camera3D camera, Vector3 position, Vector3 direction)
        => camera.LookAtFromPosition(position, position + direction, Vector3.Up);

    private static IEnumerable<Node> Descendants(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
#endif
