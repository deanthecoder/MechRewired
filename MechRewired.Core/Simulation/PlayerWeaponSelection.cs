// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Simulation;

/// <summary>
/// Tracks MW2's selected weapon, three mutable weapon groups and chain/group firing mode.
/// </summary>
public sealed class PlayerWeaponSelection
{
    public const int GroupCount = 3;
    private readonly int[] m_groups;
    private readonly int[] m_cycleOrder;

    public PlayerWeaponSelection(IReadOnlyList<MechMountedWeapon> weapons)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        if (weapons.Count == 0)
        {
            throw new ArgumentException("At least one mounted weapon is required.", nameof(weapons));
        }

        Weapons = weapons;
        m_groups = weapons
            .Select(weapon => weapon.AuthoredGroup is >= 0 and < GroupCount
                ? weapon.AuthoredGroup
                : 0)
            .ToArray();
        var columns = BuildColumns(weapons);
        m_cycleOrder = Enumerable.Range(0, columns.Max(column => column.Count))
            .SelectMany(row => columns.Where(column => row < column.Count).Select(column => column[row]))
            .ToArray();
        SelectedWeaponIndex = m_cycleOrder[0];
    }

    public IReadOnlyList<MechMountedWeapon> Weapons { get; }

    public int SelectedWeaponIndex { get; private set; }

    public int SelectedGroup { get; private set; }

    public bool GroupFireEnabled { get; private set; }

    public MechMountedWeapon SelectedWeapon => Weapons[SelectedWeaponIndex];

    public int GetGroup(int weaponIndex) => m_groups[weaponIndex];

    public void CycleWeapon(int direction = 1, Func<int, bool> canSelect = null)
    {
        var current = Array.IndexOf(m_cycleOrder, SelectedWeaponIndex);
        var step = Math.Sign(direction);
        for (var offset = 1; offset <= m_cycleOrder.Length; offset++)
        {
            var candidate = m_cycleOrder[Wrap(current + step * offset, m_cycleOrder.Length)];
            if (m_groups[candidate] == SelectedGroup && (canSelect?.Invoke(candidate) ?? true))
            {
                SelectedWeaponIndex = candidate;
                return;
            }
        }
    }

    public static IReadOnlyList<IReadOnlyList<int>> BuildColumns(IReadOnlyList<MechMountedWeapon> weapons)
    {
        List<int>[] columns = [[], []];
        for (var index = 0; index < weapons.Count; index++)
        {
            var column = weapons[index].Section switch
            {
                MechDamageSection.LeftArm or MechDamageSection.LeftTorso or MechDamageSection.LeftLeg => 0,
                MechDamageSection.RightArm or MechDamageSection.RightTorso or MechDamageSection.RightLeg => 1,
                _ => columns[0].Count <= columns[1].Count ? 0 : 1
            };
            columns[column].Add(index);
        }

        return columns;
    }

    public void AssignSelectedToGroup(int group)
    {
        if (group < 0 || group >= GroupCount)
        {
            throw new ArgumentOutOfRangeException(nameof(group));
        }

        m_groups[SelectedWeaponIndex] = group;
        SelectedGroup = group;
    }

    public void CycleGroup(int direction = 1, Func<int, bool> canSelect = null)
    {
        var step = Math.Sign(direction);
        for (var offset = 1; offset <= GroupCount; offset++)
        {
            var candidate = Wrap(SelectedGroup + step * offset, GroupCount);
            var firstWeapon = Array.FindIndex(m_groups, group => group == candidate);
            if (canSelect != null)
            {
                firstWeapon = Enumerable.Range(0, Weapons.Count)
                    .FirstOrDefault(index => m_groups[index] == candidate && canSelect(index), -1);
            }
            if (firstWeapon < 0)
            {
                continue;
            }

            SelectedGroup = candidate;
            SelectedWeaponIndex = firstWeapon;
            return;
        }
    }

    public IReadOnlyList<int> GetFireIndices(bool forceGroup = false)
    {
        if (forceGroup)
        {
            return Enumerable.Range(0, Weapons.Count)
                .Where(index => m_groups[index] == SelectedGroup)
                .ToArray();
        }

        return [SelectedWeaponIndex];
    }

    public void AdvanceAfterFire(bool forcedGroup = false, Func<int, bool> canSelect = null)
    {
        if (forcedGroup)
        {
            return;
        }

        CycleWeapon(1, canSelect);
    }

    private static int Wrap(int value, int count) => (value % count + count) % count;
}
