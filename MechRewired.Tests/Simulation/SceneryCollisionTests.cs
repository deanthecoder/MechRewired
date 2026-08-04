// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Numerics;
using MechRewired.Simulation;
using NUnit.Framework;

namespace MechRewired.Tests.Simulation;

[TestFixture]
public sealed class SceneryCollisionTests
{
    private static readonly SceneryObstacle ChemicalTank = new(
        "Chemical Tank",
        new Vector2(10.0f, 10.0f),
        new Vector2(14.0f, 14.0f));

    [Test]
    public void EnteringASceneryFootprintIsBlocked()
    {
        var blocked = SceneryCollision.TryFindBlockingObstacle(
            new Vector2(0.0f, 12.0f),
            new Vector2(20.0f, 12.0f),
            1.5f,
            [ChemicalTank],
            out var obstacle);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Is.True);
            Assert.That(obstacle, Is.EqualTo(ChemicalTank));
        });
    }

    [Test]
    public void MechRadiusPreventsClippingPastAnObstacle()
    {
        var blocked = SceneryCollision.TryFindBlockingObstacle(
            new Vector2(0.0f, 8.7f),
            new Vector2(20.0f, 8.7f),
            1.5f,
            [ChemicalTank],
            out _);

        Assert.That(blocked, Is.True);
    }

    [Test]
    public void MovementAwayFromAnOverlappingDeploymentPositionIsAllowed()
    {
        var blocked = SceneryCollision.TryFindBlockingObstacle(
            new Vector2(12.0f, 12.0f),
            new Vector2(0.0f, 12.0f),
            1.5f,
            [ChemicalTank],
            out _);

        Assert.That(blocked, Is.False);
    }

    [Test]
    public void MovementDeeperIntoAnOverlappingObstacleIsBlocked()
    {
        var blocked = SceneryCollision.TryFindBlockingObstacle(
            new Vector2(10.0f, 12.0f),
            new Vector2(11.0f, 12.0f),
            1.5f,
            [ChemicalTank],
            out var obstacle);

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Is.True);
            Assert.That(obstacle, Is.EqualTo(ChemicalTank));
        });
    }

    [Test]
    public void OverlapIsResolvedThroughTheNearestExpandedFace()
    {
        var resolved = SceneryCollision.TryResolveOverlap(
            new Vector2(10.0f, 12.0f),
            1.5f,
            [ChemicalTank],
            out var position,
            out var obstacle);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(position.X, Is.LessThan(8.5f));
            Assert.That(position.Y, Is.EqualTo(12.0f));
            Assert.That(obstacle, Is.EqualTo(ChemicalTank));
        });
    }

    [Test]
    public void PositionOutsideExpandedFootprintNeedsNoResolution()
    {
        var resolved = SceneryCollision.TryResolveOverlap(
            new Vector2(0.0f, 0.0f),
            1.5f,
            [ChemicalTank],
            out var position,
            out var obstacle);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.False);
            Assert.That(position, Is.EqualTo(new Vector2(0.0f, 0.0f)));
            Assert.That(obstacle, Is.Null);
        });
    }

    [Test]
    public void CrossingAnAuthoredWallIsBlocked()
    {
        var wall = new SceneryObstacle(
            "Wall",
            new Vector2(5.0f, 0.0f),
            new Vector2(5.0f, 10.0f),
            [new SceneryWallTriangle(
                new Vector2(5.0f, 0.0f),
                new Vector2(5.0f, 10.0f),
                new Vector2(5.0f, 0.0f))]);

        var blocked = SceneryCollision.TryFindBlockingObstacle(
            new Vector2(0.0f, 5.0f),
            new Vector2(10.0f, 5.0f),
            1.0f,
            [wall],
            out _);

        Assert.That(blocked, Is.True);
    }

    [Test]
    public void EmptySpaceInsideBroadBoundsRemainsPassable()
    {
        var openStructure = new SceneryObstacle(
            "Open structure",
            Vector2.Zero,
            new Vector2(10.0f, 10.0f),
            [
                new SceneryWallTriangle(
                    new Vector2(0.0f, 0.0f),
                    new Vector2(0.0f, 10.0f),
                    new Vector2(0.0f, 0.0f)),
                new SceneryWallTriangle(
                    new Vector2(10.0f, 0.0f),
                    new Vector2(10.0f, 10.0f),
                    new Vector2(10.0f, 0.0f))
            ]);

        var blocked = SceneryCollision.TryFindBlockingObstacle(
            new Vector2(5.0f, 1.0f),
            new Vector2(5.0f, 9.0f),
            1.0f,
            [openStructure],
            out _);

        Assert.That(blocked, Is.False);
    }

    [Test]
    public void WideMechCanMoveAwayFromAnAuthoredWallCorner()
    {
        var building = new SceneryObstacle(
            "Building",
            Vector2.Zero,
            new Vector2(25.0f, 5.0f),
            [
                new SceneryWallTriangle(
                    new Vector2(0.0f, 0.0f),
                    new Vector2(25.0f, 0.0f),
                    new Vector2(25.0f, 5.0f)),
                new SceneryWallTriangle(
                    new Vector2(0.0f, 0.0f),
                    new Vector2(25.0f, 5.0f),
                    new Vector2(0.0f, 5.0f))
            ]);

        var blocked = SceneryCollision.TryFindBlockingObstacle(
            new Vector2(27.29f, 5.75f),
            new Vector2(27.69f, 5.75f),
            3.2f,
            [building],
            out _);

        Assert.That(blocked, Is.False);
    }

    [Test]
    public void WideMechCannotMoveDeeperIntoAnAuthoredWallCorner()
    {
        var building = new SceneryObstacle(
            "Building",
            Vector2.Zero,
            new Vector2(25.0f, 5.0f),
            [
                new SceneryWallTriangle(
                    new Vector2(0.0f, 0.0f),
                    new Vector2(25.0f, 0.0f),
                    new Vector2(25.0f, 5.0f)),
                new SceneryWallTriangle(
                    new Vector2(0.0f, 0.0f),
                    new Vector2(25.0f, 5.0f),
                    new Vector2(0.0f, 5.0f))
            ]);

        var blocked = SceneryCollision.TryFindBlockingObstacle(
            new Vector2(27.29f, 5.75f),
            new Vector2(26.89f, 5.75f),
            3.2f,
            [building],
            out _);

        Assert.That(blocked, Is.True);
    }
}
