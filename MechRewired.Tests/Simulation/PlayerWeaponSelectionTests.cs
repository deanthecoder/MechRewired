// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Simulation;

[TestFixture]
public sealed class PlayerWeaponSelectionTests
{
    [Test]
    public void IndividualFireAdvancesWithinSelectedGroup()
    {
        var selection = new PlayerWeaponSelection(
        [
            Weapon(2201),
            Weapon(2301),
            Weapon(1)
        ]);

        Assert.That(selection.GetFireIndices(), Is.EqualTo(new[] { 0 }));
        Assert.That(selection.GetGroup(0), Is.Zero);
        selection.AdvanceAfterFire();
        Assert.That(selection.SelectedWeaponIndex, Is.EqualTo(1));

        selection.AssignSelectedToGroup(1);
        selection.CycleGroup(-1);

        Assert.That(selection.GetFireIndices(), Is.EqualTo(new[] { 0 }));
        selection.AdvanceAfterFire();
        Assert.That(selection.GetFireIndices(), Is.EqualTo(new[] { 2 }));
        Assert.That(selection.SelectedGroup, Is.Zero);
        Assert.That(selection.GetFireIndices(true), Is.EqualTo(new[] { 0, 2 }));
    }

    [Test]
    public void ApostropheCyclesPopulatedGroupsAndSelectsFirstWeapon()
    {
        var selection = new PlayerWeaponSelection([Weapon(2201), Weapon(2301), Weapon(1)]);
        selection.AssignSelectedToGroup(0);
        selection.CycleWeapon();
        selection.AssignSelectedToGroup(2);

        Assert.That(selection.SelectedGroup, Is.EqualTo(2));
        Assert.That(selection.SelectedWeaponIndex, Is.EqualTo(1));
        selection.CycleGroup();
        Assert.That(selection.SelectedGroup, Is.EqualTo(0));
        Assert.That(selection.SelectedWeaponIndex, Is.EqualTo(0));
    }

    [Test]
    public void WeaponsWithoutAuthoredGroupsDefaultToGroupOne()
    {
        var selection = new PlayerWeaponSelection([Weapon(2201), Weapon(2301)]);

        Assert.That(selection.GetFireIndices(true), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void WeaponCycleAlternatesHudColumnsBeforeAdvancingRows()
    {
        var selection = new PlayerWeaponSelection(
        [
            Weapon(2201, MechDamageSection.LeftArm),
            Weapon(2301, MechDamageSection.LeftTorso),
            Weapon(2202, MechDamageSection.RightArm),
            Weapon(2302, MechDamageSection.RightTorso)
        ]);

        Assert.That(selection.SelectedWeaponIndex, Is.EqualTo(0));
        selection.CycleWeapon();
        Assert.That(selection.SelectedWeaponIndex, Is.EqualTo(2));
        selection.CycleWeapon();
        Assert.That(selection.SelectedWeaponIndex, Is.EqualTo(1));
        selection.CycleWeapon();
        Assert.That(selection.SelectedWeaponIndex, Is.EqualTo(3));
    }

    private static MechMountedWeapon Weapon(
        ushort sourceId,
        MechDamageSection section = MechDamageSection.LeftArm)
    {
        Assert.That(MechWeaponCatalog.TryGet(sourceId, out var specification), Is.True);
        return new MechMountedWeapon(sourceId, specification, section, -1);
    }
}
