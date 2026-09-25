// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

#if DEBUG
// Godot 4.7 exposes SetInput only on the base C# binding, even for XRControllerTracker.
#pragma warning disable CS0618
using Godot;

namespace MechRewired;

/// <summary>Exercises the real controller/seat/menu path using deterministic synthetic XR trackers.</summary>
public partial class QuestVrSmokeCheck : Node
{
    private readonly PlayerMech m_player;
    private readonly PlayerHud m_hud;
    public QuestVrSmokeCheck(PlayerMech player, PlayerHud hud)
    {
        m_player = player;
        m_hud = hud;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override async void _Ready()
    {
        var left = Tracker("left_hand", XRServer.TrackerType.Controller);
        var right = Tracker("right_hand", XRServer.TrackerType.Controller);
        var head = new XRPositionalTracker { Name = "head", Type = XRServer.TrackerType.Head };
        XRServer.AddTracker(head);
        try
        {
            SetPose(left, new Vector3(-0.25f, -0.2f, -0.35f));
            SetPose(right, new Vector3(0.25f, -0.2f, -0.35f));
            head.SetPose("default", Transform3D.Identity, Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
            await Frames(5);
            var rig = m_player.VrRig;
            Check(rig.Camera.Current && !m_player.CockpitCamera.Current, "tracked camera owns view");
            Check(m_hud.GetViewport() is SubViewport && m_hud.VrSurface != null, "HUD uses stereo-visible surface");
            Check(rig.Left.GetIsActive() && rig.Right.GetIsActive(), "synthetic controllers tracked");

            left.SetInput("primary", new Vector2(0, 1));
            await Frames(3);
            Check(m_player.Drive.ThrottlePercent > 0, "forward stick sets throttle");
            left.SetInput("primary", Vector2.Zero);
            await Frames(3);
            var throttle = m_player.Drive.ThrottlePercent;
            await Frames(3);
            Check(m_player.Drive.ThrottlePercent == throttle, "released stick retains throttle");

            left.SetInput("primary", new Vector2(0, 1));
            left.SetInput("primary_click", true);
            await Frames(3);
            Check(m_player.Drive.ThrottlePercent == 0, "stop wins over held throttle");
            left.SetInput("primary_click", false);
            await Frames(3);
            Check(m_player.Drive.ThrottlePercent == 0, "stop waits for neutral stick");
            left.SetInput("primary", Vector2.Zero);
            await Frames(3);
            left.SetInput("primary", new Vector2(0, -1));
            await Frames(3);
            Check(m_player.Drive.IsReversing && m_player.Drive.ThrottlePercent > 0, "backward stick selects reverse from stop");
            left.SetInput("primary", Vector2.Zero);
            m_player.StopVrMovement();

            var before = m_player.VrAim;
            left.SetInput("trigger", 1.0f);
            await Frames(3);
            Check(m_player.VrAim.DistanceTo(before) < 0.001f, "head-aim clutch has no initial jump");
            head.SetPose("default", new Transform3D(new Basis(Vector3.Up, 0.25f), Vector3.Zero), Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
            await Frames(3);
            Check(m_player.VrAim.X > before.X + 0.15f, "head-aim clutch follows relative yaw");
            left.SetInput("trigger", 0.0f);
            await Frames(3);
            var latchedAim = m_player.VrAim;
            head.SetPose("default", Transform3D.Identity, Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
            await Frames(3);
            Check(m_player.VrAim.DistanceTo(latchedAim) < 0.001f, "free look leaves torso aimed");
            m_player.SetVrAim(0, 0);

            var fires = 0;
            m_player.FireRequested += () => fires++;
            rig.Menu.Toggle();
            right.SetInput("trigger", 1.0f);
            await Frames(3);
            Check(GetTree().Paused && fires == 0, "menu pauses mission and suppresses firing");
            rig.Menu.Close();
            await Frames(3);
            Check(fires == 0, "held menu trigger cannot leak into firing");
            right.SetInput("trigger", 0.0f);
            await Frames(3);
            right.SetInput("trigger", 1.0f);
            await Frames(3);
            Check(fires > 0, "fresh trigger fires after resume");
            right.SetInput("trigger", 0.0f);

            // Let the original instrument power-up complete before capturing the preview.
            await Frames(160);
            await Capture("quest-vr-cockpit.png");
            rig.Menu.Toggle();
            await Frames(4);
            await Capture("quest-vr-menu.png");
            rig.Menu.Close();
            rig.Menu.ShowMissionResult("MISSION FAILED / SMOKE CHECK");
            Check(GetTree().Paused && rig.Menu.IsOpen && rig.Camera.Current, "mission result retains tracked view and opens restart menu");
            GD.Print("QUEST_VR_SMOKE_PASS");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError("QUEST_VR_SMOKE_FAIL: " + error);
            GetTree().Quit(1);
        }
        finally
        {
            XRServer.RemoveTracker(left);
            XRServer.RemoveTracker(right);
            XRServer.RemoveTracker(head);
        }
    }

    private static XRControllerTracker Tracker(string name, XRServer.TrackerType type)
    {
        var tracker = new XRControllerTracker { Name = name, Type = type };
        XRServer.AddTracker(tracker);
        return tracker;
    }

    private static void SetPose(XRPositionalTracker tracker, Vector3 position)
    {
        var pose = new Transform3D(Basis.Identity, position);
        tracker.SetPose("grip", pose, Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
        tracker.SetPose("aim", pose, Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async System.Threading.Tasks.Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = ProjectSettings.GlobalizePath("res://../artifacts/" + name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var result = GetViewport().GetTexture().GetImage().SavePng(path);
        Check(result == Error.Ok, "capture " + name);
        GD.Print("VR_CAPTURE: " + path);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print("VR_CHECK: " + message);
    }
}
#endif
