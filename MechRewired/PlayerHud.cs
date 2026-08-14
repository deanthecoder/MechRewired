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
using MechRewired.Missions;
using MechRewired.Resources;
using MechRewired.Simulation;

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
    private const float RadarPowerTransitionSeconds = 0.35f;
    private const float CompassScale = 0.75f;
    private const float CompassPixelsPerDegree = 3.2f * CompassScale;
    private const float AltimeterPixelsPerMeter = 14.0f;
    private const float MaximumTargetFrameSize = 160.0f;
    private const float ObjectiveTargetFrameSize = 48.0f;
    private const float PlayerDamageRight = 1225.0f;
    private const float PlayerDamageSize = 130.5f;
    private const float PlayerDamageCenterX = PlayerDamageRight - PlayerDamageSize * 0.5f;
    private static readonly float[] RadarRanges = [500.0f, 1000.0f, 2000.0f, 4000.0f];
    private static readonly Color HudGreen = Color.FromHtml("00f000");
    private static readonly Color RadarAmber = Color.FromHtml("d7a900");
    private static readonly Color ReachedNavigationAmber = Color.FromHtml("796000");
    private static readonly Color TerrainBlue = Color.FromHtml("1828e8");
    private static readonly Color GaugeRed = Color.FromHtml("e00000");
    private static readonly Color GaugeBlueShade = Color.FromHtml("101a9e");
    private static readonly Color GaugeBlueInnerShade = Color.FromHtml("1522bc");
    private static readonly Color DestroyedSectionGrey = Color.FromHtml("34383c");
    private static readonly Color GaugeSideShade = new(0.08f, 0.0f, 0.0f, 0.5f);
    private static readonly Color TargetFrame = Color.FromHtml("4b0a00");

    private readonly PlayerMech m_playerMech;
    private readonly MechDamageSilhouette m_playerDamageSilhouette;
    private readonly PlayerNavigation m_navigation;
    private readonly PlayerTargeting m_targeting;
    private readonly PlayerMission m_mission;
    private int m_radarRangeIndex = 1;
    private float m_scale = 1.0f;
    private Vector2 m_offset;
    private float m_radarPower = 1.0f;

    public PlayerHud(
        PlayerMech playerMech,
        MechDamageSilhouette playerDamageSilhouette,
        PlayerNavigation navigation,
        PlayerTargeting targeting,
        PlayerMission mission)
    {
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(playerDamageSilhouette);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(targeting);
        ArgumentNullException.ThrowIfNull(mission);

        m_playerMech = playerMech;
        m_playerDamageSilhouette = playerDamageSilhouette;
        m_navigation = navigation;
        m_targeting = targeting;
        m_mission = mission;
    }

    public override void _Ready()
    {
        GD.Print(
            $"MechRewired: pilot HUD online (radar {RadarRanges[m_radarRangeIndex] / 1000.0f:F1}km; " +
            $"NAV '{SelectedNavigationPoint.Description}'; X/Shift+X adjusts radar range; " +
            $"Z/Shift+Z adjusts display zoom; N/Shift+N cycles NAV points).");
    }

    public override void _Process(double delta)
    {
        var shouldBeVisible = m_playerMech.CockpitCamera?.Current == true;
        if (Visible != shouldBeVisible)
        {
            Visible = shouldBeVisible;
        }

        var radarTarget = m_targeting.IsShutdown ? 0.0f : 1.0f;
        m_radarPower = Mathf.MoveToward(
            m_radarPower,
            radarTarget,
            (float)delta / RadarPowerTransitionSeconds);

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
        if (m_targeting.IsShutdown)
        {
            return;
        }

        DrawCompass();
        DrawWeapons();
        DrawHeat();
        DrawAltimeter();
        DrawNavigationTarget();
        DrawSpeed();
        DrawCombatReticle();
        DrawNavigationDirectionIndicator();
        DrawObjectiveTargets();
        DrawSelectedTarget();
        DrawMissionStatus();
        DrawPlayerDamageStatus();
    }

    private MechWarriorWorldNavPoint SelectedNavigationPoint =>
        m_navigation.SelectedPoint;

    private void DrawRadar()
    {
        var center = Point(155.0f, 117.0f);
        var radius = RadarRadius * m_scale * m_radarPower;
        if (radius <= 0.01f)
        {
            return;
        }

        var playerPosition = center;
        DrawArc(center, radius, 0.0f, Mathf.Tau, 64, RadarAmber, LineWidth(2.0f), false);
        if (m_targeting.IsShutdown || m_radarPower < 0.999f)
        {
            return;
        }

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

        DrawEnemyMechs(center, playerPosition);
    }

    private void DrawEnemyMechs(Vector2 center, Vector2 playerPosition)
    {
        var radarRange = RadarRanges[m_radarRangeIndex];
        foreach (var enemyMech in m_targeting.EnemyMechs.Where(enemyMech =>
                     !enemyMech.IsDestroyed && !enemyMech.IsPoweredDown))
        {
            var localPosition = m_playerMech.ToLocal(enemyMech.TargetPosition);
            var point = playerPosition + new Vector2(
                localPosition.X / radarRange * RadarRadius,
                localPosition.Z / radarRange * RadarRadius) * m_scale;
            var fromCenter = point - center;
            if (fromCenter.Length() > RadarRadius * m_scale)
            {
                continue;
            }

            var markerRadius = ReferenceEquals(enemyMech, m_targeting.SelectedEnemy) ? 5.0f : 3.0f;
            markerRadius *= m_scale;
            Vector2[] marker =
            {
                point + Vector2.Up * markerRadius,
                point + Vector2.Right * markerRadius,
                point + Vector2.Down * markerRadius,
                point + Vector2.Left * markerRadius
            };
            if (ReferenceEquals(enemyMech, m_targeting.SelectedEnemy))
            {
                DrawColoredPolygon(marker, GaugeRed);
            }
            else
            {
                DrawPolyline([.. marker, marker[0]], GaugeRed, LineWidth(1.0f));
            }
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
            m_playerMech.Torso.GlobalRotationDegrees).Y);
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
        DrawLine(
            Point(centerX - 3.75f, top - 4.5f),
            Point(centerX + 3.75f, top - 4.5f),
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

        var enemyMech = m_targeting.SelectedEnemy;
        if (enemyMech != null)
        {
            DrawEnemyTargetPanel(enemyMech, panelLeft, panelTop, panelWidth, panelHeight);
            return;
        }

        var iconCenter = Point(panelLeft + panelWidth * 0.5f, panelTop + panelHeight * 0.5f);
        var navigation = SelectedNavigationPoint;
        var navigationColor = m_navigation.IsReached(m_navigation.SelectedIndex)
            ? ReachedNavigationAmber
            : RadarAmber;
        DrawDiamond(iconCenter, 17.0f, 3.0f, navigationColor);
        DrawDiamond(iconCenter, 8.0f, 2.0f, navigationColor);
        var distanceMeters = m_navigation.DistanceToSelectedMeters;
        var distanceText = distanceMeters >= 1000.0f
            ? $"{distanceMeters / 1000.0f:F2}Km"
            : $"{distanceMeters:F0}m";
        DrawText(new Vector2(panelLeft, 675.0f), navigation.Description, navigationColor, 25);
        DrawText(new Vector2(panelLeft, 706.0f), distanceText, HudGreen, 25);
    }

    private void DrawEnemyTargetPanel(
        EnemyMech enemyMech,
        float panelLeft,
        float panelTop,
        float panelWidth,
        float panelHeight)
    {
        DrawDamageSilhouette(
            enemyMech.DamageSilhouette,
            panelLeft,
            panelTop,
            panelWidth,
            panelHeight,
            enemyMech.Damage,
            10.0f);

        var distanceMeters = enemyMech.TargetPosition.DistanceTo(m_playerMech.GlobalPosition);
        var distanceText = distanceMeters >= 1000.0f
            ? $"{distanceMeters / 1000.0f:F2}Km"
            : $"{distanceMeters:F0}m";
        DrawText(new Vector2(panelLeft, 675.0f), enemyMech.Description, RadarAmber, 25);
        DrawText(new Vector2(panelLeft, 706.0f), distanceText, HudGreen, 25);
    }

    private void DrawDiamond(Vector2 center, float radius, float width, Color? color = null)
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
        DrawPolyline(points, color ?? RadarAmber, LineWidth(width));
    }

    private void DrawNavigationDirectionIndicator()
    {
        if (m_navigation.DistanceToSelectedMeters <= SelectedNavigationPoint.Radius)
        {
            return;
        }

        var camera = m_playerMech.CockpitCamera;
        if (camera == null)
        {
            return;
        }

        var navigationPosition = MechWarriorCoordinateSystem.ToGodotPosition(
            m_navigation.SelectedPoint.Position);
        var center = Size * 0.5f;
        Vector2 screenPosition;
        if (!camera.IsPositionBehind(navigationPosition))
        {
            screenPosition = camera.UnprojectPosition(navigationPosition);
        }
        else
        {
            var localDirection = camera.ToLocal(navigationPosition);
            var edgeDirection = new Vector2(localDirection.X, -localDirection.Y);
            if (edgeDirection.LengthSquared() < 0.0001f)
            {
                edgeDirection = Vector2.Down;
            }

            screenPosition = center + edgeDirection.Normalized() * Size.Length();
        }

        var offset = screenPosition - center;
        var horizontalLimit = Math.Max(Size.X * 0.5f - 42.0f * m_scale, 1.0f);
        var verticalLimit = Math.Max(Size.Y * 0.5f - 42.0f * m_scale, 1.0f);
        var clampScale = Math.Min(
            1.0f,
            Math.Min(
                horizontalLimit / Math.Max(Mathf.Abs(offset.X), 0.001f),
                verticalLimit / Math.Max(Mathf.Abs(offset.Y), 0.001f)));
        var markerCenter = center + offset * clampScale;
        var radius = 6.0f * m_scale;
        Vector2[] diamond =
        {
            markerCenter + Vector2.Up * radius,
            markerCenter + Vector2.Right * radius,
            markerCenter + Vector2.Down * radius,
            markerCenter + Vector2.Left * radius,
            markerCenter + Vector2.Up * radius
        };
        DrawPolyline(diamond, HudGreen, LineWidth(2.0f));
        var fontSize = Math.Max((int)(14 * m_scale), 1);
        const string label = "NAV";
        var labelWidth = ThemeDB.FallbackFont.GetStringSize(
            label,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize).X;
        DrawString(
            ThemeDB.FallbackFont,
            markerCenter + new Vector2(-labelWidth * 0.5f, -11.0f * m_scale),
            label,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize,
            HudGreen);
    }

    private void DrawCombatReticle()
    {
        var center = Size * 0.5f;
        var inner = 7.0f * m_scale;
        var outer = 22.0f * m_scale;
        var width = LineWidth(2.0f);
        var color = m_targeting.MissileLocked ? GaugeRed : HudGreen;
        DrawLine(center + Vector2.Left * outer, center + Vector2.Left * inner, color, width);
        DrawLine(center + Vector2.Right * inner, center + Vector2.Right * outer, color, width);
        DrawLine(center + Vector2.Up * outer, center + Vector2.Up * inner, color, width);
        DrawLine(center + Vector2.Down * inner, center + Vector2.Down * outer, color, width);
        DrawArc(center, 13.0f * m_scale, 0.0f, Mathf.Tau, 24, color, width);
    }

    private void DrawWeapons()
    {
        const float firstColumnX = 925.0f;
        const float columnWidth = 145.0f;
        const float firstBaselineY = 57.0f;
        const float rowHeight = 27.0f;
        var selection = m_targeting.WeaponSelection;
        var columns = PlayerWeaponSelection.BuildColumns(selection.Weapons);
        for (var column = 0; column < columns.Count; column++)
        {
            for (var row = 0; row < columns[column].Count; row++)
            {
                var index = columns[column][row];
                var x = firstColumnX + column * columnWidth;
                var y = firstBaselineY + row * rowHeight;
                var weapon = selection.Weapons[index];
                var operational = m_targeting.IsWeaponOperational(index);
                var groupColor = selection.GetGroup(index) switch
                {
                    0 => HudGreen,
                    1 => Colors.White,
                    2 => RadarAmber,
                    _ => HudGreen
                };
                var weaponColor = !operational
                    ? Colors.Black
                    : !m_targeting.IsWeaponReady(index)
                        ? GaugeRed
                        : groupColor;
                DrawText(
                    new Vector2(x, y),
                    weapon.Specification.HudName,
                    weaponColor,
                    19);
                if (weapon.Specification.Kind == MechWeaponKind.Missile)
                {
                    DrawText(
                        new Vector2(x + 105.0f, y),
                        $"{m_targeting.GetWeaponAmmo(index)}",
                        weaponColor,
                        16);
                }

                if (index == selection.SelectedWeaponIndex)
                {
                    DrawRect(
                        new Rect2(
                            Point(x - 5.0f, y - 19.0f),
                            new Vector2(142.0f, 24.0f) * m_scale),
                        weaponColor,
                        false,
                        LineWidth(2.0f));
                }
            }
        }

    }

    private void DrawHeat()
    {
        const float heatGaugeLeft = 430.0f;
        const float gaugeTop = 646.0f;
        const float gaugeWidth = 190.0f;
        const float gaugeHeight = 15.0f;
        const float rateGaugeLeft = 655.0f;
        const float rateGaugeWidth = 150.0f;
        var heatGauge = new Rect2(
            Point(heatGaugeLeft, gaugeTop),
            new Vector2(gaugeWidth, gaugeHeight) * m_scale);
        DrawBlueGauge(heatGauge);
        DrawThermalFillFromEdges(
            heatGaugeLeft,
            gaugeTop,
            gaugeWidth,
            gaugeHeight,
            (float)Math.Clamp(m_targeting.HeatFraction * 2.0, 0.0, 2.0));
        DrawCenteredText(heatGaugeLeft + gaugeWidth * 0.5f, 691.0f, "Heat", HudGreen, 24);

        var rateGauge = new Rect2(
            Point(rateGaugeLeft, gaugeTop),
            new Vector2(rateGaugeWidth, gaugeHeight) * m_scale);
        DrawBlueGauge(rateGauge);
        DrawThermalFillFromLeft(
            rateGaugeLeft,
            gaugeTop,
            rateGaugeWidth,
            gaugeHeight,
            (float)Math.Clamp(m_targeting.HeatRate / 20.0, 0.0, 2.0));
        DrawCenteredText(rateGaugeLeft + rateGaugeWidth * 0.5f, 691.0f, "dH/dT", HudGreen, 24);
    }

    private void DrawBlueGauge(Rect2 gauge)
    {
        DrawRect(gauge, TerrainBlue);
        DrawGaugeShading(gauge);
    }

    private void DrawGaugeShading(Rect2 gauge)
    {
        var lineWidth = LineWidth(1.0f);
        var top = gauge.Position;
        var firstInnerLine = top + Vector2.Down * lineWidth;
        var lastInnerLine = top + Vector2.Down * (gauge.Size.Y - lineWidth * 2.0f);
        var bottom = top + Vector2.Down * (gauge.Size.Y - lineWidth);
        DrawLine(top, top + Vector2.Right * gauge.Size.X, GaugeBlueShade, lineWidth);
        DrawLine(
            firstInnerLine,
            firstInnerLine + Vector2.Right * gauge.Size.X,
            GaugeBlueInnerShade,
            lineWidth);
        DrawLine(
            lastInnerLine,
            lastInnerLine + Vector2.Right * gauge.Size.X,
            GaugeBlueInnerShade,
            lineWidth);
        DrawLine(
            bottom,
            bottom + Vector2.Right * gauge.Size.X,
            GaugeBlueShade,
            lineWidth);
    }

    private void DrawThermalFillFromEdges(
        float left,
        float top,
        float width,
        float height,
        float thermalProgress)
    {
        var yellowWidth = width * 0.5f * Math.Min(thermalProgress, 1.0f);
        DrawThermalStrip(left, top, yellowWidth, height, RadarAmber);
        DrawThermalStrip(left + width - yellowWidth, top, yellowWidth, height, RadarAmber);
        var redWidth = width * 0.5f * Math.Max(thermalProgress - 1.0f, 0.0f);
        DrawThermalStrip(left, top, redWidth, height, GaugeRed);
        DrawThermalStrip(left + width - redWidth, top, redWidth, height, GaugeRed);
    }

    private void DrawThermalFillFromLeft(
        float left,
        float top,
        float width,
        float height,
        float thermalProgress)
    {
        DrawThermalStrip(left, top, width * Math.Min(thermalProgress, 1.0f), height, RadarAmber);
        DrawThermalStrip(left, top, width * Math.Max(thermalProgress - 1.0f, 0.0f), height, GaugeRed);
    }

    private void DrawThermalStrip(float left, float top, float width, float height, Color color)
    {
        if (width <= 0.0f)
        {
            return;
        }

        DrawRect(
            new Rect2(Point(left, top), new Vector2(width, height) * m_scale),
            color);
        var edgeHeight = Math.Min(1.0f, height * 0.25f);
        DrawRect(
            new Rect2(Point(left, top), new Vector2(width, edgeHeight) * m_scale),
            color.Lightened(0.12f));
        DrawRect(
            new Rect2(
                Point(left, top + height - edgeHeight),
                new Vector2(width, edgeHeight) * m_scale),
            color.Darkened(0.25f));
    }

    private void DrawSelectedTarget()
    {
        var actor = m_targeting.SelectedActor;
        var enemyMech = m_targeting.SelectedEnemy;
        var camera = m_playerMech.CockpitCamera;
        var targetPosition = enemyMech?.TargetPosition ?? actor?.TargetPosition ?? default;
        if ((actor == null && enemyMech == null) ||
            (actor != null && ReferenceEquals(actor, m_targeting.ObjectiveActor)) ||
            camera == null)
        {
            return;
        }

        var bounds = enemyMech?.WorldBounds ?? actor.WorldBounds;
        var targetRect = camera.IsPositionBehind(targetPosition)
            ? GetOffscreenTargetRect(camera, targetPosition)
            : ClampTargetRect(GetScreenRect(camera, bounds).Grow(5.0f * m_scale));
        DrawTargetCorners(targetRect, enemyMech == null ? RadarAmber : GaugeRed);
        if (enemyMech != null)
        {
            return;
        }

        var description = actor.Description;
        var center = targetRect.GetCenter();
        var radius = Math.Max(targetRect.Size.X, targetRect.Size.Y) * 0.5f;
        var fontSize = Math.Max((int)(18 * m_scale), 1);
        var labelWidth = ThemeDB.FallbackFont.GetStringSize(
            description,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize).X;
        DrawString(
            ThemeDB.FallbackFont,
            center + new Vector2(-labelWidth * 0.5f, radius + 22.0f * m_scale),
            description,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize,
            RadarAmber);
    }

    private Rect2 GetOffscreenTargetRect(Camera3D camera, Vector3 targetPosition)
    {
        var localDirection = camera.ToLocal(targetPosition);
        var direction = new Vector2(localDirection.X, -localDirection.Y);
        if (direction.LengthSquared() < 0.0001f)
        {
            direction = Vector2.Down;
        }

        var size = new Vector2(48.0f, 48.0f) * m_scale;
        var center = Size * 0.5f + direction.Normalized() * Size.Length();
        return ClampTargetRect(new Rect2(center - size * 0.5f, size));
    }

    private Rect2 ClampTargetRect(Rect2 rect)
    {
        var margin = 20.0f * m_scale;
        var halfSize = rect.Size * 0.5f;
        var center = rect.GetCenter();
        center.X = Mathf.Clamp(center.X, margin + halfSize.X, Size.X - margin - halfSize.X);
        center.Y = Mathf.Clamp(center.Y, margin + halfSize.Y, Size.Y - margin - halfSize.Y);
        return new Rect2(center - halfSize, rect.Size);
    }

    private void DrawObjectiveTargets()
    {
        var camera = m_playerMech.CockpitCamera;
        if (camera == null)
        {
            return;
        }

        var actor = m_targeting.ObjectiveActor;
        var aimPosition = m_targeting.ObjectiveAimPosition;
        if (actor == null || camera.IsPositionBehind(aimPosition))
        {
            return;
        }

        var frameSize = new Vector2(ObjectiveTargetFrameSize, ObjectiveTargetFrameSize) * m_scale;
        var targetRect = new Rect2(
            camera.UnprojectPosition(aimPosition) - frameSize * 0.5f,
            frameSize);
        var objectiveKind = m_mission.GetActiveObjectiveKind(actor);
        var color = objectiveKind == MissionObjectiveKind.Inspect
            ? RadarAmber
            : GaugeRed;
        DrawTargetCorners(targetRect, color);
        if (objectiveKind == MissionObjectiveKind.Inspect)
        {
            var fontSize = Math.Max((int)(16 * m_scale), 1);
            const string label = "INSPECT [I]";
            var labelWidth = ThemeDB.FallbackFont.GetStringSize(
                label,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize).X;
            DrawString(
                ThemeDB.FallbackFont,
                targetRect.GetCenter() + new Vector2(-labelWidth * 0.5f, 42.0f * m_scale),
                label,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize,
                color);
        }
    }

    private Rect2 GetScreenRect(Camera3D camera, Aabb bounds)
    {
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var x = 0; x <= 1; x++)
        {
            for (var y = 0; y <= 1; y++)
            {
                for (var z = 0; z <= 1; z++)
                {
                    var corner = bounds.Position + new Vector3(
                        bounds.Size.X * x,
                        bounds.Size.Y * y,
                        bounds.Size.Z * z);
                    var point = camera.UnprojectPosition(corner);
                    minimum = minimum.Min(point);
                    maximum = maximum.Max(point);
                }
            }
        }

        var minimumSize = new Vector2(30.0f, 30.0f) * m_scale;
        var size = maximum - minimum;
        var adjustment = (minimumSize - size).Max(Vector2.Zero);
        var rect = new Rect2(minimum - adjustment * 0.5f, size + adjustment);
        var maximumSize = MaximumTargetFrameSize * m_scale;
        var cappedSize = new Vector2(
            Math.Min(rect.Size.X, maximumSize),
            Math.Min(rect.Size.Y, maximumSize));
        return new Rect2(rect.GetCenter() - cappedSize * 0.5f, cappedSize);
    }

    private void DrawTargetCorners(Rect2 rect, Color color)
    {
        var corner = Math.Min(12.0f * m_scale, Math.Min(rect.Size.X, rect.Size.Y) * 0.4f);
        var width = LineWidth(2.0f);
        DrawLine(rect.Position, rect.Position + Vector2.Right * corner, color, width);
        DrawLine(rect.Position, rect.Position + Vector2.Down * corner, color, width);
        DrawLine(rect.End, rect.End + Vector2.Left * corner, color, width);
        DrawLine(rect.End, rect.End + Vector2.Up * corner, color, width);
        var topRight = new Vector2(rect.End.X, rect.Position.Y);
        DrawLine(topRight, topRight + Vector2.Left * corner, color, width);
        DrawLine(topRight, topRight + Vector2.Down * corner, color, width);
        var bottomLeft = new Vector2(rect.Position.X, rect.End.Y);
        DrawLine(bottomLeft, bottomLeft + Vector2.Right * corner, color, width);
        DrawLine(bottomLeft, bottomLeft + Vector2.Up * corner, color, width);
    }

    private void DrawMissionStatus()
    {
        if (string.IsNullOrWhiteSpace(m_mission.StatusMessage))
        {
            return;
        }

        DrawCenteredText(640.0f, 105.0f, m_mission.StatusMessage, HudGreen, 24);
    }

    private void DrawPlayerDamageStatus()
    {
        DrawDamageSilhouette(
            m_playerDamageSilhouette,
            PlayerDamageRight - PlayerDamageSize,
            525.25f,
            PlayerDamageSize,
            PlayerDamageSize,
            m_playerMech.Damage,
            0.0f);

        if (m_playerMech.IsDestroyed)
        {
            DrawCenteredText(640.0f, 155.0f, "MECH DESTROYED", GaugeRed, 30);
        }
    }

    private void DrawDamageSilhouette(
        MechDamageSilhouette silhouette,
        float left,
        float top,
        float width,
        float height,
        MechDamageModel damage,
        float padding)
    {
        var availableSize = new Vector2(width - padding * 2.0f, height - padding * 2.0f);
        var textureScale = Math.Min(
            availableSize.X / silhouette.Width,
            availableSize.Y / silhouette.Height);
        var textureSize = new Vector2(silhouette.Width, silhouette.Height) * textureScale;
        var textureRect = new Rect2(
            Point(
                left + (width - textureSize.X) * 0.5f,
                top + (height - textureSize.Y) * 0.5f),
            textureSize * m_scale);
        foreach (var section in Enum.GetValues<MechDamageSection>())
        {
            DrawTextureRect(
                silhouette.SectionMasks[section],
                textureRect,
                false,
                GetDamageColor(damage, section));
        }
    }

    private static Color GetDamageColor(MechDamageModel damage, MechDamageSection section)
    {
        var healthFraction = damage.GetHealthFraction(section);
        return healthFraction <= 0.0f
            ? DestroyedSectionGrey
            : healthFraction > 0.66f
                ? HudGreen
                : healthFraction > 0.33f
                    ? RadarAmber
                    : GaugeRed;
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

        DrawCenteredText(
            PlayerDamageCenterX,
            687.0f,
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
