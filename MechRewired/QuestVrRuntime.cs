// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>Explicit desktop opt-in; Android always launches the seated Wolf VR prototype.</summary>
public static class QuestVrRuntime
{
    public static bool Active { get; private set; }
    public static bool Preview => OS.GetCmdlineUserArgs().Contains("--vr-preview");
    public static bool Requested => OS.HasFeature("android") || Preview || OS.GetCmdlineUserArgs().Contains("--vr");

    public static void Initialize(Viewport viewport)
    {
        Active = false;
        if (!Requested) return;
        GD.Print("MechRewired: VR game data directory: " + ProjectSettings.GlobalizePath("user://game-data"));
        var xr = XRServer.FindInterface("OpenXR");
        if (!Preview && xr != null && (xr.IsInitialized() || xr.Initialize()))
        {
            viewport.UseXR = true;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Active = true;
            GD.Print("MechRewired: OpenXR seated cockpit enabled.");
        }
        else if (Preview)
        {
            Active = true;
            GD.Print("MechRewired: VR desktop preview (no headset tracking).");
        }
        else GD.PushError("MechRewired: OpenXR initialization failed; no headset session is available.");
    }

    public static void ShowStartupMessage(Node parent, string message)
    {
        var origin = new XROrigin3D { Name = "VrStartup", ProcessMode = Node.ProcessModeEnum.Always };
        parent.AddChild(origin);
        var camera = new XRCamera3D { Current = true, Near = 0.05f };
        origin.AddChild(camera);
        camera.AddChild(new Label3D
        {
            Text = message, Position = new Vector3(0, 0, -1.5f), FontSize = 28,
            PixelSize = 0.001f, NoDepthTest = true
        });
    }
}
