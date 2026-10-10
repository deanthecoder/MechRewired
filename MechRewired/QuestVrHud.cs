// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>Reuses the existing HUD and original chassis silhouette on a stereo-visible cockpit surface.</summary>
public static class QuestVrHud
{
    public static void Attach(PlayerMech player, PlayerHud hud)
    {
        var viewport = new SubViewport
        {
            Name = "VrInstrumentViewport", Size = new Vector2I(1280, 720),
            TransparentBg = true, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always
        };
        player.AddChild(viewport);
        viewport.AddChild(hud);
        hud.Size = new Vector2(1280, 720);
        var surface = new MeshInstance3D
        {
            Name = "VrHudGlass", Position = new Vector3(0, 0, -1.4f),
            Mesh = new QuadMesh { Size = new Vector2(2.0f, 1.125f) },
            Layers = PlayerCockpit.RenderLayer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoTexture = viewport.GetTexture(),
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            }
        };
        player.CockpitMount.AddChild(surface);
        hud.VrCoverage = new QuestHudCoverage(surface);
        hud.VrSurface = surface;
        player.VrRig.AimSurface = surface;
        hud.EnableVrRenderCaching();
    }
}
