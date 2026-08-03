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
    private readonly IReadOnlyList<AudioStreamWav> m_reachedReports;
    private readonly bool[] m_reached;
    private readonly bool[] m_inside;
    private readonly AudioStreamPlayer m_tonePlayer;
    private readonly AudioStreamPlayer m_reportPlayer;

    public PlayerNavigation(
        PlayerMech playerMech,
        IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        AudioStreamWav reachedTone,
        IReadOnlyList<AudioStreamWav> reachedReports)
    {
        ArgumentNullException.ThrowIfNull(playerMech);
        ArgumentNullException.ThrowIfNull(navigationPoints);
        ArgumentNullException.ThrowIfNull(reachedTone);
        ArgumentNullException.ThrowIfNull(reachedReports);
        if (navigationPoints.Count == 0)
        {
            throw new ArgumentException("At least one navigation point is required.", nameof(navigationPoints));
        }

        Name = "PlayerNavigation";
        m_playerMech = playerMech;
        MissionNavigationPoints = navigationPoints;
        NavigationPoints = navigationPoints.Select(navigationPoint => navigationPoint.Point).ToArray();
        m_reachedReports = reachedReports;
        m_reached = new bool[navigationPoints.Count];
        m_inside = new bool[navigationPoints.Count];
        m_tonePlayer = new AudioStreamPlayer
        {
            Name = "NavigationTone",
            Stream = reachedTone
        };
        m_reportPlayer = new AudioStreamPlayer
        {
            Name = "NavigationReport"
        };
        AddChild(m_tonePlayer);
        AddChild(m_reportPlayer);
    }

    public IReadOnlyList<MechWarriorWorldNavPoint> NavigationPoints { get; }

    public IReadOnlyList<MechWarriorMissionNavigationPoint> MissionNavigationPoints { get; }

    public event Action<int> NavigationPointReached;

    public int SelectedIndex { get; private set; }

    public MechWarriorWorldNavPoint SelectedPoint => NavigationPoints[SelectedIndex];

    public float DistanceToSelectedMeters => DistanceTo(SelectedPoint);

    public bool IsReached(int index) => m_reached[index];

    public override void _PhysicsProcess(double delta)
    {
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
        if (reachedIndex < m_reachedReports.Count)
        {
            m_reportPlayer.Stream = m_reachedReports[reachedIndex];
            m_reportPlayer.Play();
        }

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
}
