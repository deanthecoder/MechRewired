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
/// Tracks the player's selected and reached mission navigation points.
/// </summary>
/// <remarks>
/// Mission progression is kept outside the HUD so cockpit and future VR displays can observe the same state.
/// </remarks>
public partial class PlayerNavigation : Node
{
    private readonly PlayerMech m_playerMech;
    private readonly IReadOnlyList<MechWarriorMissionAreaBoundary> m_missionAreaBoundaries;
    private readonly bool[] m_reached;
    private readonly bool[] m_inside;
    private readonly bool[] m_insideMissionAreaBoundaries;
    private readonly bool[] m_triggeredMissionAreaBoundaries;
    private readonly AudioStreamPlayer m_tonePlayer;
    private bool m_missionAreaBoundariesInitialized;

    public PlayerNavigation(
        PlayerMech playerMech,
        IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        IReadOnlyList<MechWarriorMissionAreaBoundary> missionAreaBoundaries,
        AudioStreamWav reachedTone)
    {
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(navigationPoints);
        ArgumentNullException.ThrowIfNull(missionAreaBoundaries);
        ArgumentNullException.ThrowIfNull(reachedTone);
        if (navigationPoints.Count == 0)
        {
            throw new ArgumentException("At least one navigation point is required.", nameof(navigationPoints));
        }

        Name = "PlayerNavigation";
        m_playerMech = playerMech;
        m_missionAreaBoundaries = missionAreaBoundaries;
        MissionNavigationPoints = navigationPoints;
        NavigationPoints = navigationPoints.Select(navigationPoint => navigationPoint.Point).ToArray();
        m_reached = new bool[navigationPoints.Count];
        m_inside = new bool[navigationPoints.Count];
        m_insideMissionAreaBoundaries = new bool[missionAreaBoundaries.Count];
        m_triggeredMissionAreaBoundaries = new bool[missionAreaBoundaries.Count];
        m_tonePlayer = new AudioStreamPlayer
        {
            Name = "NavigationTone",
            Stream = reachedTone
        };
        AddChild(m_tonePlayer);
    }

    public IReadOnlyList<MechWarriorWorldNavPoint> NavigationPoints { get; }

    public IReadOnlyList<MechWarriorMissionNavigationPoint> MissionNavigationPoints { get; }

    public event Action<int> NavigationPointReached;

    public event Action<MechWarriorMissionAreaBoundary> MissionAreaBoundaryExited;

    public int SelectedIndex { get; private set; }

    public MechWarriorWorldNavPoint SelectedPoint => NavigationPoints[SelectedIndex];

    public float DistanceToSelectedMeters => DistanceTo(SelectedPoint);

    public bool IsReached(int index) => m_reached[index];

    public override void _PhysicsProcess(double delta)
    {
        UpdateMissionAreaBoundaries();
        if (DistanceToSelectedMeters > SelectedPoint.Radius)
        {
            m_inside[SelectedIndex] = false;
            return;
        }

        var reachedIndex = SelectedIndex;
        if (m_inside[reachedIndex])
        {
            return;
        }

        m_inside[reachedIndex] = true;
        NavigationPointReached?.Invoke(reachedIndex);
        if (m_reached[reachedIndex])
        {
            GD.Print($"MechRewired: re-entered NAV '{SelectedPoint.Description}'.");
            return;
        }

        m_reached[reachedIndex] = true;
        m_tonePlayer.Play();
        GD.Print(
            $"MechRewired: reached NAV '{SelectedPoint.Description}' within its " +
            $"{SelectedPoint.Radius}m proximity radius.");
        if (reachedIndex < NavigationPoints.Count - 1)
        {
            Select(reachedIndex + 1, true);
        }
    }

    public void SelectNext() => Select((SelectedIndex + 1) % NavigationPoints.Count, true);

    public void SelectPrevious() =>
        Select((SelectedIndex - 1 + NavigationPoints.Count) % NavigationPoints.Count, true);

    private void Select(int index, bool logChange)
    {
        SelectedIndex = index;
        if (logChange)
        {
            GD.Print($"MechRewired: selected NAV '{SelectedPoint.Description}'.");
        }
    }

    private float DistanceTo(MechWarriorWorldNavPoint navigationPoint)
    {
        var navigationPosition = MechWarriorCoordinateSystem.ToGodotPosition(navigationPoint.Position);
        var offset = navigationPosition - m_playerMech.GlobalPosition;
        return new Vector2(offset.X, offset.Z).Length();
    }

    private void UpdateMissionAreaBoundaries()
    {
        for (var index = 0; index < m_missionAreaBoundaries.Count; index++)
        {
            var boundary = m_missionAreaBoundaries[index];
            var isInside = DistanceTo(boundary.Point) <= boundary.Point.Radius;
            if (m_missionAreaBoundariesInitialized &&
                m_insideMissionAreaBoundaries[index] &&
                !isInside &&
                !m_triggeredMissionAreaBoundaries[index])
            {
                m_triggeredMissionAreaBoundaries[index] = true;
                GD.Print(
                    $"MechRewired: exited mission-area boundary '{boundary.ResourceName}' at " +
                    $"{boundary.Point.Radius}m.");
                MissionAreaBoundaryExited?.Invoke(boundary);
            }

            m_insideMissionAreaBoundaries[index] = isInside;
        }

        m_missionAreaBoundariesInitialized = true;
    }
}
