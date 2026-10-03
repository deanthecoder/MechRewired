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
            Check(!GetTree().Paused && !rig.Menu.IsOpen, "VR startup leaves mission unpaused with menu closed");
            Check(rig.Camera.Current && !m_player.CockpitCamera.Current, "tracked camera owns view");
            Check(rig.Position.DistanceTo(new Vector3(0, 0.12f, 0.10f)) < 0.001f,
                "seat is raised 12 cm and moved back 10 cm");
            Check(m_hud.GetViewport() is SubViewport && m_hud.VrSurface != null, "HUD uses stereo-visible surface");
            Check(m_hud.HudGlow == 0.0f && rig.Menu.FindChildren("*", "Label3D", true, false)
                .OfType<Label3D>().Any(label => label.Text == "HUD GLOW"),
                "Quest HUD glow defaults off and has a menu switch");
            Check(rig.Left.GetIsActive() && rig.Right.GetIsActive(), "synthetic controllers tracked");
            await CheckNativeCameraRecenter(rig);

            left.SetInput("primary", new Vector2(1, 0));
            await Frames(3);
            Check(Math.Abs(rig.Steering) < 0.001f, "left stick horizontal does not steer");
            left.SetInput("primary", Vector2.Zero);
            right.SetInput("primary", new Vector2(1, 0));
            await Frames(3);
            Check(rig.Steering < -0.5f && Math.Abs(m_player.VrAim.X) < 0.001f,
                "right stick horizontal steers without torso yaw");
            right.SetInput("primary", new Vector2(0, 1));
            var pitchBeforeStick = m_player.VrAim.Y;
            await Frames(5);
            Check(m_player.VrAim.Y > pitchBeforeStick + 0.0001f, "right stick vertical raises torso aim");
            right.SetInput("primary", Vector2.Zero);

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

            var targets = 0;
            m_player.NextTargetRequested += () => targets++;
            var before = m_player.VrAim;
            left.SetInput("trigger", 1.0f);
            await Frames(3);
            Check(targets == 1, "left index trigger selects next target once");
            head.SetPose("default", new Transform3D(new Basis(Vector3.Right, 0.03f), Vector3.Zero), Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
            await Frames(3);
            Check(targets == 1, "held target trigger does not repeat while looking around");
            left.SetInput("trigger", 0.0f);
            await Frames(3);
            head.SetPose("default", Transform3D.Identity, Vector3.Zero, Vector3.Zero, XRPose.TrackingConfidenceEnum.High);
            rig.Camera.Basis = Basis.Identity;
            await Frames(3);
            Check(m_player.VrAim.DistanceTo(before) < 0.001f, "free look leaves torso aimed");

            await CheckGazeControls(rig, right);

            var weaponCycles = 0;
            m_player.CycleWeaponRequested += () => weaponCycles++;
            right.SetInput("grip", 1.0f);
            await Frames(6);
            Check(weaponCycles == 1, "right grip cycles weapon once while held");
            right.SetInput("grip", 0.0f);
            left.SetInput("grip", 1.0f);
            await Frames(5);
            Check(rig.JumpJetsRequested && m_player.IsJumpJetThrusting, "left grip holds jump jets");
            left.SetInput("grip", 0.0f);
            await Frames(5);
            Check(!rig.JumpJetsRequested && !m_player.IsJumpJetThrusting, "releasing left grip stops jump jets");

            var fires = 0;
            m_player.FireRequested += () => fires++;
            rig.Menu.Toggle();
            right.SetInput("trigger", 1.0f);
            left.SetInput("grip", 1.0f);
            await Frames(3);
            Check(GetTree().Paused && fires == 0 && !rig.JumpJetsRequested, "menu suppresses firing and jump jets");
            rig.Menu.Close();
            await Frames(3);
            Check(fires == 0 && !rig.JumpJetsRequested, "held menu inputs cannot fire or thrust after resume");
            right.SetInput("trigger", 0.0f);
            left.SetInput("grip", 0.0f);
            await Frames(3);
            right.SetInput("trigger", 1.0f);
            left.SetInput("grip", 1.0f);
            await Frames(3);
            Check(fires > 0 && rig.JumpJetsRequested, "fresh trigger and grip work after resume");
            right.SetInput("trigger", 0.0f);
            left.SetInput("grip", 0.0f);

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

    private async System.Threading.Tasks.Task CheckNativeCameraRecenter(QuestVrRig rig)
    {
        var savedCameraTransform = rig.Camera.Transform;
        var savedRigTransform = rig.Transform;
        var nativePosition = new Vector3(0.35f, 1.6f, -0.4f);
        try
        {
            rig.Camera.Transform = new Transform3D(Basis.Identity, nativePosition);
            rig.RecenterSeat();
            var seatOffset = new Vector3(0.0f, 0.12f, 0.10f);
            Check((rig.Transform * rig.Camera.Position).DistanceTo(seatOffset) < 0.001f,
                "native head offset recenters the eye at the cockpit seat");

            var edgeRotation = new Basis(Vector3.Up, -0.32f) * new Basis(Vector3.Right, 0.16f);
            rig.Camera.Basis = edgeRotation;
            var injectedCameraPose = new Transform3D(edgeRotation, nativePosition);
            await Frames(12);
            Check(rig.Camera.Transform.IsEqualApprox(injectedCameraPose),
                "edge gaze leaves the native camera pose unchanged");
            Check(rig.HasHeadAim, "native offset edge gaze retains a visible windshield aim point");
        }
        finally
        {
            rig.Camera.Transform = savedCameraTransform;
            rig.Transform = savedRigTransform;
            await Frames(3);
        }
    }

    private async System.Threading.Tasks.Task CheckGazeControls(QuestVrRig rig, XRControllerTracker right)
    {
        var savedPlayerTransform = m_player.GlobalTransform;
        var savedRigTransform = rig.Transform;
        var savedCameraTransform = rig.Camera.Transform;
        try
        {
            right.SetInput("primary", Vector2.Zero);
            right.SetInput("primary_click", false);
            rig.BenchmarkActive = false;
            if (GetTree().Paused) GetTree().Paused = false;
            m_player.StopVrMovement();
            m_player.SetVrTorsoAim(0, 0);
            rig.Camera.Transform = Transform3D.Identity;
            rig.Transform = savedRigTransform;
            var edgeCases = new (string Name, Basis Rotation)[]
            {
                ("center", Basis.Identity),
                ("right", new Basis(Vector3.Up, -0.65f)),
                ("left", new Basis(Vector3.Up, 0.65f)),
                ("upper", new Basis(Vector3.Right, 0.65f)),
                ("lower", new Basis(Vector3.Right, -0.65f))
            };
            foreach (var edge in edgeCases)
            {
                m_player.SetVrTorsoAim(0, 0);
                rig.Transform = savedRigTransform;
                rig.Camera.Basis = edge.Rotation;
                await Frames(12);
                Check(m_player.VrAim.DistanceTo(Vector2.Zero) < 0.001f,
                    $"{edge.Name} gaze does not automatically change torso aim");
            }

            // Capture a rightward raw headset bearing. Alignment must rotate the legs in place,
            // without recentering or otherwise moving the tracked camera pose.
            rig.Transform = savedRigTransform;
            m_player.SetVrTorsoAim(0, 0);
            rig.Camera.Basis = new Basis(Vector3.Up, -0.65f);
            await Frames(3);
            var cameraPose = rig.Camera.Transform;
            var gazeHeading = Mathf.Atan2(rig.Camera.GlobalBasis.Z.X, rig.Camera.GlobalBasis.Z.Z);
            var headingErrorBeforeAlignment = Mathf.Abs(Mathf.AngleDifference(m_player.GlobalRotation.Y, gazeHeading));
            right.SetInput("primary_click", true);
            await Frames(2);
            right.SetInput("primary_click", false);
            await Frames(4);
            Check(m_player.IsVrAligningLegsToGaze, "right primary click starts legs alignment to gaze");
            Check(rig.Camera.Transform.IsEqualApprox(cameraPose), "gaze alignment turns legs without teleporting headset pose");
            Check(Mathf.Abs(Mathf.AngleDifference(m_player.GlobalRotation.Y, gazeHeading)) < headingErrorBeforeAlignment,
                "gaze alignment turns legs toward the captured world bearing");

            right.SetInput("primary", new Vector2(1, 0));
            await Frames(3);
            Check(!m_player.IsVrAligningLegsToGaze && rig.Steering < -0.5f,
                "manual right X steering cancels gaze alignment");
            right.SetInput("primary", Vector2.Zero);

            m_player.StopVrMovement();
            m_player.GlobalRotation = new Vector3(m_player.GlobalRotation.X, Mathf.Pi - 0.12f, m_player.GlobalRotation.Z);
            rig.Transform = savedRigTransform;
            m_player.SetVrTorsoAim(0, 0);
            rig.Camera.Basis = new Basis(Vector3.Up, 0.25f);
            await Frames(3);
            gazeHeading = Mathf.Atan2(rig.Camera.GlobalBasis.Z.X, rig.Camera.GlobalBasis.Z.Z);
            headingErrorBeforeAlignment = Mathf.Abs(Mathf.AngleDifference(m_player.GlobalRotation.Y, gazeHeading));
            right.SetInput("primary_click", true);
            await Frames(2);
            right.SetInput("primary_click", false);
            await Frames(4);
            Check(m_player.IsVrAligningLegsToGaze &&
                  Mathf.Abs(Mathf.AngleDifference(m_player.GlobalRotation.Y, gazeHeading)) < headingErrorBeforeAlignment,
                "gaze alignment takes the short turn across the yaw wrap-around");

            m_player.StopVrMovement();
            m_player.SetVrTorsoAim(0, 0);
            rig.Camera.Transform = Transform3D.Identity;
            rig.Menu.Toggle();
            await Frames(3);
            var pausedAim = m_player.VrAim;
            rig.Transform = savedRigTransform;
            rig.Camera.Basis = new Basis(Vector3.Up, -1.0f);
            await Frames(12);
            Check(GetTree().Paused && m_player.VrAim.DistanceTo(pausedAim) < 0.001f,
                "paused menu keeps torso aim unchanged at a gaze edge");
            rig.Menu.Close();
            await Frames(3);

            rig.BenchmarkActive = true;
            var benchmarkAim = m_player.VrAim;
            rig.Transform = savedRigTransform;
            rig.Camera.Basis = new Basis(Vector3.Up, -1.0f);
            await Frames(12);
            Check(m_player.VrAim.DistanceTo(benchmarkAim) < 0.001f,
                "benchmark keeps torso aim unchanged at a gaze edge");
            rig.BenchmarkActive = false;
        }
        finally
        {
            right.SetInput("primary", Vector2.Zero);
            right.SetInput("primary_click", false);
            rig.BenchmarkActive = false;
            if (GetTree().Paused) rig.Menu.Close();
            m_player.StopVrMovement();
            m_player.SetVrTorsoAim(0, 0);
            rig.Camera.Transform = savedCameraTransform;
            m_player.GlobalTransform = savedPlayerTransform;
            rig.Transform = savedRigTransform;
            await Frames(5);
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
