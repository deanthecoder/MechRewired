// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>
/// Provides stable attachment points for player locomotion, torso articulation and cameras.
/// </summary>
public partial class PlayerMech : Node3D
{
    public const uint ExteriorRenderLayer = 1u << 1;

    private const float MaximumSlopeDegrees = 35.0f;
    private const float MouseSensitivity = 0.002f;
    private const float MaximumTorsoYaw = Mathf.Pi / 2.0f;
    private const float MinimumTorsoPitch = -Mathf.Pi / 6.0f;
    private const float MaximumTorsoPitch = Mathf.Pi / 4.0f;
    private const float KeyboardTorsoSpeed = Mathf.Pi / 3.0f;
    private const float TorsoAimResponse = 5.0f;
    private const float GaitCycleDistance = 18.0f;
    private const float MaximumGaitSpeedFraction = 0.4f;
    private const float GaitEngageRate = 4.0f;
    private const float GaitSettleRate = 0.6f;
    private const float PivotGaitRadius = 8.0f;
    private const float PivotGaitWeight = 0.18f;
    private const float CockpitRelativeVerticalGait = 0.055f;
    private const float CockpitRelativeLateralGait = 0.034f;
    private const float CockpitRelativeRollGait = 0.016f;
    private const float CockpitTorsoYawFactor = 0.08f;
    private const float CockpitPitchDegrees = -19.0f;
    private const float MotorSettleSeconds = 0.15f;

    private IReadOnlyList<DebugTriangle> m_terrainTriangles = Array.Empty<DebugTriangle>();
    private float m_modelBottomY;
    private float m_torsoYaw;
    private float m_torsoPitch;
    private float m_targetTorsoYaw;
    private float m_targetTorsoPitch;
    private float m_gaitPhase;
    private float m_gaitWeight;
    private float m_motorIdleTime;
    private bool m_gaitActive;
    private int m_footfallCount;
    private bool m_slopeBlocked;
    private readonly AudioStreamPlayer m_torsoMotor;
    private readonly AudioStreamPlayer m_footfall;
    private readonly AudioStreamPlayer m_startup;
    private readonly AudioStreamPlayer m_deploymentReport;

    public PlayerMech(double maximumForwardSpeedKph, PlayerMechSounds sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        Name = "PlayerMech";
        Drive = new MechDrive(new MechDriveProfile(maximumForwardSpeedKph));
        Legs = new Node3D { Name = "Legs" };
        Torso = new Node3D { Name = "Torso" };
        CockpitMount = new Node3D { Name = "CockpitMount" };
        ViewBobMount = new Node3D { Name = "ViewBobMount" };
        AddChild(Legs);
        AddChild(Torso);
        Torso.AddChild(CockpitMount);
        CockpitMount.AddChild(ViewBobMount);

        m_torsoMotor = new AudioStreamPlayer
        {
            Name = "TorsoMotor",
            Stream = sounds.TorsoMotor,
            VolumeDb = -10.0f
        };
        m_footfall = new AudioStreamPlayer
        {
            Name = "Footfall",
            Stream = sounds.Footfall,
            VolumeDb = -4.0f,
            MaxPolyphony = 2
        };
        m_startup = new AudioStreamPlayer
        {
            Name = "Startup",
            Stream = sounds.Startup,
            VolumeDb = -3.0f
        };
        m_deploymentReport = new AudioStreamPlayer
        {
            Name = "DeploymentReport",
            Stream = sounds.DeploymentReport
        };
        AddChild(m_torsoMotor);
        AddChild(m_footfall);
        AddChild(m_startup);
        AddChild(m_deploymentReport);
    }

    public MechDrive Drive { get; }

    public Node3D Legs { get; }

    public Node3D Torso { get; }

    public Node3D CockpitMount { get; }

    public Node3D ViewBobMount { get; }

    public PlayerCockpitCamera CockpitCamera { get; private set; }

