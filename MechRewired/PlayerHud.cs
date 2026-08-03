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

namespace MechRewired;

/// <summary>
/// Draws the pilot's original-style navigation and movement instruments.
/// </summary>
/// <remarks>
/// This first HUD slice keeps the flight instruments independent of later combat and damage displays.
/// </remarks>
public partial class PlayerHud : Control
{
    private const float ReferenceWidth = 1280.0f;
    private const float ReferenceHeight = 720.0f;
    private const float RadarRadius = 91.0f;
    private const float CompassScale = 0.75f;
    private const float CompassPixelsPerDegree = 3.2f * CompassScale;
    private const float AltimeterPixelsPerMeter = 14.0f;
    private static readonly float[] RadarRanges = [500.0f, 1000.0f, 2000.0f, 4000.0f];
    private static readonly Color HudGreen = Color.FromHtml("00f000");
    private static readonly Color RadarAmber = Color.FromHtml("d7a900");
    private static readonly Color TerrainBlue = Color.FromHtml("1828e8");
    private static readonly Color GaugeRed = Color.FromHtml("e00000");
    private static readonly Color GaugeSideShade = new(0.08f, 0.0f, 0.0f, 0.5f);
    private static readonly Color TargetFrame = Color.FromHtml("4b0a00");

    private readonly PlayerMech m_playerMech;
    private readonly PlayerNavigation m_navigation;
    private int m_radarRangeIndex = 1;
    private float m_scale = 1.0f;
    private Vector2 m_offset;

    public PlayerHud(
        PlayerMech playerMech,
        PlayerNavigation navigation)
    {
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(navigation);

        m_playerMech = playerMech;
        m_navigation = navigation;
    }

    public override void _Ready()
    {
        GD.Print(
            $"MechRewired: pilot HUD online (radar {RadarRanges[m_radarRangeIndex] / 1000.0f:F1}km; " +
            $"NAV '{SelectedNavigationPoint.Description}'; X zooms in, Shift+X zooms out; " +
            $"N/Shift+N cycles NAV points).");
    }

