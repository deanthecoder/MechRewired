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
using MechRewired.Resources;
using MechRewired.Simulation;

namespace MechRewired;

/// <summary>
/// Provides stable attachment points for player locomotion, torso articulation and cameras.
/// </summary>
public partial class PlayerMech : Node3D
{
    public const uint ExteriorRenderLayer = 1u << 1;

    private const float DefaultDisplayFov = 80.0f;
    private const float MinimumDisplayFov = 28.0f;
    private const float DisplayZoomDegreesPerSecond = 35.0f;

    private const float MaximumSlopeDegrees = 50.0f;
    private const float MaximumUphillSpeedReduction = 0.3f;
    private const float MouseSensitivity = 0.002f;
    private const float MaximumTorsoYaw = Mathf.Pi / 2.0f;
    private const float MinimumTorsoPitch = -Mathf.Pi / 6.0f;
    private const float MaximumTorsoPitch = Mathf.Pi / 4.0f;
    private const float KeyboardTorsoSpeed = Mathf.Pi / 3.0f;
    private const float TorsoAimResponse = 5.0f;
    private const float CockpitRelativeVerticalGait = 0.055f;
    private const float CockpitRelativeLateralGait = 0.034f;
    private const float CockpitRelativeRollGait = 0.016f;
    private const float CockpitTorsoYawFactor = 0.08f;
    private const float CockpitPitchDegrees = -19.0f;
    private const float MotorSettleSeconds = 0.15f;
    private const float LegAlignmentTolerance = 0.005f;
    private const float DamageShudderDuration = 0.55f;
    private const float DamageShudderFrequency = 14.0f;

    private IReadOnlyList<DebugTriangle> m_terrainTriangles = Array.Empty<DebugTriangle>();
    private Func<IReadOnlyList<SceneryObstacle>> m_sceneryObstacleProvider = () => Array.Empty<SceneryObstacle>();
    private float m_modelBottomY;
    private float m_footprintRadius;
    private float m_torsoYaw;
    private float m_torsoPitch;
    private float m_targetTorsoYaw;
    private float m_targetTorsoPitch;
    private float m_motorIdleTime;
    private int m_footfallCount;
    private bool m_slopeBlocked;
    private bool m_sceneryBlocked;
    private SceneryObstacle m_lastBlockingObstacle;
    private bool m_aligningLegsToTorso;
    private bool m_translationLocked;
    private string m_translationLockReason = string.Empty;
    private bool m_displayZoomMoving;
    private float m_damageShudderRemaining;
    private float m_damageShudderStrength;
    private int m_nextDamageImpact;
    private readonly AudioStreamPlayer m_torsoMotor;
    private readonly MechRig m_mechRig;
    private readonly AudioStreamPlayer m_footfall;
    private readonly AudioStreamPlayer m_startup;
    private readonly AudioStreamPlayer m_reactorHum;
    private readonly AudioStreamPlayer m_deploymentReport;
    private readonly AudioStreamPlayer m_displayZoom;
    private readonly AudioStreamPlayer m_driveTransition;
    private readonly AudioStreamPlayer m_damageImpact;
    private readonly AudioStreamPlayer m_criticalHit;
    private readonly IReadOnlyList<AudioStreamWav> m_damageImpactSounds;
    private readonly AudioStreamWav m_startWalking;
    private readonly AudioStreamWav m_stopWalking;
    private readonly AudioStreamWav m_startRunning;
    private readonly AudioStreamWav m_stopRunning;
    private readonly double m_cruisingSpeedKph;
    private readonly MechDamageModel m_damageModel;
    private readonly List<(MeshInstance3D Mesh, string PartName)> m_destructibleParts = new();
    private Aabb m_localBounds;

