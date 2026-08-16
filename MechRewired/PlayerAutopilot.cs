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

namespace MechRewired;

/// <summary>
/// Provides a cautious, local-route autopilot to the currently selected mission NAV point.
/// </summary>
/// <remarks>
/// This is intentionally a pilot aid rather than combat AI: it follows the existing NAV sequence,
/// probes nearby terrain/scenery for a viable course, and immediately yields control after a hit,
/// obstacle, or any player input.
/// </remarks>
public partial class PlayerAutopilot : Node
{
    private const int CruiseThrottleKey = 9;
    private const int TurningThrottleKey = 3;
    private const int SharpTurningThrottleKey = 1;
    private const float FullSteeringAngleRadians = Mathf.Pi / 5.0f;
    private const float RouteProbeIntervalSeconds = 0.20f;
    private const float RouteUnavailableTimeoutSeconds = 0.70f;

    private readonly PlayerMech m_playerMech;
    private readonly PlayerNavigation m_navigation;
    private readonly AudioStreamPlayer m_enabledSound;
    private readonly AudioStreamPlayer m_disabledSound;
    private readonly AudioStreamPlayer m_autopilotSound;
    private AudioStreamPlayer m_queuedStatusSound;
    private float m_routeProbeCooldown;
    private float m_routeUnavailableSeconds;
    private float m_guidedHeading;
    private int m_announcedNavigationIndex = -1;

    public PlayerAutopilot(
        PlayerMech playerMech,
        PlayerNavigation navigation,
        AudioStreamWav autopilotSound,
        AudioStreamWav enabledSound,
        AudioStreamWav disabledSound)
    {
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(autopilotSound);
        ArgumentNullException.ThrowIfNull(enabledSound);
        ArgumentNullException.ThrowIfNull(disabledSound);

        Name = "PlayerAutopilot";
        m_playerMech = playerMech;
        m_navigation = navigation;
        m_autopilotSound = new AudioStreamPlayer
        {
            Name = "Autopilot",
            Stream = autopilotSound
        };
        m_enabledSound = new AudioStreamPlayer
        {
            Name = "AutopilotEnabled",
            Stream = enabledSound
        };
        m_disabledSound = new AudioStreamPlayer
        {
            Name = "AutopilotDisabled",
            Stream = disabledSound
        };
        AddChild(m_autopilotSound);
        AddChild(m_enabledSound);
        AddChild(m_disabledSound);
        m_autopilotSound.Finished += PlayQueuedStatusSound;
        playerMech.AutopilotToggleRequested += Toggle;
        playerMech.ManualControlRequested += reason => Deactivate($"manual {reason}");
        playerMech.DamageReceived += _ => Deactivate("incoming damage");
        playerMech.MovementBlocked += reason => Deactivate($"blocked by {reason}");
        playerMech.Destroyed += () => Deactivate("mech destruction");
        navigation.NavigationPointReached += OnNavigationPointReached;
    }

    public bool IsActive { get; private set; }

    public void Toggle()
    {
        if (IsActive)
        {
            Deactivate("pilot command");
            return;
        }

        if (m_playerMech.IsDestroyed ||
            m_playerMech.IsShutdown ||
            m_playerMech.IsImmobilized ||
            m_playerMech.IsTranslationLocked)
        {
            GD.Print("MechRewired: autopilot unavailable; PlayerMech cannot currently travel.");
            return;
        }

        IsActive = true;
        m_routeProbeCooldown = 0.0f;
        m_routeUnavailableSeconds = 0.0f;
        m_announcedNavigationIndex = -1;
        QueueStatusSound(m_enabledSound);
        GD.Print($"MechRewired: autopilot engaged for NAV '{m_navigation.SelectedPoint.Description}'.");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsActive)
        {
            return;
        }

        if (m_playerMech.IsDestroyed ||
            m_playerMech.IsShutdown ||
            m_playerMech.IsImmobilized ||
            m_playerMech.IsTranslationLocked)
        {
            Deactivate("movement is no longer available");
            return;
        }

        var destination = MechWarriorCoordinateSystem.ToGodotPosition(m_navigation.SelectedPoint.Position);
        var offset = destination - m_playerMech.GlobalPosition;
        offset.Y = 0.0f;
        var distance = offset.Length();
        if (distance <= 0.01f)
        {
            m_playerMech.SetAutopilotControl(0.0f, TurningThrottleKey);
            return;
        }

        if (m_announcedNavigationIndex != m_navigation.SelectedIndex)
        {
            m_announcedNavigationIndex = m_navigation.SelectedIndex;
            GD.Print($"MechRewired: autopilot routing to NAV '{m_navigation.SelectedPoint.Description}'.");
        }

        var desiredHeading = Mathf.Atan2(-offset.X, -offset.Z);
        m_routeProbeCooldown -= (float)delta;
        if (m_routeProbeCooldown <= 0.0f)
        {
            m_routeProbeCooldown = RouteProbeIntervalSeconds;
            if (!m_playerMech.TryFindAutopilotHeading(desiredHeading, distance, out m_guidedHeading))
            {
                m_routeUnavailableSeconds += RouteProbeIntervalSeconds;
                if (m_routeUnavailableSeconds >= RouteUnavailableTimeoutSeconds)
                {
                    Deactivate("no traversable route to the selected NAV point");
                }

                return;
            }

            m_routeUnavailableSeconds = 0.0f;
        }

        var headingError = Mathf.AngleDifference(m_playerMech.Rotation.Y, m_guidedHeading);
        var steering = Mathf.Clamp(headingError / FullSteeringAngleRadians, -1.0f, 1.0f);
        var headingErrorMagnitude = Mathf.Abs(headingError);
        var throttleKey = headingErrorMagnitude > Mathf.DegToRad(60.0f)
            ? SharpTurningThrottleKey
            : headingErrorMagnitude > Mathf.DegToRad(25.0f) ||
              distance < m_navigation.SelectedPoint.Radius * 1.5f
                ? TurningThrottleKey
                : CruiseThrottleKey;
        m_playerMech.SetAutopilotControl(steering, throttleKey);
    }

    private void Deactivate(string reason)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        m_playerMech.ClearAutopilotControl();
        QueueStatusSound(m_disabledSound);
        GD.Print($"MechRewired: autopilot disengaged ({reason}).");
    }

    private void OnNavigationPointReached(int index)
    {
        if (IsActive && index == m_navigation.SelectedIndex)
        {
            Deactivate($"reached NAV '{m_navigation.NavigationPoints[index].Description}'");
        }
    }

    private void QueueStatusSound(AudioStreamPlayer statusSound)
    {
        m_enabledSound.Stop();
        m_disabledSound.Stop();
        m_queuedStatusSound = statusSound;
        m_autopilotSound.Stop();
        m_autopilotSound.Play();
    }

    private void PlayQueuedStatusSound()
    {
        m_queuedStatusSound?.Play();
        m_queuedStatusSound = null;
    }
}
