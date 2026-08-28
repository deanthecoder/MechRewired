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
public sealed class MechDamageModelTests
{
    [Test]
    public void DamageConsumesArmorThenInternalStructure()
    {
        var model = CreateModel();

        model.ApplyDamage(MechDamageSection.LeftArm, 12);

        Assert.That(model.GetRemaining(MechDamageSection.LeftArm), Is.EqualTo(new MechSectionArmor(0, 0, 8)));
        Assert.That(model.IsSectionDestroyed(MechDamageSection.LeftArm), Is.False);
    }

    [Test]
    public void RearTorsoDamageUsesRearArmor()
    {
        var model = CreateModel();

        var result = model.ApplyDamage(MechDamageSection.CenterTorso, 4, true);

        Assert.That(result.RearArmorHit, Is.True);
        Assert.That(model.GetRemaining(MechDamageSection.CenterTorso).RearArmor, Is.EqualTo(1));
        Assert.That(model.GetRemaining(MechDamageSection.CenterTorso).FrontArmor, Is.EqualTo(10));
    }

    [Test]
    public void OneDestroyedLegImmobilizesButDoesNotDestroyMech()
    {
        var model = CreateModel();

        model.ApplyDamage(MechDamageSection.LeftLeg, 20);

        Assert.That(model.IsSectionDestroyed(MechDamageSection.LeftLeg), Is.True);
        Assert.That(model.IsDestroyed, Is.False);
    }

    [Test]
    public void BothDestroyedLegsDestroyMech()
    {
        var model = CreateModel();

        model.ApplyDamage(MechDamageSection.LeftLeg, 20);
        var result = model.ApplyDamage(MechDamageSection.RightLeg, 20);

        Assert.That(result.MechDestroyed, Is.True);
    }

    [TestCase(MechDamageSection.Head)]
    [TestCase(MechDamageSection.CenterTorso)]
    public void VitalSectionDestructionDestroysMech(MechDamageSection section)
    {
        var result = CreateModel().ApplyDamage(section, 30);

        Assert.That(result.MechDestroyed, Is.True);
    }

    [Test]
    public void InactiveLegSectionsDoNotStartFixedEmplacementDestroyed()
    {
        var sections = Enum.GetValues<MechDamageSection>().ToDictionary(
            section => section,
            section => section is MechDamageSection.LeftLeg or MechDamageSection.RightLeg or
                MechDamageSection.LeftTorso or MechDamageSection.RightTorso
                ? new MechSectionArmor(0, 0, 0)
                : new MechSectionArmor(10, 0, 5));

        var model = new MechDamageModel(sections);

        Assert.That(model.IsDestroyed, Is.False);
        Assert.That(model.ApplyDamage(MechDamageSection.CenterTorso, 15).MechDestroyed, Is.True);
    }

    private static MechDamageModel CreateModel() => new(
        Enum.GetValues<MechDamageSection>().ToDictionary(
            section => section,
            section => new MechSectionArmor(10, section is MechDamageSection.CenterTorso or MechDamageSection.LeftTorso or MechDamageSection.RightTorso ? 5 : 0, 10)));
}