    public PlayerCockpit Cockpit { get; private set; }

    public Camera3D ExternalCamera { get; private set; }

    public Node3D GetPartParent(string partName) => partName switch
    {
        "Torso" or "Windshield" or "LeftArm" or "RightArm" => Torso,
        _ => Legs
    };

    public void Configure(Aabb modelBounds, IReadOnlyList<DebugTriangle> sceneTriangles)
    {
        m_modelBottomY = modelBounds.Position.Y;
        m_terrainTriangles = sceneTriangles
            .Where(triangle =>
                triangle.ResourcePath == "IMPLICIT/GROUND" ||
                triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal))
            .ToArray();
        var cockpitHeight = modelBounds.Position.Y + modelBounds.Size.Y - 0.8f;
        var cockpitFront = modelBounds.Position.Z - 0.15f;
        CockpitMount.Position = new Vector3(0.0f, cockpitHeight, cockpitFront);
        Cockpit = new PlayerCockpit();
        CockpitMount.AddChild(Cockpit);

        CockpitCamera = new PlayerCockpitCamera
        {
            Name = "CockpitCamera",
            Current = true,
            Near = 0.05f,
            Far = 8000.0f,
            Fov = 80.0f,
            CullMask = 1u | PlayerCockpit.RenderLayer
        };
        ViewBobMount.AddChild(CockpitCamera);

        ExternalCamera = new Camera3D
        {
            Name = "ExternalCamera",
            Current = false,
            Far = 8000.0f,
            CullMask = 1u | ExteriorRenderLayer
        };
        AddChild(ExternalCamera);
        var target = modelBounds.GetCenter();
        var cameraPosition = new Vector3(0.0f, target.Y + modelBounds.Size.Y * 0.45f, modelBounds.Size.Z * 2.5f + 12.0f);
        ExternalCamera.Position = cameraPosition;
        ExternalCamera.LookAt(ToGlobal(target));
        m_startup.Play();
        m_deploymentReport.Play();

        GD.Print(
            $"MechRewired: player controls ready (1-0 throttle; -/= adjust; Backspace reverses; " +
            $"Left/Right steer; mouse aims torso; keypad 5 centers; " +
            $"maximum {Drive.Profile.MaximumForwardSpeedKph:F1} km/h, " +
            $"reverse {Drive.Profile.MaximumForwardSpeedKph * Drive.Profile.ReverseSpeedFactor:F1} km/h).");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (CockpitCamera == null)
        {
            return;
        }

        var isPilotCamera = CockpitCamera.Current || ExternalCamera.Current;
        var headLookHeld = Input.IsPhysicalKeyPressed(Key.Shift);
        var steering = 0.0;
        if (isPilotCamera && !headLookHeld)
        {
            if (Input.IsPhysicalKeyPressed(Key.Left))
            {
                steering += 1.0;
            }

            if (Input.IsPhysicalKeyPressed(Key.Right))
            {
                steering -= 1.0;
            }
        }

        ApplyKeyboardTorsoAim(delta, isPilotCamera, headLookHeld);
        var torsoAngularSpeed = ApplySmoothedTorsoAim((float)delta);
        var driveStep = Drive.Advance(delta, steering);
        RotateY(Mathf.DegToRad((float)driveStep.HeadingChangeDegrees));
        var appliedDistance = TryMoveAcrossTerrain((float)driveStep.DistanceMeters)
            ? Math.Abs((float)driveStep.DistanceMeters)
            : 0.0f;
        ApplyCockpitGait(
            appliedDistance,
            Mathf.Abs(Mathf.DegToRad((float)driveStep.HeadingChangeDegrees)),
            (float)delta);
        var chassisAngularSpeed = Mathf.Abs(Mathf.DegToRad((float)driveStep.HeadingChangeDegrees)) / (float)delta;
        UpdateMotorAudio(Mathf.Max(torsoAngularSpeed, chassisAngularSpeed), (float)delta);
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (CockpitCamera == null || (!CockpitCamera.Current && !ExternalCamera.Current))
        {
            return;
        }

