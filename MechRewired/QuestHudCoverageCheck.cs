// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to use, modify, or distribute this software, without warranty.
#if DEBUG
using System.Reflection;
using Godot;

namespace MechRewired;

/// <summary>Checks coverage against actual rendered HUD pixels; needs a renderer and original data.</summary>
public partial class QuestHudCoverageCheck : Node
{
    public override void _Ready() => Callable.From(Run).CallDeferred();

    private async void Run()
    {
        try
        {
            AddChild(GD.Load<PackedScene>("res://Main.tscn").Instantiate());
            for (var i = 0; i < 60; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var hud = FindChildren("*", "Control", true, false).OfType<PlayerHud>().Single();
            if (hud.VrCoverage == null || hud.VrSurface.Mesh is not QuadMesh || hud.VrSurface.Layers != 0)
                throw new InvalidOperationException("Sparse HUD/gaze anchor not configured; use --vr-preview.");
            // Avoid startup fade masking missing coverage.
            foreach (var name in new[] { "m_hudPower", "m_radarPower" })
                typeof(PlayerHud).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(hud, 1f);
            foreach (var mode in new[] { "normal", "glow", "fullscreen-radar", "hidden-radar" })
            {
                if (mode == "glow") { hud.HudGlow = 1; hud.HudGlowRadius = 32; }
                if (mode is "fullscreen-radar" or "hidden-radar")
                    hud._UnhandledInput(new InputEventKey { Keycode = Key.F2, Pressed = true });
                hud.QueueRedraw();
                for (var i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = hud.GetViewport().GetTexture().GetImage();
                image.Convert(Image.Format.Rgba8);
                var pixels = image.GetData();
                var lit = 0;
                for (var y = 0; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    if (pixels[(y * image.GetWidth() + x) * 4 + 3] == 0) continue;
                    lit++;
                    if (!hud.VrCoverage.CoversPixel(x, y))
                        throw new InvalidOperationException($"{mode}: visible pixel outside coverage at {x},{y}");
                }
                if (lit == 0) throw new InvalidOperationException("HUD texture was empty.");
                GD.Print($"HUD_COVERAGE: {mode} cells={hud.VrCoverage.VisibleCells}/576 visiblePixels={lit}");
                if (mode == "normal" && hud.VrCoverage.VisibleCells >= 576)
                    throw new InvalidOperationException("Normal HUD still covers the entire plane.");
                if (mode == "normal") await CompareSurfaces(hud);
            }
            // Clearing one layer must retain the other, then removing both must clear stale cells.
            hud.VrCoverage.Begin(false); hud.VrCoverage.End();
            hud.VrCoverage.Begin(true); hud.VrCoverage.Include(new Rect2(80, 80, 10, 10)); hud.VrCoverage.End();
            if (hud.VrCoverage.VisibleCells != 1) throw new InvalidOperationException("Stale coverage retained.");
            hud.VrCoverage.Begin(true); hud.VrCoverage.End();
            if (hud.VrCoverage.VisibleCells != 0) throw new InvalidOperationException("Empty coverage retained.");
            GD.Print("HUD_COVERAGE: PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task CompareSurfaces(PlayerHud hud)
    {
        SubViewport MakeViewport(bool sparse)
        {
            var viewport = new SubViewport { Size = new Vector2I(1280, 720),
                World3D = new World3D(), TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(viewport);
            viewport.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal,
                Size = 1.125f, Position = new Vector3(0, 0, 1), Current = true,
                CullMask = PlayerCockpit.RenderLayer });
            var surface = sparse ? (MeshInstance3D)hud.VrSurface.Duplicate() : new MeshInstance3D
            {
                Mesh = hud.VrSurface.Mesh, MaterialOverride = hud.VrSurface.MaterialOverride,
                Layers = PlayerCockpit.RenderLayer
            };
            surface.Transform = Transform3D.Identity;
            viewport.AddChild(surface);
            return viewport;
        }
        var reference = MakeViewport(false);
        var sparse = MakeViewport(true);
        for (var i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var expected = reference.GetTexture().GetImage();
        using var actual = sparse.GetTexture().GetImage();
        expected.Convert(Image.Format.Rgba8); actual.Convert(Image.Format.Rgba8);
        var a = expected.GetData(); var b = actual.GetData();
        var differences = 0;
        for (var i = 0; i < a.Length; i++) if (Math.Abs(a[i] - b[i]) > 2) differences++;
        GD.Print($"HUD_COVERAGE: reference comparison differingChannels={differences}/{a.Length}");
        if (differences > a.Length / 1000) throw new InvalidOperationException("Sparse HUD differs from original surface.");
        reference.QueueFree(); sparse.QueueFree();
    }
}
#endif
