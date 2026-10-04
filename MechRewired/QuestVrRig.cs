// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Rendering;

namespace MechRewired;

/// <summary>Seat-relative tracking and controller input. Tracking keeps running while the mission is paused.</summary>
public partial class QuestVrRig : XROrigin3D
{
    private static readonly Vector3 SeatOffset = new(0.0f, 0.12f, 0.10f);
    // Includes the reticle radius and the frame overlapping the authored glass edges.
    private const float WindshieldAimInset = 0.16f;
    private const float TorsoPitchSpeed = Mathf.Pi / 3.0f;
    private const float HeadAimFollowRate = 24.0f;
    private readonly PlayerMech m_player;
    private readonly OpenXRInterface m_interface;
    private readonly HashSet<string> m_held = new();
    private bool m_seatCentered;
    private bool m_requireRelease = true;
    private bool m_jumpJetsRequireRelease = true;
    private float m_throttleRepeat;
    private int m_throttleDirection;
    private bool m_waitForThrottleCenter = true;
    private Vector2 m_headAimPoint;
    private bool m_hasHeadAim;
    private int m_initialTrackingFrames;
    private bool m_hasFocusedSession;
    private int m_lastInputState = -1;
    private ulong m_nextInputStateLog;

    public XRCamera3D Camera { get; }
    public XRController3D Left { get; }
    public XRController3D Right { get; }
    public XRController3D RightAim { get; }
    public QuestVrMenu Menu { get; set; }
    public bool BenchmarkActive { get; set; }
    public Action BenchmarkCancelRequested { get; set; }
    public float Steering { get; private set; }
    public bool JumpJetsRequested { get; private set; }
    public MeshInstance3D AimSurface { get; set; }
    public bool HasHeadAim => m_hasHeadAim;
    public Vector3 HeadAimDirection => m_hasHeadAim && GodotObject.IsInstanceValid(AimSurface)
        ? Camera.GlobalPosition.DirectionTo(AimSurface.ToGlobal(new Vector3(m_headAimPoint.X, m_headAimPoint.Y, 0)))
        : -m_player.Torso.GlobalBasis.Z.Normalized();

    public QuestVrRig(PlayerMech player)
    {
        m_player = player;
        m_interface = XRServer.FindInterface("OpenXR") as OpenXRInterface;
        Name = "QuestVrRig";
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = -100;
        Camera = new XRCamera3D
        {
            Name = "TrackedHead", Current = true, Near = 0.05f, Far = 8000,
            CullMask = 1u | PlayerCockpit.RenderLayer
        };
        AddChild(Camera);
        Left = CreateController("LeftHand", "left_hand", "grip");
        Right = CreateController("RightHand", "right_hand", "grip");
        RightAim = CreateController("RightAim", "right_hand", "aim");
    }

    private XRController3D CreateController(string name, string tracker, string pose)
    {
        var controller = new XRController3D { Name = name, Tracker = tracker, Pose = pose };
        AddChild(controller);
        return controller;
    }