    public override void _Process(double delta)
    {
        var shouldBeVisible = m_playerMech.CockpitCamera?.Current == true;
        if (Visible != shouldBeVisible)
        {
            Visible = shouldBeVisible;
        }

        if (Visible)
        {
            QueueRedraw();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (!Visible || inputEvent is not InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            return;
        }

        if (keyEvent.Keycode == Key.X)
        {
            var adjustment = keyEvent.ShiftPressed ? 1 : -1;
            m_radarRangeIndex = Math.Clamp(
                m_radarRangeIndex + adjustment,
                0,
                RadarRanges.Length - 1);
            GD.Print(
                $"MechRewired: radar range {RadarRanges[m_radarRangeIndex] / 1000.0f:F1}km.");
            GetViewport().SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode != Key.N)
        {
            return;
        }

        if (keyEvent.ShiftPressed)
        {
            m_navigation.SelectPrevious();
        }
        else
        {
            m_navigation.SelectNext();
        }

        GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        m_scale = Math.Min(Size.X / ReferenceWidth, Size.Y / ReferenceHeight);
        m_offset = new Vector2(
            (Size.X - ReferenceWidth * m_scale) * 0.5f,
            (Size.Y - ReferenceHeight * m_scale) * 0.5f);

        DrawRadar();
        DrawCompass();
        DrawAltimeter();
        DrawNavigationTarget();
        DrawSpeed();
    }

    private MechWarriorWorldNavPoint SelectedNavigationPoint =>
        m_navigation.SelectedPoint;

    private void DrawRadar()
    {
        var center = Point(155.0f, 117.0f);
        var radius = RadarRadius * m_scale;
        var playerPosition = center;
        DrawArc(center, radius, 0.0f, Mathf.Tau, 64, RadarAmber, LineWidth(2.0f), false);
        DrawText(new Vector2(24.0f, 42.0f), $"R: {RadarRanges[m_radarRangeIndex] / 1000.0f:F1}Km", HudGreen, 24);

        DrawLine(
            playerPosition + new Vector2(-7.0f, -7.0f) * m_scale,
            playerPosition + new Vector2(-7.0f, 3.0f) * m_scale,
            HudGreen,
            LineWidth(3.0f));
        DrawLine(
            playerPosition + new Vector2(-7.0f, 3.0f) * m_scale,
            playerPosition + new Vector2(7.0f, 3.0f) * m_scale,
            HudGreen,
            LineWidth(3.0f));
        DrawLine(
            playerPosition + new Vector2(7.0f, 3.0f) * m_scale,
            playerPosition + new Vector2(7.0f, -7.0f) * m_scale,
            HudGreen,
            LineWidth(3.0f));

        DrawTorsoViewWedge(playerPosition);
        for (var index = 0; index < m_navigation.NavigationPoints.Count; index++)
        {
            DrawNavigationPoint(center, playerPosition, index);
        }
    }

    private void DrawTorsoViewWedge(Vector2 playerPosition)
    {
        const float halfViewAngle = 38.0f;
        var torsoDegrees = Mathf.RadToDeg(m_playerMech.TorsoYawRadians);
        foreach (var side in new[] { -halfViewAngle, halfViewAngle })
        {
            var angle = Mathf.DegToRad(torsoDegrees + side);
            var direction = new Vector2(-Mathf.Sin(angle), -Mathf.Cos(angle));
            var center = Point(155.0f, 117.0f);
            var fromCenter = playerPosition - center;
            var radius = RadarRadius * m_scale;
            var distance = -fromCenter.Dot(direction) + Mathf.Sqrt(
                Mathf.Pow(fromCenter.Dot(direction), 2.0f) - fromCenter.LengthSquared() + radius * radius);
            DrawLine(
                playerPosition,
                playerPosition + direction * distance,
                RadarAmber,
                LineWidth(2.0f));
        }
    }

    private void DrawNavigationPoint(Vector2 center, Vector2 playerPosition, int navigationIndex)
    {
        var navigation = m_navigation.NavigationPoints[navigationIndex];
        var worldPosition = MechWarriorCoordinateSystem.ToGodotPosition(navigation.Position);
        var localPosition = m_playerMech.ToLocal(worldPosition);
        var radarRange = RadarRanges[m_radarRangeIndex];
        var point = playerPosition + new Vector2(
            localPosition.X / radarRange * RadarRadius,
            localPosition.Z / radarRange * RadarRadius) * m_scale;
        var fromCenter = point - center;
        var maximumRadius = (RadarRadius - 5.0f) * m_scale;
        if (fromCenter.Length() > maximumRadius)
        {
            point = center + fromCenter.Normalized() * maximumRadius;
        }

        var markerRadius = (navigationIndex == m_navigation.SelectedIndex ? 5.0f : 3.0f) * m_scale;
        Vector2[] points =
        {
            point + Vector2.Up * markerRadius,
            point + Vector2.Right * markerRadius,
            point + Vector2.Down * markerRadius,
            point + Vector2.Left * markerRadius
        };
        if (navigationIndex == m_navigation.SelectedIndex)
        {
            DrawColoredPolygon(points, RadarAmber);
        }
        else
        {
            var markerColor = m_navigation.IsReached(navigationIndex) ? HudGreen : RadarAmber;
            DrawPolyline([.. points, points[0]], markerColor, LineWidth(1.0f));
        }
    }

    private void DrawCompass()
    {
        var centerX = 640.0f;
        var top = 47.0f;
        var heading = NormalizeDegrees(MechWarriorCoordinateSystem.ToSourceRotation(
            m_playerMech.GlobalRotationDegrees).Y);
        var firstBearing = (int)MathF.Floor((heading - 60.0f) / 5.0f) * 5;
        var lastBearing = (int)MathF.Ceiling((heading + 60.0f) / 5.0f) * 5;
        for (var unwrappedBearing = firstBearing; unwrappedBearing <= lastBearing; unwrappedBearing += 5)
        {
            var bearing = NormalizeDegrees(unwrappedBearing);
            var x = centerX - (unwrappedBearing - heading) * CompassPixelsPerDegree;
            var isMajor = ((int)bearing) % 30 == 0;
            var tickHeight = (isMajor ? 22.0f : 13.0f) * CompassScale;
            DrawLine(
                Point(x, top),
                Point(x, top + tickHeight),
                HudGreen,
                LineWidth(3.0f * CompassScale));
            if (isMajor)
            {
                var label = ((int)bearing / 10).ToString("00");
                DrawCenteredText(x, top + 35.0f, label, HudGreen, 18);
            }
        }

        DrawActiveNavigationBearing(centerX, top, heading);
        var torsoOffset = Mathf.RadToDeg(m_playerMech.TorsoYawRadians) * CompassPixelsPerDegree;
        DrawLine(
            Point(centerX - torsoOffset - 3.75f, top - 4.5f),
            Point(centerX - torsoOffset + 3.75f, top - 4.5f),
            HudGreen,
            LineWidth(3.0f * CompassScale));
    }

    private void DrawActiveNavigationBearing(float centerX, float top, float heading)
    {
        var navigationPosition = MechWarriorCoordinateSystem.ToGodotPosition(
            m_navigation.SelectedPoint.Position);
        var direction = navigationPosition - m_playerMech.GlobalPosition;
        var bearing = NormalizeDegrees(Mathf.RadToDeg(Mathf.Atan2(direction.X, -direction.Z)));
        var bearingOffset = Mathf.RadToDeg(Mathf.AngleDifference(
            Mathf.DegToRad(heading),
            Mathf.DegToRad(bearing)));
        var markerOffset = Mathf.Clamp(bearingOffset, -60.0f, 60.0f) * CompassPixelsPerDegree;
        var markerCenter = Point(centerX - markerOffset, top - 12.0f);
        var markerRadius = 4.5f * m_scale;
        Vector2[] marker =
        {
            markerCenter + Vector2.Up * markerRadius,
            markerCenter + Vector2.Right * markerRadius,
            markerCenter + Vector2.Down * markerRadius,
            markerCenter + Vector2.Left * markerRadius
        };
        DrawColoredPolygon(marker, RadarAmber);
    }

    private void DrawAltimeter()
    {
        const float x = 66.0f;
        const float centerY = 367.0f;
        var elevation = m_playerMech.FeetElevation;
        var firstMeter = (int)MathF.Floor(elevation - 7.0f);
        var lastMeter = (int)MathF.Ceiling(elevation + 7.0f);
        for (var meter = firstMeter; meter <= lastMeter; meter++)
        {
            var y = centerY - (meter - elevation) * AltimeterPixelsPerMeter;
            var major = meter % 5 == 0;
            DrawLine(
                Point(x, y),
                Point(x + (major ? 19.0f : 11.0f), y),
                HudGreen,
                LineWidth(2.0f));
            if (major)
            {
                DrawText(new Vector2(32.0f, y + 7.0f), meter.ToString(), HudGreen, 22);
            }
        }

        DrawLine(Point(x + 18.0f, centerY), Point(x + 34.0f, centerY), HudGreen, LineWidth(3.0f));
        DrawLine(Point(x + 26.0f, centerY), Point(x + 38.0f, centerY), TerrainBlue, LineWidth(3.0f));
    }

    private void DrawNavigationTarget()
    {
        const float panelLeft = 40.0f;
        const float panelTop = 518.0f;
        const float panelWidth = 215.0f;
        const float panelHeight = 125.0f;
        var panel = new Rect2(
            Point(panelLeft, panelTop),
            new Vector2(panelWidth, panelHeight) * m_scale);
        DrawRect(panel, Colors.Black);
        DrawRect(panel, TargetFrame, false, LineWidth(4.0f));

        var iconCenter = Point(panelLeft + panelWidth * 0.5f, panelTop + panelHeight * 0.5f);
        DrawDiamond(iconCenter, 17.0f, 3.0f);
        DrawDiamond(iconCenter, 8.0f, 2.0f);

        var navigation = SelectedNavigationPoint;
        var distanceMeters = m_navigation.DistanceToSelectedMeters;
        var distanceText = distanceMeters >= 1000.0f
            ? $"{distanceMeters / 1000.0f:F2}Km"
            : $"{distanceMeters:F0}m";
        DrawText(new Vector2(panelLeft, 675.0f), navigation.Description, RadarAmber, 25);
        DrawText(new Vector2(panelLeft, 706.0f), distanceText, HudGreen, 25);
    }

    private void DrawDiamond(Vector2 center, float radius, float width)
    {
        radius *= m_scale;
        Vector2[] points =
        {
            center + Vector2.Up * radius,
            center + Vector2.Right * radius,
            center + Vector2.Down * radius,
            center + Vector2.Left * radius,
            center + Vector2.Up * radius
        };
        DrawPolyline(points, RadarAmber, LineWidth(width));
    }

    private void DrawSpeed()
    {
        const float gaugeLeft = 1248.0f;
        const float gaugeWidth = 16.0f;
        const float positiveTop = 510.0f;
        const float zeroY = 640.0f;
        const float negativeBottom = 705.0f;
        var positiveOutline = new Rect2(
            Point(gaugeLeft, positiveTop),
            new Vector2(gaugeWidth, zeroY - positiveTop) * m_scale);
        var negativeOutline = new Rect2(
            Point(gaugeLeft, zeroY),
            new Vector2(gaugeWidth, negativeBottom - zeroY) * m_scale);
        DrawRect(positiveOutline, GaugeRed, false, LineWidth(2.0f));
        DrawRect(negativeOutline, GaugeRed, false, LineWidth(2.0f));

        const float inset = 3.0f;
        const float sideShadeWidth = 2.0f;
        var speed = m_playerMech.Drive.TargetSpeedKph;
        if (speed > 0.001)
        {
            var fraction = (float)Math.Clamp(
                speed / m_playerMech.Drive.Profile.MaximumForwardSpeedKph,
                0.0,
                1.0);
            var fillHeight = (zeroY - positiveTop - inset * 2.0f) * fraction;
            DrawRect(
                new Rect2(
                    Point(gaugeLeft + inset, zeroY - inset - fillHeight),
                    new Vector2(gaugeWidth - inset * 2.0f, fillHeight) * m_scale),
                HudGreen);
        }
        else if (speed < -0.001)
        {
            var maximumReverseSpeed = m_playerMech.Drive.Profile.MaximumForwardSpeedKph *
                                      m_playerMech.Drive.Profile.ReverseSpeedFactor;
            var fraction = (float)Math.Clamp(-speed / maximumReverseSpeed, 0.0, 1.0);
            var fillHeight = (negativeBottom - zeroY - inset * 2.0f) * fraction;
            DrawRect(
                new Rect2(
                    Point(gaugeLeft + inset, zeroY + inset),
                    new Vector2(gaugeWidth - inset * 2.0f, fillHeight) * m_scale),
                TerrainBlue);
        }
        else
        {
            DrawLine(
                Point(gaugeLeft + inset, zeroY),
                Point(gaugeLeft + gaugeWidth - inset, zeroY),
                HudGreen,
                LineWidth(3.0f));
        }

        DrawRect(
            new Rect2(
                Point(gaugeLeft + inset, positiveTop + inset),
                new Vector2(sideShadeWidth, negativeBottom - positiveTop - inset * 2.0f) * m_scale),
            GaugeSideShade);
        DrawRect(
            new Rect2(
                Point(gaugeLeft + gaugeWidth - inset - sideShadeWidth, positiveTop + inset),
                new Vector2(sideShadeWidth, negativeBottom - positiveTop - inset * 2.0f) * m_scale),
            GaugeSideShade);

        DrawText(
            new Vector2(1110.0f, 687.0f),
            $"{m_playerMech.ActualSpeedKph:F0} kph",
            HudGreen,
            25);
    }

    private Vector2 Point(float x, float y) => m_offset + new Vector2(x, y) * m_scale;

    private float LineWidth(float width) => Math.Max(width * m_scale, 1.0f);

    private void DrawText(Vector2 position, string text, Color color, int fontSize)
    {
        DrawString(
            ThemeDB.FallbackFont,
            Point(position.X, position.Y),
            text,
            HorizontalAlignment.Left,
            -1.0f,
            Math.Max((int)(fontSize * m_scale), 1),
            color);
    }

    private void DrawCenteredText(float centerX, float baselineY, string text, Color color, int fontSize)
    {
        var scaledFontSize = Math.Max((int)(fontSize * m_scale), 1);
        var width = ThemeDB.FallbackFont.GetStringSize(
            text,
            HorizontalAlignment.Left,
            -1.0f,
            scaledFontSize).X;
        DrawString(
            ThemeDB.FallbackFont,
            Point(centerX, baselineY) - new Vector2(width * 0.5f, 0.0f),
            text,
            HorizontalAlignment.Left,
            -1.0f,
            scaledFontSize,
            color);
    }

    private static float NormalizeDegrees(float degrees) => (degrees % 360.0f + 360.0f) % 360.0f;
}
