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
/// Provides a cautious, pre-planned-route autopilot to the currently selected mission NAV point.
/// </summary>
/// <remarks>
/// This is intentionally a pilot aid rather than combat AI: it plans and validates a smooth local
/// Bézier course when engaged, then immediately yields control after a hit, obstacle, or any player input.
/// </remarks>
public partial class PlayerAutopilot : Node
{
    private const int CruiseThrottleKey = 9;
    private const int TurningThrottleKey = 3;
    private const int SharpTurningThrottleKey = 1;
    private const float FullSteeringAngleRadians = Mathf.Pi / 5.0f;
    private const float CourseSampleSpacingMeters = 8.0f;
    private const float CourseLookAheadMeters = 22.0f;
    private const float SteeringResponsePerSecond = 2.5f;
    private const float PlannedRouteSegmentMaximumMeters = 60.0f;
    private const int MaximumPlannedRouteSegments = 256;

    private readonly PlayerMech m_playerMech;
    private readonly PlayerNavigation m_navigation;
    private readonly AudioStreamPlayer m_enabledSound;
    private readonly AudioStreamPlayer m_disabledSound;
    private readonly AudioStreamPlayer m_autopilotSound;
    private AudioStreamPlayer m_queuedStatusSound;
    private readonly List<Vector3> m_coursePoints = new();
    private int m_coursePointIndex;
    private int m_courseNavigationIndex = -1;
    private float m_smoothedSteering;
    private bool m_isLocalFallbackCourse;
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

        if (!TryBuildCourse())
        {
            GD.Print("MechRewired: autopilot unavailable; no traversable smooth course to the selected NAV point.");
            return;
        }

        IsActive = true;
        m_announcedNavigationIndex = -1;
        m_smoothedSteering = 0.0f;
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

        if (m_courseNavigationIndex != m_navigation.SelectedIndex && !TryBuildCourse())
        {
            Deactivate("no traversable smooth course to the selected NAV point");
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

        if (m_coursePointIndex == m_coursePoints.Count - 1 &&
            PlanarDistance(m_playerMech.GlobalPosition, m_coursePoints[^1]) <= CourseSampleSpacingMeters &&
            distance > m_navigation.SelectedPoint.Radius)
        {
            if (!TryBuildCourse())
            {
                Deactivate("no traversable course continuation to the selected NAV point");
                return;
            }
        }

        if (m_announcedNavigationIndex != m_navigation.SelectedIndex)
        {
            m_announcedNavigationIndex = m_navigation.SelectedIndex;
            GD.Print($"MechRewired: autopilot routing to NAV '{m_navigation.SelectedPoint.Description}'.");
        }

        var guidancePoint = FindCourseLookAheadPoint();
        if (!m_isLocalFallbackCourse &&
            !m_playerMech.IsAutopilotCourseTraversable(m_playerMech.GlobalPosition, guidancePoint))
        {
            if (!TryBuildLocalFallbackCourse())
            {
                Deactivate("planned course is no longer traversable");
            }

            return;
        }

        var guidanceOffset = guidancePoint - m_playerMech.GlobalPosition;
        guidanceOffset.Y = 0.0f;
        var guidanceHeading = Mathf.Atan2(-guidanceOffset.X, -guidanceOffset.Z);
        var headingError = Mathf.AngleDifference(m_playerMech.Rotation.Y, guidanceHeading);
        var targetSteering = Mathf.Clamp(headingError / FullSteeringAngleRadians, -1.0f, 1.0f);
        m_smoothedSteering = Mathf.MoveToward(
            m_smoothedSteering,
            targetSteering,
            SteeringResponsePerSecond * (float)delta);
        var headingErrorMagnitude = Mathf.Abs(headingError);
        var throttleKey = headingErrorMagnitude > Mathf.DegToRad(60.0f)
            ? SharpTurningThrottleKey
            : headingErrorMagnitude > Mathf.DegToRad(25.0f) ||
              distance < m_navigation.SelectedPoint.Radius * 1.5f
                ? TurningThrottleKey
                : CruiseThrottleKey;
        m_playerMech.SetAutopilotControl(m_smoothedSteering, throttleKey);
    }

    private bool TryBuildCourse()
    {
        // A full plan is preferable, but mountain terrain can have locally viable routes that a
        // simple forward planner cannot prove all the way to the NAV point. Keep the established
        // local course selector as a graceful fallback instead of refusing to engage autopilot.
        return TryBuildStrictCourse() || TryBuildLocalFallbackCourse();
    }