    public override void _Ready()
    {
        m_player.CockpitCamera.Current = false;
        Camera.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void RecenterSeat()
    {
        // Cancel only the tracked reference-space yaw and translation. Never overwrite the HMD pose.
        var forward = -Camera.Basis.Z;
        var yaw = Mathf.Atan2(-forward.X, -forward.Z);
        Basis = new Basis(Vector3.Up, -yaw);
        Position = SeatOffset - Basis * Camera.Position;
        m_seatCentered = true;
    }

    public override void _Process(double delta)
    {
        var headTracked = (XRServer.GetTracker("head") as XRPositionalTracker)?.GetPose("default")?.HasTrackingData == true;
        var sessionState = m_interface?.GetSessionState();
        var sessionFocused = QuestVrRuntime.Preview || sessionState == OpenXRInterface.SessionState.Focused;
        if (!m_seatCentered)
        {
            // The tracker can become valid before XRCamera3D has applied that frame's pose.
            // Wait for a focused session and two pose updates before anchoring the seat.
            m_initialTrackingFrames = sessionFocused && (QuestVrRuntime.Preview || headTracked)
                ? m_initialTrackingFrames + 1 : 0;
            if (m_initialTrackingFrames >= 2) RecenterSeat();
        }
        if (BenchmarkActive)
        {
            // Keep native head tracking live, but never let controller input unpause or alter a trial.
            if ((!QuestVrRuntime.Preview && (!headTracked || !sessionFocused)) ||
                Pressed("benchmark_cancel", Left.GetIsActive() && Left.IsButtonPressed("menu_button")))
                BenchmarkCancelRequested?.Invoke();
            return;
        }
        if (!QuestVrRuntime.Preview && m_hasFocusedSession && (!headTracked || !sessionFocused) && Menu is { IsOpen: false })
        {
            m_player.StopVrMovement();
            Menu.Toggle();
        }
        var leftTracked = Left.GetIsActive();
        var rightTracked = Right.GetIsActive();
        if (sessionFocused && headTracked && m_seatCentered) m_hasFocusedSession = true;
        var menuPressed = Pressed("menu", leftTracked && Left.IsButtonPressed("menu_button"));
        var stopPressed = Pressed("stop", leftTracked && Left.IsButtonPressed("primary_click"));
        var weaponButtonPressed = Pressed("weapon_button", rightTracked && Right.IsButtonPressed("ax_button"));
        var weaponGripPressed = Pressed("weapon_grip", rightTracked && Right.GetFloat("grip") > 0.65f);
        var targetButtonPressed = Pressed("target_button", rightTracked && Right.IsButtonPressed("by_button"));
        var targetTriggerPressed = Pressed("target_trigger", leftTracked && Left.GetFloat("trigger") > 0.65f);
        var inspectPressed = Pressed("inspect", leftTracked && Left.IsButtonPressed("ax_button"));
        var centerPressed = Pressed("center", leftTracked && Left.IsButtonPressed("by_button"));
        var alignLegsPressed = Pressed("align_legs", rightTracked && Right.IsButtonPressed("primary_click"));
        if (menuPressed) Menu?.Toggle();
        var fireTrigger = rightTracked ? Right.GetFloat("trigger") : 0.0f;
        var firing = fireTrigger > 0.65f;
        var jumpJetsHeld = leftTracked && Left.GetFloat("grip") > 0.65f;
        if (!GetTree().Paused && sessionFocused && m_seatCentered && (QuestVrRuntime.Preview || headTracked))
            UpdateHeadAim((float)delta);
        else
        {
            m_hasHeadAim = false;
        }
        var inputState = (GetTree().Paused ? 1 : 0) | (sessionFocused ? 2 : 0) |
                         (headTracked ? 4 : 0) | (leftTracked ? 8 : 0) | (rightTracked ? 16 : 0) |
                         (m_seatCentered ? 32 : 0) | (Menu is { IsOpen: true } ? 64 : 0) | (m_hasHeadAim ? 128 : 0);
        var now = Time.GetTicksMsec();
        if (inputState != m_lastInputState && now >= m_nextInputStateLog)
        {
            GD.Print($"QUEST_VR_INPUT: paused={GetTree().Paused} focus={sessionState} head={headTracked} " +
                     $"left={leftTracked} right={rightTracked} seat={m_seatCentered} menu={Menu?.IsOpen} aim={m_hasHeadAim}");
            m_lastInputState = inputState;
            m_nextInputStateLog = now + 1000;
        }
        if (GetTree().Paused || !sessionFocused || m_player.IsDestroyed || !m_seatCentered || (!QuestVrRuntime.Preview && !headTracked))
        {
            Steering = 0;
            JumpJetsRequested = false;
            m_requireRelease = true;
            m_jumpJetsRequireRelease = true;
            m_throttleDirection = 0;
            m_waitForThrottleCenter = true;
            // Lost controllers must not leave a latched mech driving away.
            if (!GetTree().Paused && (!leftTracked || !rightTracked)) m_player.StopVrMovement();
            return;
        }
        // Weapon actions depend on their own controller. Losing the other hand must not
        // disable firing, but locomotion still stops until both controllers return.
        var locomotionTracked = leftTracked && rightTracked;
        if (!locomotionTracked)
        {
            m_player.StopVrMovement();
            m_throttleDirection = 0;
            m_waitForThrottleCenter = true;
            m_jumpJetsRequireRelease = true;
        }
        if (!rightTracked) m_requireRelease = true;
        // Hysteresis requires a deliberate partial release on a tracked controller.
        if (rightTracked && fireTrigger < 0.45f) m_requireRelease = false;
        if (locomotionTracked && !jumpJetsHeld) m_jumpJetsRequireRelease = false;
        JumpJetsRequested = locomotionTracked && jumpJetsHeld && !m_jumpJetsRequireRelease;
        if (stopPressed)
        {
            m_player.StopVrMovement();
            m_waitForThrottleCenter = true;
        }
        if (weaponButtonPressed || weaponGripPressed) m_player.VrCycleWeapon();
        if (targetButtonPressed || targetTriggerPressed) m_player.VrCycleTarget();
        if (inspectPressed) m_player.VrInspect();
        if (centerPressed)
        {
            RecenterSeat();
            UpdateHeadAim((float)delta, snap: true);
        }

        var movement = locomotionTracked ? Left.GetVector2("primary") : Vector2.Zero;
        var aimStick = rightTracked ? Right.GetVector2("primary") : Vector2.Zero;
        Steering = locomotionTracked ? -Deadzone(aimStick.X) : 0.0f;
        var pitchInput = Deadzone(aimStick.Y);
        if (locomotionTracked && alignLegsPressed) m_player.AlignVrLegsToGaze(m_player.WeaponAimDirection);
        if (!Mathf.IsZeroApprox(pitchInput))
            m_player.SetVrPitch(m_player.VrAim.Y + pitchInput * TorsoPitchSpeed * (float)delta);
        var direction = movement.Y > 0.55f ? 1 : movement.Y < -0.55f ? -1 : 0;
        if (locomotionTracked && direction == 0) m_waitForThrottleCenter = false;
        if (m_waitForThrottleCenter) direction = 0;
        m_throttleRepeat -= (float)delta;
        if (direction != 0 && (direction != m_throttleDirection || m_throttleRepeat <= 0))
        {
            m_player.AdjustVrThrottle(direction);
            m_throttleRepeat = direction == m_throttleDirection ? 0.22f : 0.4f;
        }
        m_throttleDirection = direction;

        if (firing && !m_requireRelease)
        {
            m_requireRelease = true;
            m_player.VrFire();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (QuestVrRuntime.Preview && inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            if (BenchmarkActive) BenchmarkCancelRequested?.Invoke();
            else Menu?.Toggle();
        }
    }

    private void UpdateHeadAim(float delta, bool snap = false)
    {
        var hadHeadAim = m_hasHeadAim;
        m_hasHeadAim = false;
        if (!GodotObject.IsInstanceValid(AimSurface) || AimSurface.Mesh is not QuadMesh quad) return;
        var vertices = m_player.Cockpit.MainWindshieldVertices;
        if (vertices.Length is < 3 or > 32) return;
        Span<System.Numerics.Vector3> projectedVertices = stackalloc System.Numerics.Vector3[vertices.Length];
        var glassToHud = AimSurface.GlobalTransform.AffineInverse() * m_player.Cockpit.MainWindshieldTransform;
        for (var i = 0; i < vertices.Length; i++) projectedVertices[i] = ToNumerics(glassToHud * vertices[i]);
        var eye = AimSurface.ToLocal(Camera.GlobalPosition);
        var forward = AimSurface.GlobalBasis.Inverse() * -Camera.GlobalBasis.Z;
        if (CockpitGazeAim.TryGetAimPoint(ToNumerics(eye), ToNumerics(forward), projectedVertices,
                new System.Numerics.Vector2(quad.Size.X, quad.Size.Y) * 0.5f, WindshieldAimInset, out var point))
        {
            var target = new Vector2(point.X, point.Y);
            // Smooth the shared reticle/weapon ray, so the displayed aim still matches shots.
            var blend = 1.0f - Mathf.Exp(-HeadAimFollowRate * delta);
            m_headAimPoint = hadHeadAim && !snap ? m_headAimPoint.Lerp(target, blend) : target;
            m_hasHeadAim = true;
        }
    }

    private static System.Numerics.Vector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);

    private bool Pressed(string action, bool down)
    {
        if (!down) { m_held.Remove(action); return false; }
        return m_held.Add(action);
    }

    private static float Deadzone(float value) => Math.Abs(value) <= 0.2f ? 0 : Mathf.Sign(value) * (Math.Abs(value) - 0.2f) / 0.8f;
}