    public PlayerMech(
        MechWarriorMechFile mechDefinition,
        PlayerMechSounds sounds)
    {
        ArgumentNullException.ThrowIfNull(mechDefinition);
        ArgumentNullException.ThrowIfNull(sounds);
        if (mechDefinition.CruisingSpeedKph <= 0.0 ||
            mechDefinition.CruisingSpeedKph >= mechDefinition.MaximumSpeedKph)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mechDefinition),
                "Cruising speed must be positive and below maximum forward speed.");
        }

        Name = "PlayerMech";
        m_damageModel = new MechDamageModel(mechDefinition.Sections);
        m_cruisingSpeedKph = mechDefinition.CruisingSpeedKph;
        Drive = new MechDrive(new MechDriveProfile(mechDefinition.MaximumSpeedKph));
        Legs = new Node3D { Name = "Legs" };
        Torso = new Node3D { Name = "Torso" };
        CockpitMount = new Node3D { Name = "CockpitMount" };
        ViewBobMount = new Node3D { Name = "ViewBobMount" };
        AddChild(Legs);
        AddChild(Torso);
        m_mechRig = new MechRig { Name = "MechRig" };
        AddChild(m_mechRig);
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
        m_reactorHum = new AudioStreamPlayer
        {
            Name = "ReactorHum",
            Stream = sounds.ReactorHum,
            VolumeDb = -12.0f
        };
        m_deploymentReport = new AudioStreamPlayer
        {
            Name = "DeploymentReport",
            Stream = sounds.DeploymentReport
        };
        m_displayZoom = new AudioStreamPlayer
        {
            Name = "DisplayZoom",
            Stream = sounds.DisplayZoom,
            VolumeDb = -2.0f,
            MaxPolyphony = 2
        };
        m_startWalking = sounds.StartWalking;
        m_stopWalking = sounds.StopWalking;
        m_startRunning = sounds.StartRunning;
        m_stopRunning = sounds.StopRunning;
        m_driveTransition = new AudioStreamPlayer
        {
            Name = "DriveTransition",
            VolumeDb = -4.0f
        };
        m_damageImpactSounds = sounds.WeaponImpacts;
        m_damageImpact = new AudioStreamPlayer
        {
            Name = "DamageImpact",
            VolumeDb = -1.0f,
            MaxPolyphony = 4
        };
        m_criticalHit = new AudioStreamPlayer
        {
            Name = "CriticalHitReport",
            Stream = sounds.CriticalHit,
            VolumeDb = -0.5f
        };
        AddChild(m_torsoMotor);
        AddChild(m_footfall);
        AddChild(m_startup);
        AddChild(m_reactorHum);
        AddChild(m_deploymentReport);
        AddChild(m_displayZoom);
        AddChild(m_driveTransition);
        AddChild(m_damageImpact);
        AddChild(m_criticalHit);
    }

    public MechDrive Drive { get; }

    public float TorsoYawRadians => m_torsoYaw;

    public float FeetElevation => Position.Y + m_modelBottomY;

    public float ActualSpeedKph { get; private set; }

    public int Health => m_damageModel.Health;

    public int MaximumHealth => m_damageModel.MaximumHealth;

    public bool IsDestroyed => m_damageModel.IsDestroyed;

    public MechDamageModel Damage => m_damageModel;

    public bool IsImmobilized =>
        m_damageModel.IsSectionDestroyed(MechDamageSection.LeftLeg) ||
        m_damageModel.IsSectionDestroyed(MechDamageSection.RightLeg);

    public bool IsWeaponSideOperational(int side) => side < 0
        ? !m_damageModel.IsSectionDestroyed(MechDamageSection.LeftArm)
        : !m_damageModel.IsSectionDestroyed(MechDamageSection.RightArm);

    public Vector3 TargetPosition => CockpitMount?.GlobalPosition ?? GlobalPosition + Vector3.Up * 8.0f;

    public Aabb WorldBounds => GlobalTransform * m_localBounds;

    public Node3D Legs { get; }

    public Node3D Torso { get; }

    public Node3D CockpitMount { get; }

    public Node3D ViewBobMount { get; }

    public PlayerCockpitCamera CockpitCamera { get; private set; }

    public PlayerCockpit Cockpit { get; private set; }

    public Camera3D ExternalCamera { get; private set; }

    public event Action FireRequested;

    public event Action TargetRequested;

    public event Action NextTargetRequested;

    public event Action PreviousTargetRequested;

    public event Action NearestEnemyTargetRequested;

    public event Action ClearTargetRequested;

    public event Action InspectTargetRequested;

    public event Action Destroyed;

    public void ApplyDamage(
        int damage,
        string attacker,
        MechDamageSection section,
        bool fromRear,
        Vector3 hitPosition)
    {
        if (damage <= 0 || IsDestroyed)
        {
            return;
        }

        var previousHealth = Health;
        var result = m_damageModel.ApplyDamage(section, damage, fromRear);
        PlayDamageImpact();
        m_damageShudderRemaining = DamageShudderDuration;
        m_damageShudderStrength = Mathf.Clamp(damage / 8.0f, 0.5f, 1.25f);
        GD.Print(
            $"MechRewired: PlayerMech {section}{(result.RearArmorHit ? " rear" : string.Empty)} " +
            $"hit by {attacker} for {damage} damage ({Health}/{MaximumHealth} aggregate; " +
            $"{m_damageModel.GetRemaining(section).InternalStructure}/" +
            $"{m_damageModel.GetMaximum(section).InternalStructure} internal).");
        var criticalThreshold = MaximumHealth / 3.0f;
        if (Health > 0 && previousHealth > criticalThreshold && Health <= criticalThreshold)
        {
            m_criticalHit.Play();
            GD.Print("MechRewired: PlayerMech entered critical-health state.");
        }

        if (result.SectionNewlyDestroyed && !result.MechDestroyed)
        {
            DetachSection(section, hitPosition);
            if (section is MechDamageSection.LeftLeg or MechDamageSection.RightLeg)
            {
                LockChassisForLegLoss(section);
            }
        }

        if (result.MechDestroyed)
        {
            Drive.SelectStop();
            m_translationLocked = true;
            m_translationLockReason = "destroyed";
            StopOperationalAudio();
            DetachRemainingMech(hitPosition);
            Legs.Visible = false;
            Torso.Visible = false;
            GD.Print("MechRewired: PlayerMech destroyed.");
            Destroyed?.Invoke();
        }
    }

    /// <summary>
    /// Stops and locks forward/reverse travel while leaving stationary steering and aiming available.
    /// </summary>
    public void LockMovementForExtraction()
    {
        if (m_translationLocked)
        {
            return;
        }

        var previousTargetSpeedKph = Drive.TargetSpeedKph;
        m_translationLocked = true;
        m_translationLockReason = "extraction";
        Drive.SelectStop();
        PlayDriveTransition(previousTargetSpeedKph);
        GD.Print(
            "MechRewired: extraction reached; PlayerMech braking to 0 km/h with translation controls locked " +
            "(steering and torso controls remain active).");
    }

    public Node3D GetPartParent(string partName) => partName switch
    {
        "Torso" or "Windshield" or "LeftDecal" or "RightDecal" or "LeftArm" or "RightArm" => Torso,
        _ => Legs
    };

    public bool RegisterGaitPart(Node3D node, string partName) =>
        m_mechRig.RegisterPart(node, partName);

    public void RegisterDestructiblePart(MeshInstance3D mesh, string partName)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        m_destructibleParts.Add((mesh, partName));
    }

    public bool TryRaycastSections(
        Vector3 origin,
        Vector3 direction,
        out MechSectionHit hit) =>
        MechSectionHitTester.TryFindNearest(
            this,
            m_destructibleParts.Where(part => IsAncestorOf(part.Mesh)),
            origin,
            direction,
            out hit);

    public void Configure(
        Aabb modelBounds,
        IReadOnlyList<DebugTriangle> sceneTriangles,
        Func<IReadOnlyList<SceneryObstacle>> sceneryObstacleProvider)
    {
        ArgumentNullException.ThrowIfNull(sceneryObstacleProvider);
        m_localBounds = modelBounds;
        m_modelBottomY = modelBounds.Position.Y;
        m_footprintRadius = Mathf.Max(modelBounds.Size.X, modelBounds.Size.Z) * 0.35f;
        m_terrainTriangles = sceneTriangles
            .Where(triangle =>
                triangle.ResourcePath == "IMPLICIT/GROUND" ||
                triangle.ResourcePath.StartsWith("POLY/T_", StringComparison.Ordinal))
            .ToArray();
        m_sceneryObstacleProvider = sceneryObstacleProvider;
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
            Fov = DefaultDisplayFov,
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
        m_reactorHum.Play();
        m_deploymentReport.Play();

        GD.Print(
            $"MechRewired: player controls ready (1-0 throttle; -/= adjust; Backspace reverses; " +
            $"Left/Right steer; mouse aims torso; M aligns legs; keypad 5 centers; " +
            $"maximum {Drive.Profile.MaximumForwardSpeedKph:F1} km/h, " +
            $"reverse {Drive.Profile.MaximumForwardSpeedKph * Drive.Profile.ReverseSpeedFactor:F1} km/h).");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (CockpitCamera == null)
        {
            return;
        }

        if (IsDestroyed)
        {
            ActualSpeedKph = 0.0f;
            m_mechRig.Advance(0.0f, 0.0f, 0.0f, (float)delta);
            UpdateMotorAudio(0.0f, (float)delta);
            return;
        }

        var isPilotCamera = CockpitCamera.Current || ExternalCamera.Current;
        UpdateDisplayZoom((float)delta);
        var headLookHeld = Input.IsPhysicalKeyPressed(Key.Shift);
        var steering = 0.0;
        var manualSteering = false;
        if (isPilotCamera && !headLookHeld)
        {
            if (Input.IsPhysicalKeyPressed(Key.Left))
            {
                steering += 1.0;
                manualSteering = true;
            }

            if (Input.IsPhysicalKeyPressed(Key.Right))
            {
                steering -= 1.0;
                manualSteering = true;
            }
        }

        if (manualSteering)
        {
            m_aligningLegsToTorso = false;
        }

        ApplyKeyboardTorsoAim(delta, isPilotCamera, headLookHeld);
        var torsoAngularSpeed = ApplySmoothedTorsoAim((float)delta);
        if (IsImmobilized)
        {
            m_aligningLegsToTorso = false;
            ActualSpeedKph = 0.0f;
            m_mechRig.Advance(0.0f, 0.0f, 0.0f, (float)delta);
            ApplyDamageShudder((float)delta);
            UpdateMotorAudio(torsoAngularSpeed, (float)delta);
            return;
        }

        if (m_aligningLegsToTorso)
        {
            steering = Mathf.Sign(m_torsoYaw);
        }

        var driveStep = Drive.Advance(delta, steering);
        var headingChangeRadians = Mathf.DegToRad((float)driveStep.HeadingChangeDegrees);
        headingChangeRadians = ApplyLegAlignment(headingChangeRadians);
        RotateY(headingChangeRadians);
        var appliedDistance = TryMoveAcrossTerrain(
            (float)driveStep.DistanceMeters * GetDebugTravelMultiplier());
        ActualSpeedKph = Mathf.IsZeroApprox((float)delta)
            ? 0.0f
            : appliedDistance / (float)delta * 3.6f;
        ApplyCockpitGait(
            appliedDistance,
            Mathf.Abs(headingChangeRadians),
            (float)delta);
        ApplyDamageShudder((float)delta);
        var chassisAngularSpeed = Mathf.Abs(headingChangeRadians) / (float)delta;
        UpdateMotorAudio(Mathf.Max(torsoAngularSpeed, chassisAngularSpeed), (float)delta);
    }

    private void LockChassisForLegLoss(MechDamageSection section)
    {
        Drive.SelectStop();
        m_translationLocked = true;
        m_translationLockReason = $"{section} destroyed";
        ActualSpeedKph = 0.0f;
        GD.Print($"MechRewired: PlayerMech {section} destroyed; chassis translation and steering disabled.");
    }

    private void DetachSection(MechDamageSection section, Vector3 hitPosition)
    {
        var bodySections = section switch
        {
            MechDamageSection.LeftArm => new[] { MechBodySection.LeftArm },
            MechDamageSection.RightArm => new[] { MechBodySection.RightArm },
            MechDamageSection.LeftLeg => new[]
            {
                MechBodySection.LeftUpperLeg,
                MechBodySection.LeftLowerLeg,
                MechBodySection.LeftFoot
            },
            MechDamageSection.RightLeg => new[]
            {
                MechBodySection.RightUpperLeg,
                MechBodySection.RightLowerLeg,
                MechBodySection.RightFoot
            },
            _ => Array.Empty<MechBodySection>()
        };
        var parts = m_destructibleParts.Where(part =>
                IsAncestorOf(part.Mesh) &&
                bodySections.Contains(MechBodySectionClassifier.Classify(part.PartName)))
            .ToArray();
        if (parts.Length == 0)
        {
            return;
        }

        MechWreckage.Spawn(
            GetParent(),
            this,
            $"{Name}-{section}",
            parts,
            hitPosition,
            0x54494D42 + (int)section);
        GD.Print($"MechRewired: PlayerMech lost {section}; {parts.Length} authored meshes detached.");
    }

    private void DetachRemainingMech(Vector3 hitPosition)
    {
        var remaining = m_destructibleParts.Where(part => IsAncestorOf(part.Mesh)).ToArray();
        if (remaining.Length > 0)
        {
            MechWreckage.Spawn(
                GetParent(),
                this,
                Name,
                remaining,
                hitPosition,
                0x54494D42);
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (IsDestroyed)
        {
            return;
        }

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

            case InputEventKey { Keycode: Key.Z }:
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.M }:
                AlignLegsToTorso();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab }:
                RequestWeaponCycle();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.T, CtrlPressed: true }:
                ClearTargetRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.T }:
                NextTargetRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.R }:
                PreviousTargetRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.E }:
                NearestEnemyTargetRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Q }:
                TargetRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.I }:
                InspectTargetRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space }:
                RequestFire();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } leftButton:
                if (Input.MouseMode == Input.MouseModeEnum.Captured)
                {
                    if (leftButton.CtrlPressed)
                    {
                        RequestWeaponCycle();
                    }
                    else
                    {
                        RequestFire();
                    }
                }
                else
                {
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                }

                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
                when Input.MouseMode == Input.MouseModeEnum.Captured:
                RequestWeaponCycle();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Middle }
                when Input.MouseMode == Input.MouseModeEnum.Captured:
                TargetRequested?.Invoke();
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion mouseMotion when Input.MouseMode == Input.MouseModeEnum.Captured:
                m_aligningLegsToTorso = false;
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
            $"{(Drive.IsReversing ? "reverse" : "forward")}; speed {ActualSpeedKph:F1} km/h; " +
            $"target {Drive.TargetSpeedKph:F1} km/h; torso yaw {Mathf.RadToDeg(m_torsoYaw):F1} degrees, " +
            $"pitch {Mathf.RadToDeg(m_torsoPitch):F1} degrees (target " +
            $"{Mathf.RadToDeg(m_targetTorsoYaw):F1}, {Mathf.RadToDeg(m_targetTorsoPitch):F1}); " +
            $"health {Health}/{MaximumHealth}; translation lock " +
            $"{(m_translationLocked ? m_translationLockReason : "none")}.");
        if (m_slopeBlocked || m_sceneryBlocked)
        {
            var scenery = m_lastBlockingObstacle == null
                ? "none"
                : $"'{m_lastBlockingObstacle.Name}' ({m_lastBlockingObstacle.Walls.Count} wall triangles; " +
                  $"bounds {m_lastBlockingObstacle.Minimum} to {m_lastBlockingObstacle.Maximum})";
            GD.Print(
                $"MechRewired: PlayerMech movement constraint: slope={m_slopeBlocked}; " +
                $"scenery={m_sceneryBlocked}; blocker {scenery}; footprint radius {m_footprintRadius:F1}m.");
        }
    }

    private void UpdateDisplayZoom(float delta)
    {
        var zoomHeld = CockpitCamera.Current && Input.IsPhysicalKeyPressed(Key.Z);
        var targetFov = Input.IsPhysicalKeyPressed(Key.Shift)
            ? DefaultDisplayFov
            : MinimumDisplayFov;
        var previousFov = CockpitCamera.Fov;
        if (zoomHeld)
        {
            CockpitCamera.Fov = Mathf.MoveToward(
                previousFov,
                targetFov,
                DisplayZoomDegreesPerSecond * delta);
        }

        var isMoving = zoomHeld && !Mathf.IsEqualApprox(previousFov, CockpitCamera.Fov);
        if (isMoving && !m_displayZoomMoving)
        {
            m_displayZoomMoving = true;
            m_displayZoom.Play();
        }
        else if (!isMoving && m_displayZoomMoving)
        {
            m_displayZoomMoving = false;
            m_displayZoom.Stop();
            var magnification = Mathf.Tan(Mathf.DegToRad(DefaultDisplayFov * 0.5f)) /
                                Mathf.Tan(Mathf.DegToRad(CockpitCamera.Fov * 0.5f));
            GD.Print(
                $"MechRewired: cockpit display zoom {magnification:F1}x " +
                $"(field of view {CockpitCamera.Fov:F1} degrees).");
        }
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
        var previousTargetSpeedKph = Drive.TargetSpeedKph;
        if (m_translationLocked &&
            (throttleKey >= 0 || key is Key.Equal or Key.Minus or Key.Backspace or Key.Quoteleft))
        {
            GD.Print(
                $"MechRewired: PlayerMech translation command ignored; movement locked for " +
                $"{m_translationLockReason} (health {Health}/{MaximumHealth}).");
            return true;
        }

        if (throttleKey >= 0)
        {
            Drive.SetThrottleKey(throttleKey);
            PlayDriveTransition(previousTargetSpeedKph);
            LogThrottleChange();
#if DEBUG
            if (throttleKey == 0)
            {
                GD.Print("MechRewired: DEBUG speed 0 travel override active (3x movement speed).");
            }
#endif
            return true;
        }

        switch (key)
        {
            case Key.Equal:
                Drive.IncreaseThrottle();
                PlayDriveTransition(previousTargetSpeedKph);
                LogThrottleChange();
                return true;

            case Key.Minus:
                Drive.DecreaseThrottle();
                PlayDriveTransition(previousTargetSpeedKph);
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

    private float GetDebugTravelMultiplier()
    {
#if DEBUG
        return Drive.ThrottleKey == 0 ? 3.0f : 1.0f;
#else
        return 1.0f;
#endif
    }

    private static void RequestWeaponCycle()
    {
        GD.Print("MechRewired: medium laser selected (additional weapons not yet implemented).");
    }

    private void RequestFire()
    {
        if (!IsDestroyed)
        {
            FireRequested?.Invoke();
        }
    }

    private void PlayDamageImpact()
    {
        if (m_damageImpactSounds.Count == 0)
        {
            return;
        }

        m_damageImpact.Stream = m_damageImpactSounds[m_nextDamageImpact++ % m_damageImpactSounds.Count];
        m_damageImpact.Play();
    }

    private void StopOperationalAudio()
    {
        m_reactorHum.Stop();
        m_torsoMotor.Stop();
        m_footfall.Stop();
        m_startup.Stop();
        m_displayZoom.Stop();
        m_driveTransition.Stop();
    }

    private void PlayDriveTransition(double previousTargetSpeedKph)
    {
        var previousSpeed = Math.Abs(previousTargetSpeedKph);
        var selectedSpeed = Math.Abs(Drive.TargetSpeedKph);
        if (previousSpeed < 0.001 && selectedSpeed >= 0.001)
        {
            m_driveTransition.Stream = m_startWalking;
        }
        else if (previousSpeed >= 0.001 && selectedSpeed < 0.001)
        {
            m_driveTransition.Stream = m_stopWalking;
        }
        else if (previousSpeed <= m_cruisingSpeedKph && selectedSpeed > m_cruisingSpeedKph)
        {
            m_driveTransition.Stream = m_startRunning;
        }
        else if (previousSpeed > m_cruisingSpeedKph && selectedSpeed <= m_cruisingSpeedKph)
        {
            m_driveTransition.Stream = m_stopRunning;
        }
        else
        {
            return;
        }

        m_driveTransition.Play();
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

        m_aligningLegsToTorso = false;
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
        m_aligningLegsToTorso = false;
        m_targetTorsoYaw = 0.0f;
        m_targetTorsoPitch = 0.0f;
        CockpitCamera.CenterView();
        GD.Print("MechRewired: centered torso and pilot view.");
    }

    private void AlignLegsToTorso()
    {
        if (Mathf.Abs(m_torsoYaw) <= LegAlignmentTolerance)
        {
            GD.Print("MechRewired: legs already aligned with torso.");
            return;
        }

        m_targetTorsoYaw = m_torsoYaw;
        m_aligningLegsToTorso = true;
        GD.Print(
            $"MechRewired: aligning legs to torso bearing " +
            $"({Mathf.RadToDeg(m_torsoYaw):F1} degrees relative).");
    }

    private float ApplyLegAlignment(float proposedHeadingChange)
    {
        if (!m_aligningLegsToTorso)
        {
            return proposedHeadingChange;
        }

        var headingChange = Mathf.Sign(m_torsoYaw) * Mathf.Min(
            Mathf.Abs(proposedHeadingChange),
            Mathf.Abs(m_torsoYaw));
        m_torsoYaw -= headingChange;
        m_targetTorsoYaw -= headingChange;
        if (Mathf.Abs(m_torsoYaw) <= LegAlignmentTolerance)
        {
            m_torsoYaw = 0.0f;
            m_targetTorsoYaw = 0.0f;
            m_aligningLegsToTorso = false;
            GD.Print("MechRewired: legs aligned with torso.");
        }

        Torso.Rotation = new Vector3(m_torsoPitch, m_torsoYaw, 0.0f);
        return headingChange;
    }

    private float TryMoveAcrossTerrain(float distanceMeters)
    {
        if (Mathf.IsZeroApprox(distanceMeters))
        {
            return 0.0f;
        }

        var obstacles = m_sceneryObstacleProvider();
        if (SceneryCollision.TryResolveOverlap(
                new System.Numerics.Vector2(Position.X, Position.Z),
                m_footprintRadius,
                obstacles,
                out var resolvedPosition,
                out var overlappingObstacle))
        {
            var depenetratedPosition = new Vector3(resolvedPosition.X, Position.Y, resolvedPosition.Y);
            if (TryGetSurface(depenetratedPosition, out var resolvedSurfaceHeight, out _))
            {
                depenetratedPosition.Y = resolvedSurfaceHeight - m_modelBottomY;
                Position = depenetratedPosition;
                GD.Print(
                    $"MechRewired: moved PlayerMech out of overlapping scenery " +
                    $"'{overlappingObstacle.Name}'.");
            }
        }

        var candidate = Position - GlobalBasis.Z * distanceMeters;
        if (!TryGetSurface(candidate, out var surfaceHeight, out var slopeDegrees))
        {
            return 0.0f;
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
            return 0.0f;
        }

        var elevationGain = surfaceHeight - FeetElevation;
        var uphillAngle = elevationGain > 0.0f
            ? Mathf.RadToDeg(Mathf.Atan2(elevationGain, Mathf.Abs(distanceMeters)))
            : 0.0f;
        var speedMultiplier = 1.0f - MaximumUphillSpeedReduction *
            Mathf.Clamp(uphillAngle / MaximumSlopeDegrees, 0.0f, 1.0f);
        var appliedDistance = distanceMeters * speedMultiplier;
        candidate = Position - GlobalBasis.Z * appliedDistance;
        if (!TryGetSurface(candidate, out surfaceHeight, out slopeDegrees) ||
            slopeDegrees > MaximumSlopeDegrees)
        {
            return 0.0f;
        }

        if (SceneryCollision.TryFindBlockingObstacle(
                new System.Numerics.Vector2(Position.X, Position.Z),
                new System.Numerics.Vector2(candidate.X, candidate.Z),
                m_footprintRadius,
                obstacles,
                out var blockingObstacle))
        {
            if (!m_sceneryBlocked)
            {
                GD.Print(
                    $"MechRewired: PlayerMech movement blocked by scenery '{blockingObstacle.Name}' " +
                    $"(footprint radius {m_footprintRadius:F1}m).");
            }

            m_sceneryBlocked = true;
            m_lastBlockingObstacle = blockingObstacle;
            return 0.0f;
        }

        m_slopeBlocked = false;
        m_sceneryBlocked = false;
        m_lastBlockingObstacle = null;
        candidate.Y = surfaceHeight - m_modelBottomY;
        Position = candidate;
        return appliedDistance;
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
        if (m_mechRig.Advance(distanceMeters, headingChangeRadians, speedFraction, delta))
        {
            PlayFootfall();
        }

        var gaitPhase = m_mechRig.Phase;
        var gaitWeight = m_mechRig.Weight;
        var landingPulse = Mathf.Pow(Mathf.Max(0.0f, Mathf.Cos(gaitPhase * 2.0f)), 10.0f);
        var vertical = (Mathf.Sin(gaitPhase * 2.0f) * 0.015f - landingPulse * 0.07f) * gaitWeight;
        var lateral = Mathf.Sin(gaitPhase) * 0.025f * gaitWeight;
        var roll = -Mathf.Sin(gaitPhase) * 0.012f * gaitWeight;
        var viewOffset = new Vector3(lateral, vertical, 0.0f);
        ViewBobMount.Position = viewOffset;
        ViewBobMount.Rotation = new Vector3(0.0f, 0.0f, roll);

        // Express the cockpit's motion relative to the moving camera as smooth hydraulic travel.
        // Adding the camera transform first cancels its sharp landing pulse from the visible frame.
        var cockpitRelativeOffset = new Vector3(
            -Mathf.Sin(gaitPhase) * CockpitRelativeLateralGait,
            -Mathf.Sin(gaitPhase * 2.0f) * CockpitRelativeVerticalGait,
            0.0f) * gaitWeight;
        var cockpitRelativeRoll = Mathf.Sin(gaitPhase) * CockpitRelativeRollGait * gaitWeight;
        Cockpit.SetPose(
            CockpitPitchDegrees,
            m_torsoYaw * CockpitTorsoYawFactor,
            viewOffset + cockpitRelativeOffset,
            roll + cockpitRelativeRoll);
    }

    private void ApplyDamageShudder(float delta)
    {
        if (m_damageShudderRemaining <= 0.0f)
        {
            return;
        }

        m_damageShudderRemaining = Math.Max(0.0f, m_damageShudderRemaining - delta);
        var elapsed = DamageShudderDuration - m_damageShudderRemaining;
        var envelope = m_damageShudderRemaining / DamageShudderDuration;
        envelope *= envelope;
        var phase = elapsed * DamageShudderFrequency * Mathf.Tau;
        var amplitude = m_damageShudderStrength * envelope;
        ViewBobMount.Position += new Vector3(
            Mathf.Sin(phase * 0.83f) * 0.065f,
            Mathf.Sin(phase * 1.37f) * 0.045f,
            0.0f) * amplitude;
        ViewBobMount.Rotation += new Vector3(
            Mathf.Sin(phase * 1.13f) * 0.008f,
            0.0f,
            Mathf.Sin(phase) * 0.018f) * amplitude;
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