    private bool TryBuildStrictCourse()
    {
        var start = m_playerMech.GlobalPosition;
        var destination = MechWarriorCoordinateSystem.ToGodotPosition(m_navigation.SelectedPoint.Position);
        var offset = destination - start;
        offset.Y = 0.0f;
        var distance = offset.Length();
        if (distance <= 0.01f)
        {
            return false;
        }

        var routePoints = new List<Vector3> { start };
        var routeHeadings = new List<float>();
        while (routePoints.Count <= MaximumPlannedRouteSegments)
        {
            var routeStart = routePoints[^1];
            var routeOffset = destination - routeStart;
            routeOffset.Y = 0.0f;
            var remainingDistance = routeOffset.Length();
            var desiredHeading = Mathf.Atan2(-routeOffset.X, -routeOffset.Z);
            if (remainingDistance <= PlannedRouteSegmentMaximumMeters &&
                m_playerMech.IsAutopilotCourseTraversable(routeStart, destination))
            {
                routePoints.Add(destination);
                routeHeadings.Add(desiredHeading);
                break;
            }

            var segmentDistance = Mathf.Min(remainingDistance, PlannedRouteSegmentMaximumMeters);
            if (!m_playerMech.TryFindAutopilotHeading(
                    routeStart,
                    desiredHeading,
                    segmentDistance,
                    out var departureHeading))
            {
                return false;
            }

            var nextPoint = routeStart + HeadingVector(departureHeading) * segmentDistance;
            var nextDistance = PlanarDistance(nextPoint, destination);
            if (nextDistance >= remainingDistance - 0.01f)
            {
                return false;
            }

            routePoints.Add(nextPoint);
            routeHeadings.Add(departureHeading);
        }

        if (routePoints[^1] != destination)
        {
            return false;
        }

        var coursePoints = new List<Vector3> { start };
        for (var segmentIndex = 0; segmentIndex < routeHeadings.Count; segmentIndex++)
        {
            var segmentStart = routePoints[segmentIndex];
            var segmentEnd = routePoints[segmentIndex + 1];
            var segmentDistance = PlanarDistance(segmentStart, segmentEnd);
            var startHeading = routeHeadings[segmentIndex];
            var endHeading = segmentIndex < routeHeadings.Count - 1
                ? routeHeadings[segmentIndex + 1]
                : startHeading;
            var controlDistance = Mathf.Min(segmentDistance * 0.20f, 15.0f);
            var curve = new CubicBezierCurve(
                ToPlanarPosition(segmentStart),
                ToPlanarPosition(segmentStart + HeadingVector(startHeading) * controlDistance),
                ToPlanarPosition(segmentEnd - HeadingVector(endHeading) * controlDistance),
                ToPlanarPosition(segmentEnd));
            var sampleCount = Mathf.Max(2, Mathf.CeilToInt(segmentDistance / CourseSampleSpacingMeters));
            for (var sample = 1; sample <= sampleCount; sample++)
            {
                var planarPoint = curve.Evaluate((float)sample / sampleCount);
                var point = new Vector3(planarPoint.X, start.Y, planarPoint.Y);
                if (!m_playerMech.IsAutopilotCourseTraversable(coursePoints[^1], point))
                {
                    return false;
                }

                coursePoints.Add(point);
            }
        }

        m_coursePoints.Clear();
        m_coursePoints.AddRange(coursePoints);
        m_coursePointIndex = 0;
        m_courseNavigationIndex = m_navigation.SelectedIndex;
        m_isLocalFallbackCourse = false;
        return true;
    }

    private bool TryBuildLocalFallbackCourse()
    {
        var start = m_playerMech.GlobalPosition;
        var destination = MechWarriorCoordinateSystem.ToGodotPosition(m_navigation.SelectedPoint.Position);
        var offset = destination - start;
        offset.Y = 0.0f;
        var distance = offset.Length();
        if (distance <= 0.01f ||
            !m_playerMech.TryFindAutopilotHeading(
                Mathf.Atan2(-offset.X, -offset.Z),
                distance,
                out var heading))
        {
            return false;
        }

        // This is deliberately the same local clearance test used by the original autopilot.
        // It advances a stable 60m course before planning the next section, rather than making a
        // new steering decision five times a second.
        var endpoint = start + HeadingVector(heading) * Mathf.Min(distance, PlannedRouteSegmentMaximumMeters);
        m_coursePoints.Clear();
        m_coursePoints.Add(start);
        var sampleCount = Mathf.Max(2, Mathf.CeilToInt(PlanarDistance(start, endpoint) / CourseSampleSpacingMeters));
        for (var sample = 1; sample <= sampleCount; sample++)
        {
            m_coursePoints.Add(start.Lerp(endpoint, (float)sample / sampleCount));
        }

        m_coursePointIndex = 0;
        m_courseNavigationIndex = m_navigation.SelectedIndex;
        m_isLocalFallbackCourse = true;
        return true;
    }

    private Vector3 FindCourseLookAheadPoint()
    {
        var currentPosition = m_playerMech.GlobalPosition;
        var furthestPoint = Mathf.Min(m_coursePointIndex + 16, m_coursePoints.Count - 1);
        for (var pointIndex = m_coursePointIndex + 1; pointIndex <= furthestPoint; pointIndex++)
        {
            if (PlanarDistance(currentPosition, m_coursePoints[pointIndex]) <=
                PlanarDistance(currentPosition, m_coursePoints[m_coursePointIndex]))
            {
                m_coursePointIndex = pointIndex;
            }
        }

        var remainingDistance = CourseLookAheadMeters;
        var coursePoint = m_coursePoints[m_coursePointIndex];
        for (var pointIndex = m_coursePointIndex + 1; pointIndex < m_coursePoints.Count; pointIndex++)
        {
            var nextCoursePoint = m_coursePoints[pointIndex];
            var segmentDistance = PlanarDistance(coursePoint, nextCoursePoint);
            if (segmentDistance >= remainingDistance)
            {
                return coursePoint.Lerp(nextCoursePoint, remainingDistance / segmentDistance);
            }

            remainingDistance -= segmentDistance;
            coursePoint = nextCoursePoint;
        }

        return m_coursePoints[^1];
    }

    private static System.Numerics.Vector2 ToPlanarPosition(Vector3 position) => new(position.X, position.Z);

    private static Vector3 HeadingVector(float heading) => new(-Mathf.Sin(heading), 0.0f, -Mathf.Cos(heading));

    private static float PlanarDistance(Vector3 first, Vector3 second) =>
        new Vector2(first.X - second.X, first.Z - second.Z).Length();

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
