// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Resources;
using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

[TestFixture]
public sealed class MechWarriorMechFileTests
{
    [Test]
    public void LoadDecodesTimberWolfMovementSpeeds()
    {
        var data = CreateMechData();
        BitConverter.GetBytes(75).CopyTo(data, 0);
        BitConverter.GetBytes(5).CopyTo(data, 4);

        var mech = MechWarriorMechFile.Load(data);

        Assert.That(mech.Tonnage, Is.EqualTo(75));
        Assert.That(mech.WalkingMovementPoints, Is.EqualTo(5));
        Assert.That(mech.HeatSinkCount, Is.EqualTo(20));
        Assert.That(mech.CruisingSpeedKph, Is.EqualTo(54.0));
        Assert.That(mech.MaximumSpeedKph, Is.EqualTo(86.4));
        Assert.That(mech.Sections[MechDamageSection.CenterTorso], Is.EqualTo(new MechSectionArmor(40, 12, 30)));
    }

    [Test]
    public void LoadRejectsATruncatedGeneralHeader()
    {
        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorMechFile.Load(new byte[0x157]));

        Assert.That(exception.Message, Does.Contain("343 bytes"));
        Assert.That(exception.Message, Does.Contain("344 bytes"));
    }

    [Test]
    public void LoadDecodesMountedWeaponIdentitySectionAndUnsetGroup()
    {
        var data = CreateMechData(1);
        BitConverter.GetBytes(75).CopyTo(data, 0);
        BitConverter.GetBytes(5).CopyTo(data, 4);
        BitConverter.GetBytes(1).CopyTo(data, 0x10);
        BitConverter.GetBytes((ushort)2201).CopyTo(data, 0x158);
        BitConverter.GetBytes(ushort.MaxValue).CopyTo(data, 0x15d);
        BitConverter.GetBytes((ushort)2201).CopyTo(data, 0x0e0 + 12);

        var mech = MechWarriorMechFile.Load(data);

        Assert.That(mech.Weapons, Has.Count.EqualTo(1));
        Assert.That(mech.Weapons[0], Is.EqualTo(new MechMountedWeapon(
            2201,
            MechWeaponCatalog.TryGet(2201, out var specification) ? specification : null,
            MechDamageSection.LeftArm,
            -1)));
        Assert.That(mech.AmmoBinCount, Is.Zero);
        Assert.That(mech.UnsupportedWeaponIds, Is.Empty);
    }

    private static byte[] CreateMechData(int equipmentRecords = 0)
    {
        var data = new byte[0x158 + equipmentRecords * 8];
        int[] offsets = [0x018, 0x068, 0x090, 0x040, 0x0e0, 0x0b8, 0x108, 0x130];
        foreach (var offset in offsets)
        {
            BitConverter.GetBytes(20).CopyTo(data, offset);
            BitConverter.GetBytes(0).CopyTo(data, offset + 4);
            BitConverter.GetBytes(15).CopyTo(data, offset + 8);
        }

        BitConverter.GetBytes(40).CopyTo(data, 0x068);
        BitConverter.GetBytes(12).CopyTo(data, 0x06c);
        BitConverter.GetBytes(30).CopyTo(data, 0x070);
        BitConverter.GetBytes(20).CopyTo(data, 0x0c);
        return data;
    }
}