        switch (inputEvent)
        {
            case InputEventKey { Pressed: true, Echo: false } keyEvent when TryHandleDriveKey(keyEvent.Keycode):
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false } centerEvent
                when centerEvent.Keycode is Key.Kp5 or Key.C:
                CenterPilotView();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                if (Input.MouseMode == Input.MouseModeEnum.Captured)
                {
                    GD.Print("MechRewired: weapon fire requested (weapon simulation not yet implemented).");
                }
                else
                {
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                }

                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
                when Input.MouseMode == Input.MouseModeEnum.Captured:
                GD.Print("MechRewired: weapon cycle requested (weapon simulation not yet implemented).");
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Middle }
                when Input.MouseMode == Input.MouseModeEnum.Captured:
                GD.Print("MechRewired: target-under-reticle requested (targeting not yet implemented).");
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion mouseMotion when Input.MouseMode == Input.MouseModeEnum.Captured:
                m_targetTorsoYaw = Mathf.Clamp(
                    m_targetTorsoYaw - mouseMotion.Relative.X * MouseSensitivity,
                    -MaximumTorsoYaw,
                    MaximumTorsoYaw);
                m_targetTorsoPitch = Mathf.Clamp(
                    m_targetTorsoPitch - mouseMotion.Relative.Y * MouseSensitivity,
                    MinimumTorsoPitch,
                    MaximumTorsoPitch);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public void LogMovementState()
    {
        GD.Print(
            $"MechRewired: PlayerMech throttle {Drive.ThrottlePercent}% " +
            $"{(Drive.IsReversing ? "reverse" : "forward")}; speed {Drive.CurrentSpeedKph:F1} km/h; " +
            $"target {Drive.TargetSpeedKph:F1} km/h; torso yaw {Mathf.RadToDeg(m_torsoYaw):F1} degrees, " +
            $"pitch {Mathf.RadToDeg(m_torsoPitch):F1} degrees (target " +
            $"{Mathf.RadToDeg(m_targetTorsoYaw):F1}, {Mathf.RadToDeg(m_targetTorsoPitch):F1}).");
    }

    private bool TryHandleDriveKey(Key key)
    {
        var throttleKey = key switch
        {
            Key.Key0 => 0,
            Key.Key1 => 1,
            Key.Key2 => 2,
            Key.Key3 => 3,
            Key.Key4 => 4,
            Key.Key5 => 5,
            Key.Key6 => 6,
            Key.Key7 => 7,
            Key.Key8 => 8,
            Key.Key9 => 9,
            _ => -1
        };
        if (throttleKey >= 0)
        {
            Drive.SetThrottleKey(throttleKey);
            LogThrottleChange();
            return true;
        }

        switch (key)
        {
            case Key.Equal:
                Drive.IncreaseThrottle();
                LogThrottleChange();
                return true;

            case Key.Minus:
                Drive.DecreaseThrottle();
                LogThrottleChange();
                return true;

            case Key.Backspace:
            case Key.Quoteleft:
                Drive.ToggleDirection();
                LogThrottleChange();
                return true;

            default:
                return false;
        }
    }

    private void LogThrottleChange()
    {
        GD.Print(
            $"MechRewired: throttle {Drive.ThrottlePercent}% " +
            $"{(Drive.IsReversing ? "reverse" : "forward")} selected; " +
            $"target speed {Drive.TargetSpeedKph:F1} km/h.");
    }

    private void ApplyKeyboardTorsoAim(double delta, bool isPilotCamera, bool controlHeld)
    {
        if (!isPilotCamera || controlHeld)
        {
            return;
        }

        var yawInput = 0.0f;
        if (Input.IsPhysicalKeyPressed(Key.Comma))
        {
            yawInput += 1.0f;
        }

        if (Input.IsPhysicalKeyPressed(Key.Period))
        {
            yawInput -= 1.0f;
        }

        var pitchInput = 0.0f;
        if (Input.IsPhysicalKeyPressed(Key.Up))
        {
            pitchInput += 1.0f;
        }

        if (Input.IsPhysicalKeyPressed(Key.Down))
        {
            pitchInput -= 1.0f;
        }

        if (yawInput == 0.0f && pitchInput == 0.0f)
        {
            return;
        }

        m_targetTorsoYaw = Mathf.Clamp(
            m_targetTorsoYaw + yawInput * KeyboardTorsoSpeed * (float)delta,
            -MaximumTorsoYaw,
            MaximumTorsoYaw);
        m_targetTorsoPitch = Mathf.Clamp(
            m_targetTorsoPitch + pitchInput * KeyboardTorsoSpeed * (float)delta,
            MinimumTorsoPitch,
            MaximumTorsoPitch);
    }

    private float ApplySmoothedTorsoAim(float delta)
    {
        var previousYaw = m_torsoYaw;
        var previousPitch = m_torsoPitch;
        var blend = 1.0f - Mathf.Exp(-TorsoAimResponse * delta);
        m_torsoYaw = Mathf.LerpAngle(m_torsoYaw, m_targetTorsoYaw, blend);
        m_torsoPitch = Mathf.LerpAngle(m_torsoPitch, m_targetTorsoPitch, blend);
        Torso.Rotation = new Vector3(m_torsoPitch, m_torsoYaw, 0.0f);
        return (Mathf.Abs(Mathf.AngleDifference(previousYaw, m_torsoYaw)) +
                Mathf.Abs(Mathf.AngleDifference(previousPitch, m_torsoPitch))) / delta;
    }

    private void CenterPilotView()
    {
        m_targetTorsoYaw = 0.0f;
        m_targetTorsoPitch = 0.0f;
        CockpitCamera.CenterView();
        GD.Print("MechRewired: centered torso and pilot view.");
    }

    private bool TryMoveAcrossTerrain(float distanceMeters)
    {
        if (Mathf.IsZeroApprox(distanceMeters))
        {
            return true;
        }

        var candidate = Position - GlobalBasis.Z * distanceMeters;
        if (!TryGetSurface(candidate, out var surfaceHeight, out var slopeDegrees))
        {
            return false;
        }

        if (slopeDegrees > MaximumSlopeDegrees)
        {
            if (!m_slopeBlocked)
            {
                GD.Print(
                    $"MechRewired: PlayerMech movement blocked by {slopeDegrees:F1}-degree terrain " +
                    $"(limit {MaximumSlopeDegrees:F1} degrees).");
            }

            m_slopeBlocked = true;
            return false;
        }

        m_slopeBlocked = false;
        candidate.Y = surfaceHeight - m_modelBottomY;
        Position = candidate;
        return true;
    }

    private bool TryGetSurface(Vector3 position, out float height, out float slopeDegrees)
    {
        const float rayHeight = 10000.0f;
        var origin = new Vector3(position.X, rayHeight, position.Z);
        if (!DebugTriangleRaycaster.TryFindNearest(
                m_terrainTriangles,
                origin,
                Vector3.Down,
                out var triangle,
                out var distance))
        {
            height = 0.0f;
            slopeDegrees = 0.0f;
            return false;
        }

        height = origin.Y - distance;
        var normal = (triangle.B - triangle.A).Cross(triangle.C - triangle.A).Normalized();
        var verticalAlignment = Mathf.Clamp(Mathf.Abs(normal.Dot(Vector3.Up)), 0.0f, 1.0f);
        slopeDegrees = Mathf.RadToDeg(Mathf.Acos(verticalAlignment));
        return true;
    }

    private void ApplyCockpitGait(float distanceMeters, float headingChangeRadians, float delta)
    {
        var speedFraction = (float)Drive.SpeedFraction;
        var movementWeight = distanceMeters > 0.0001f
            ? Mathf.Min(speedFraction, MaximumGaitSpeedFraction)
            : 0.0f;
        var pivotWeight = headingChangeRadians > 0.0001f ? PivotGaitWeight : 0.0f;
        var targetWeight = Mathf.Max(movementWeight, pivotWeight);
        var gaitWeightRate = targetWeight < m_gaitWeight ? GaitSettleRate : GaitEngageRate;
        m_gaitWeight = Mathf.MoveToward(m_gaitWeight, targetWeight, delta * gaitWeightRate);
        var gaitActive = distanceMeters > 0.0f || headingChangeRadians > 0.0f;
        if (gaitActive)
        {
            var strideScale = Mathf.Max(1.0f, speedFraction / MaximumGaitSpeedFraction);
            var movementPhase = distanceMeters / (GaitCycleDistance * strideScale);
            var pivotPhase = headingChangeRadians * PivotGaitRadius / GaitCycleDistance;
            var phaseAdvance = Mathf.Max(movementPhase, pivotPhase) * Mathf.Tau;
            var crossedFootfall = Mathf.FloorToInt((m_gaitPhase + phaseAdvance) / Mathf.Pi) >
                                  Mathf.FloorToInt(m_gaitPhase / Mathf.Pi);
            m_gaitPhase = Mathf.PosMod(m_gaitPhase + phaseAdvance, Mathf.Tau);
            if (!m_gaitActive || crossedFootfall)
            {
                PlayFootfall();
            }
        }

        m_gaitActive = gaitActive;

        var landingPulse = Mathf.Pow(Mathf.Max(0.0f, Mathf.Cos(m_gaitPhase * 2.0f)), 10.0f);
        var vertical = (Mathf.Sin(m_gaitPhase * 2.0f) * 0.015f - landingPulse * 0.07f) * m_gaitWeight;
        var lateral = Mathf.Sin(m_gaitPhase) * 0.025f * m_gaitWeight;
        var roll = -Mathf.Sin(m_gaitPhase) * 0.012f * m_gaitWeight;
        var viewOffset = new Vector3(lateral, vertical, 0.0f);
        ViewBobMount.Position = viewOffset;
        ViewBobMount.Rotation = new Vector3(0.0f, 0.0f, roll);

        // Express the cockpit's motion relative to the moving camera as smooth hydraulic travel.
        // Adding the camera transform first cancels its sharp landing pulse from the visible frame.
        var cockpitRelativeOffset = new Vector3(
            -Mathf.Sin(m_gaitPhase) * CockpitRelativeLateralGait,
            -Mathf.Sin(m_gaitPhase * 2.0f) * CockpitRelativeVerticalGait,
            0.0f) * m_gaitWeight;
        var cockpitRelativeRoll = Mathf.Sin(m_gaitPhase) * CockpitRelativeRollGait * m_gaitWeight;
        Cockpit.SetPose(
            CockpitPitchDegrees,
            m_torsoYaw * CockpitTorsoYawFactor,
            viewOffset + cockpitRelativeOffset,
            roll + cockpitRelativeRoll);
    }

    private void UpdateMotorAudio(float angularSpeed, float delta)
    {
        if (angularSpeed > 0.005f)
        {
            m_motorIdleTime = 0.0f;
            m_torsoMotor.PitchScale = Mathf.Clamp(0.85f + angularSpeed * 0.3f, 0.85f, 1.15f);
            if (!m_torsoMotor.Playing)
            {
                m_torsoMotor.Play();
            }

            return;
        }

        m_motorIdleTime += delta;
        if (m_motorIdleTime >= MotorSettleSeconds && m_torsoMotor.Playing)
        {
            m_torsoMotor.Stop();
        }
    }

    private void PlayFootfall()
    {
        m_footfall.PitchScale = m_footfallCount++ % 2 == 0 ? 0.97f : 1.03f;
        m_footfall.Play();
    }
}
