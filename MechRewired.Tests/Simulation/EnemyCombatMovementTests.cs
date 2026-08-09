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
public sealed class EnemyCombatMovementTests
{
    [Test]
    public void ClosesWhenOutsideFiringBand()
    {
        var movement = new EnemyCombatMovement(500.0, 1);

        var step = movement.Advance(0.1, 500.0, true, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(step.Mode, Is.EqualTo(EnemyCombatMovementMode.Closing));
            Assert.That(step.Radial, Is.Positive);
            Assert.That(step.Lateral, Is.Not.Zero);
        });
    }

    [Test]
    public void OrbitsInsideFiringBand()
    {
        var movement = new EnemyCombatMovement(500.0, 1);

        var step = movement.Advance(0.1, 280.0, true, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(step.Mode, Is.EqualTo(EnemyCombatMovementMode.Orbiting));
            Assert.That(Math.Abs(step.Lateral), Is.GreaterThan(Math.Abs(step.Radial)));
        });
    }

    [Test]
    public void CreatesSpaceWithoutRunningDirectlyAway()
    {
        var movement = new EnemyCombatMovement(500.0, 1);

        var step = movement.Advance(0.1, 100.0, true, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(step.Mode, Is.EqualTo(EnemyCombatMovementMode.CreatingSpace));
            Assert.That(step.Radial, Is.Negative);
            Assert.That(Math.Abs(step.Lateral), Is.GreaterThan(Math.Abs(step.Radial)));
        });
    }

    [Test]
    public void DamageTriggersFiniteEvasiveStrafeAndChangesDirection()
    {
        var movement = new EnemyCombatMovement(500.0, 1);
        var original = movement.Advance(0.1, 280.0, true, 1.0);

        movement.NotifyDamage();
        var evasive = movement.Advance(0.1, 280.0, true, 1.0);
        var recovered = movement.Advance(3.0, 280.0, true, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(evasive.Mode, Is.EqualTo(EnemyCombatMovementMode.Evading));
            Assert.That(Math.Sign(evasive.Lateral), Is.Not.EqualTo(Math.Sign(original.Lateral)));
            Assert.That(recovered.Mode, Is.EqualTo(EnemyCombatMovementMode.Orbiting));
        });
    }

    [Test]
    public void WeakTargetMakesEnemyCloseFurther()
    {
        var movement = new EnemyCombatMovement(500.0, 1);

        var healthyTarget = movement.Advance(0.1, 250.0, true, 1.0);
        var weakTarget = movement.Advance(0.1, 250.0, true, 0.2);

        Assert.Multiple(() =>
        {
            Assert.That(healthyTarget.Mode, Is.EqualTo(EnemyCombatMovementMode.Orbiting));
            Assert.That(weakTarget.Mode, Is.EqualTo(EnemyCombatMovementMode.Closing));
        });
    }

    [Test]
    public void SearchesTowardLastKnownPositionWithoutLineOfSight()
    {
        var movement = new EnemyCombatMovement(500.0, 1);

        var step = movement.Advance(0.1, 300.0, false, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(step.Mode, Is.EqualTo(EnemyCombatMovementMode.Searching));
            Assert.That(step.Radial, Is.Positive);
            Assert.That(step.Lateral, Is.Zero);
        });
    }

    [Test]
    public void SpeedAcceleratesInsteadOfChangingInstantly()
    {
        var movement = new EnemyCombatMovement(500.0, 1);

        var first = movement.Advance(0.1, 500.0, true, 1.0);
        var second = movement.Advance(0.1, 500.0, true, 1.0);
        for (var i = 0; i < 20; i++)
        {
            movement.Advance(0.1, 500.0, true, 1.0);
        }

        var settled = movement.Advance(0.1, 500.0, true, 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(first.SpeedFraction, Is.GreaterThan(0.0).And.LessThan(0.78));
            Assert.That(second.SpeedFraction, Is.GreaterThan(first.SpeedFraction));
            Assert.That(settled.SpeedFraction, Is.EqualTo(0.78).Within(0.0001));
        });
    }
}
