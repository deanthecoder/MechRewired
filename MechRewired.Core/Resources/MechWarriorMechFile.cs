// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Resources;

using System.Buffers.Binary;
using MechRewired.Simulation;

/// <summary>
/// Decodes the general movement header from an original MW2 MEK configuration.
/// </summary>
public sealed class MechWarriorMechFile
{
    private const int BaseFileSize = 0x158;
    private const int WeaponCountOffset = 0x10;
    private const int AmmoCountOffset = 0x14;
    private const int EquipmentRecordSize = 8;
    private const int CriticalSlotsOffset = 12;
    private const double MovementPointSpeedKph = 10.8;
    private static readonly IReadOnlyDictionary<MechDamageSection, int> SectionOffsets =
        new Dictionary<MechDamageSection, int>
        {
            [MechDamageSection.Head] = 0x018,
            [MechDamageSection.CenterTorso] = 0x068,
            [MechDamageSection.LeftTorso] = 0x090,
            [MechDamageSection.RightTorso] = 0x040,
            [MechDamageSection.LeftArm] = 0x0e0,
            [MechDamageSection.RightArm] = 0x0b8,
            [MechDamageSection.LeftLeg] = 0x108,
            [MechDamageSection.RightLeg] = 0x130
        };
    private static readonly IReadOnlyDictionary<MechDamageSection, int> CriticalCounts =
        new Dictionary<MechDamageSection, int>
        {
            [MechDamageSection.Head] = 6,
            [MechDamageSection.CenterTorso] = 12,
            [MechDamageSection.LeftTorso] = 12,
            [MechDamageSection.RightTorso] = 12,
            [MechDamageSection.LeftArm] = 12,
            [MechDamageSection.RightArm] = 12,
            [MechDamageSection.LeftLeg] = 6,
            [MechDamageSection.RightLeg] = 6
        };

    private MechWarriorMechFile(
        int tonnage,
        int walkingMovementPoints,
        IReadOnlyDictionary<MechDamageSection, MechSectionArmor> sections,
        IReadOnlyList<MechMountedWeapon> weapons,
        IReadOnlyList<ushort> unsupportedWeaponIds,
        int ammoBinCount)
    {
        Tonnage = tonnage;
        WalkingMovementPoints = walkingMovementPoints;
        Sections = sections;
        Weapons = weapons;
        UnsupportedWeaponIds = unsupportedWeaponIds;
        AmmoBinCount = ammoBinCount;
    }

    public int Tonnage { get; }

    public int WalkingMovementPoints { get; }

    public int RunningMovementPoints => (int)Math.Ceiling(WalkingMovementPoints * 1.5);

    public double CruisingSpeedKph => WalkingMovementPoints * MovementPointSpeedKph;

    public double MaximumSpeedKph => RunningMovementPoints * MovementPointSpeedKph;

    public IReadOnlyDictionary<MechDamageSection, MechSectionArmor> Sections { get; }

    public IReadOnlyList<MechMountedWeapon> Weapons { get; }

    public IReadOnlyList<ushort> UnsupportedWeaponIds { get; }

    public int AmmoBinCount { get; }

    public MechWarriorMechFile WithWeapons(IReadOnlyList<MechMountedWeapon> weapons, int ammoBinCount) =>
        new(
            Tonnage,
            WalkingMovementPoints,
            Sections,
            weapons,
            [],
            ammoBinCount);

    public static MechWarriorMechFile Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < BaseFileSize)
        {
            throw new InvalidDataException(
                $"The MEK resource is {data.Length} bytes; at least {BaseFileSize} bytes are required.");
        }

        using var stream = new MemoryStream(data, false);
        using var reader = new BinaryReader(stream);
        var tonnage = reader.ReadInt32();
        var walkingMovementPoints = reader.ReadInt32();
        if (tonnage <= 0)
        {
            throw new InvalidDataException($"The MEK tonnage must be positive; found {tonnage}.");
        }

        if (walkingMovementPoints <= 0)
        {
            throw new InvalidDataException(
                $"The MEK walking movement points must be positive; found {walkingMovementPoints}.");
        }

        var sections = SectionOffsets.ToDictionary(
            entry => entry.Key,
            entry => new MechSectionArmor(
                BitConverter.ToInt32(data, entry.Value),
                BitConverter.ToInt32(data, entry.Value + 4),
                BitConverter.ToInt32(data, entry.Value + 8)));
        foreach (var (section, armor) in sections)
        {
            if (armor.FrontArmor < 0 || armor.RearArmor < 0 || armor.InternalStructure <= 0)
            {
                throw new InvalidDataException(
                    $"The MEK {section} values are invalid: front {armor.FrontArmor}, rear " +
                    $"{armor.RearArmor}, internal {armor.InternalStructure}.");
            }
        }

        var weaponCount = ReadNonNegativeCount(data, WeaponCountOffset, "weapon");
        var ammoBinCount = ReadNonNegativeCount(data, AmmoCountOffset, "ammunition-bin");
        var equipmentTableSize = checked((weaponCount + ammoBinCount) * EquipmentRecordSize);
        if (equipmentTableSize > data.Length - BaseFileSize)
        {
            throw new InvalidDataException(
                $"The MEK equipment table requires {equipmentTableSize} bytes after its " +
                $"{BaseFileSize}-byte chassis block, but only {data.Length - BaseFileSize} remain.");
        }

        var weapons = new List<MechMountedWeapon>();
        var unsupportedWeaponIds = new List<ushort>();
        for (var index = 0; index < weaponCount; index++)
        {
            var recordOffset = BaseFileSize + index * EquipmentRecordSize;
            var sourceId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(recordOffset, 2));
            if (!MechWeaponCatalog.TryGet(sourceId, out var specification))
            {
                unsupportedWeaponIds.Add(sourceId);
                continue;
            }

            var section = FindWeaponSection(data, sourceId);
            var authoredGroup = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(recordOffset + 5, 2));
            weapons.Add(new MechMountedWeapon(
                sourceId,
                specification,
                section,
                authoredGroup == ushort.MaxValue ? -1 : authoredGroup % PlayerWeaponSelection.GroupCount));
        }

        return new MechWarriorMechFile(
            tonnage,
            walkingMovementPoints,
            sections,
            weapons.AsReadOnly(),
            unsupportedWeaponIds.AsReadOnly(),
            ammoBinCount);
    }

    private static int ReadNonNegativeCount(byte[] data, int offset, string name)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        return count < 0
            ? throw new InvalidDataException($"The MEK {name} count cannot be negative; found {count}.")
            : count;
    }

    private static MechDamageSection FindWeaponSection(byte[] data, ushort sourceId)
    {
        foreach (var (section, sectionOffset) in SectionOffsets)
        {
            for (var slot = 0; slot < CriticalCounts[section]; slot++)
            {
                var slotOffset = sectionOffset + CriticalSlotsOffset + slot * 2;
                if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(slotOffset, 2)) == sourceId)
                {
                    return section;
                }
            }
        }

        throw new InvalidDataException(
            $"The MEK weapon instance {sourceId} is not installed in any critical slot.");
    }
}
