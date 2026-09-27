// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// for any purpose. THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;

namespace MechRewired;

/// <summary>Seat-relative tracking and controller input. Tracking keeps running while the mission is paused.</summary>
public partial class QuestVrRig : XROrigin3D
{
    private static readonly Vector3 SeatOffset = new(0.0f, 0.12f, 0.10f);
    private readonly PlayerMech m_player;
    private readonly OpenXRInterface m_interface;
    private readonly HashSet<string> m_held = new();
    private bool m_seatCentered;
    private bool m_requireRelease = true;
    private bool m_jumpJetsRequireRelease = true;
    private float m_throttleRepeat;
    private int m_throttleDirection;
    private bool m_waitForThrottleCenter = true;
    private float m_fireRepeat;

    public XRCamera3D Camera { get; }
    public XRController3D Left { get; }
    public XRController3D Right { get; }
    public XRController3D RightAim { get; }
    public QuestVrMenu Menu { get; set; }
    public float Steering { get; private set; }
    public bool JumpJetsRequested { get; private set; }

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
        if (!m_seatCentered && (QuestVrRuntime.Preview || headTracked)) RecenterSeat();
        var sessionFocused = QuestVrRuntime.Preview || m_interface?.GetSessionState() == OpenXRInterface.SessionState.Focused;
        if (!QuestVrRuntime.Preview && m_seatCentered && (!headTracked || !sessionFocused) && Menu is { IsOpen: false })
        {
            m_player.StopVrMovement();
            Menu.Toggle();
        }
        var leftTracked = Left.GetIsActive();
        var rightTracked = Right.GetIsActive();
        var menuPressed = Pressed("menu", leftTracked && Left.IsButtonPressed("menu_button"));
        var stopPressed = Pressed("stop", leftTracked && Left.IsButtonPressed("primary_click"));
        var weaponButtonPressed = Pressed("weapon_button", rightTracked && Right.IsButtonPressed("ax_button"));
        var weaponGripPressed = Pressed("weapon_grip", rightTracked && Right.GetFloat("grip") > 0.65f);
        var targetButtonPressed = Pressed("target_button", rightTracked && Right.IsButtonPressed("by_button"));
        var targetTriggerPressed = Pressed("target_trigger", leftTracked && Left.GetFloat("trigger") > 0.65f);
        var inspectPressed = Pressed("inspect", leftTracked && Left.IsButtonPressed("ax_button"));
        var centerPressed = Pressed("center", leftTracked && Left.IsButtonPressed("by_button"));
        if (menuPressed) Menu?.Toggle();
        var firing = rightTracked && Right.GetFloat("trigger") > 0.65f;
        var jumpJetsHeld = leftTracked && Left.GetFloat("grip") > 0.65f;
        if (GetTree().Paused || !sessionFocused || m_player.IsDestroyed || !m_seatCentered || (!QuestVrRuntime.Preview && (!headTracked || !leftTracked || !rightTracked)))
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
        if (!firing) m_requireRelease = false;
        if (!jumpJetsHeld) m_jumpJetsRequireRelease = false;
        JumpJetsRequested = jumpJetsHeld && !m_jumpJetsRequireRelease;
        if (stopPressed)
        {
            m_player.StopVrMovement();
            m_waitForThrottleCenter = true;
        }
        if (weaponButtonPressed || weaponGripPressed) m_player.VrCycleWeapon();
        if (targetButtonPressed || targetTriggerPressed) m_player.VrCycleTarget();
        if (inspectPressed) m_player.VrInspect();
        if (centerPressed) RecenterSeat();

        var movement = leftTracked ? Left.GetVector2("primary") : Vector2.Zero;
        var aimStick = rightTracked ? Right.GetVector2("primary") : Vector2.Zero;
        Steering = -Deadzone(aimStick.X);
        var direction = movement.Y > 0.55f ? 1 : movement.Y < -0.55f ? -1 : 0;
        if (direction == 0) m_waitForThrottleCenter = false;
        if (m_waitForThrottleCenter) direction = 0;
        m_throttleRepeat -= (float)delta;
        if (direction != 0 && (direction != m_throttleDirection || m_throttleRepeat <= 0))
        {
            m_player.AdjustVrThrottle(direction);
            m_throttleRepeat = direction == m_throttleDirection ? 0.22f : 0.4f;
        }
        m_throttleDirection = direction;

        m_player.SetVrPitch(m_player.VrAim.Y + Deadzone(aimStick.Y) * 0.6f * (float)delta);
        m_fireRepeat -= (float)delta;
        if (firing && !m_requireRelease && m_fireRepeat <= 0)
        {
            m_player.VrFire();
            m_fireRepeat = 0.15f;
        }
        if (!firing) m_fireRepeat = 0;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (QuestVrRuntime.Preview && inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            Menu?.Toggle();
    }

    private bool Pressed(string action, bool down)
    {
        if (!down) { m_held.Remove(action); return false; }
        return m_held.Add(action);
    }

    private static float Deadzone(float value) => Math.Abs(value) <= 0.2f ? 0 : Mathf.Sign(value) * (Math.Abs(value) - 0.2f) / 0.8f;
}
