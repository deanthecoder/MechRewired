// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Missions;
using MechRewired.Resources;
using MechRewired.Tests.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Missions;

/// <summary>
/// Verifies data-driven objective activation and completion.
/// </summary>
/// <remarks>
/// Tests exercise Pyre Light-shaped data without placing mission-specific rules in the runtime.
/// </remarks>
[TestFixture]
public sealed class MissionRuntimeTests
{
    [Test]
    public void PrimaryObjectivesActivateTogetherAndGateExtraction()
    {
        var definition = CreateDefinition();
        var runtime = new MissionRuntime(definition);
        var destroy = definition.Objectives[0];
        var inspect = definition.Objectives[1];
        var extract = definition.Objectives[2];

        var destroyTransitions = runtime.Apply(new MissionEvent(
            MissionEventKind.TargetDestroyed,
            "YELLARE6"));
        var inspectTransitions = runtime.Apply(new MissionEvent(
            MissionEventKind.TargetInspected,
            "yellare5"));
        var extractTransitions = runtime.Apply(new MissionEvent(
            MissionEventKind.NavigationPointReached,
            "yellnav3"));

        Assert.Multiple(() =>
        {
            Assert.That(runtime.GetState(destroy.Id), Is.EqualTo(MissionObjectiveState.Completed));
            Assert.That(runtime.GetState(inspect.Id), Is.EqualTo(MissionObjectiveState.Completed));
            Assert.That(destroyTransitions.Select(transition => transition.Objective.Id),
                Is.EqualTo(new[] { destroy.Id }));
            Assert.That(inspectTransitions.Select(transition => transition.Objective.Id),
                Is.EqualTo(new[] { inspect.Id, extract.Id }));
            Assert.That(extractTransitions.Select(transition => transition.Objective.Id),
                Is.EqualTo(new[] { extract.Id }));
            Assert.That(runtime.IsComplete, Is.True);
            Assert.That(runtime.Outcome, Is.EqualTo(MissionOutcome.Successful));
        });
    }

    [Test]
    public void FailureResolvesTheAttemptAndIgnoresLaterEvents()
    {
        var definition = CreateDefinition();
        var runtime = new MissionRuntime(definition);

        var failed = runtime.Fail();
        var transitions = runtime.Apply(new MissionEvent(
            MissionEventKind.TargetDestroyed,
            "YELLARE6"));

        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.True);
            Assert.That(runtime.Outcome, Is.EqualTo(MissionOutcome.Failed));
            Assert.That(transitions, Is.Empty);
            Assert.That(runtime.GetState(definition.Objectives[0].Id), Is.EqualTo(MissionObjectiveState.Active));
            Assert.That(runtime.Fail(), Is.False);
        });
    }

    [Test]
    public void PrimaryObjectiveCompletesInAnyOrder()
    {
        var definition = CreateDefinition();
        var runtime = new MissionRuntime(definition);

        var transitions = runtime.Apply(new MissionEvent(
            MissionEventKind.TargetInspected,
            "yellare5"));

        Assert.Multiple(() =>
        {
            Assert.That(transitions.Select(transition => transition.Objective.Id),
                Is.EqualTo(new[] { definition.Objectives[1].Id }));
            Assert.That(runtime.GetState(definition.Objectives[1].Id), Is.EqualTo(MissionObjectiveState.Completed));
            Assert.That(runtime.GetState(definition.Objectives[2].Id), Is.EqualTo(MissionObjectiveState.Locked));
        });
    }

    [Test]
    public void AggregateObjectiveCompletesAfterEveryAuthoredTargetAndRemainsOptional()
    {
        var table = MechWarriorMissionTable.Load(MechWarriorMissionTableTests.CreateAggregateTableData());
        var definition = MissionDefinition.FromMissionTable(table);
        var runtime = new MissionRuntime(definition);
        var aggregate = definition.Objectives.Single(objective =>
            objective.Kind == MissionObjectiveKind.Aggregate);

        var firstTransitions = runtime.Apply(new MissionEvent(MissionEventKind.TargetDestroyed, "ENEMY1"));
        var secondTransitions = runtime.Apply(new MissionEvent(MissionEventKind.TargetDestroyed, "enemy2"));

        Assert.Multiple(() =>
        {
            Assert.That(aggregate.IsOptional, Is.True);
            Assert.That(aggregate.SuccessReport.Name, Is.EqualTo("gene002S"));
            Assert.That(aggregate.AggregateRequirements.Select(requirement => requirement.TargetResourceName),
                Is.EquivalentTo(new[] { "enemy1", "enemy2" }));
            Assert.That(firstTransitions.Select(transition => transition.Objective.Id),
                Does.Not.Contain(aggregate.Id));
            Assert.That(secondTransitions.Select(transition => transition.Objective.Id),
                Does.Contain(aggregate.Id));
            Assert.That(runtime.GetState(aggregate.Id), Is.EqualTo(MissionObjectiveState.Completed));
            Assert.That(runtime.Outcome, Is.EqualTo(MissionOutcome.Active),
                "Completing an optional objective must not bypass the required mission path.");
        });
    }

    [Test]
    public void AuthoredOuterMissionBoundaryFailsTheAttemptAfterItsWarningEvent()
    {
        var table = MechWarriorMissionTable.Load(MechWarriorMissionTableTests.CreateBoundaryTableData());
        var definition = MissionDefinition.FromMissionTable(table);
        var runtime = new MissionRuntime(definition);
        var innerBoundary = new MissionEvent(
            MissionEventKind.MissionAreaBoundaryExited,
            "yelllve1");
        var outerBoundary = new MissionEvent(
            MissionEventKind.MissionAreaBoundaryExited,
            "yelllve2");

        runtime.Apply(innerBoundary);
        var outcomeAfterWarning = runtime.Outcome;
        runtime.Apply(outerBoundary);
        var ignoredTransitions = runtime.Apply(new MissionEvent(
            MissionEventKind.TargetDestroyed,
            "yellare6"));

        Assert.Multiple(() =>
        {
            Assert.That(definition.EventReports.Select(report => report.Trigger),
                Does.Contain(innerBoundary));
            Assert.That(definition.EventReports.Select(report => report.Trigger),
                Does.Contain(outerBoundary));
            Assert.That(definition.EventReports.Single(report => report.Trigger == innerBoundary).Report.Name,
                Is.EqualTo("gene018S"));
            Assert.That(definition.EventReports.Single(report => report.Trigger == outerBoundary).Report.Name,
                Is.EqualTo("gene019S"));
            Assert.That(definition.FailureEvents, Does.Contain(outerBoundary));
            Assert.That(outcomeAfterWarning, Is.EqualTo(MissionOutcome.Active));
            Assert.That(runtime.Outcome, Is.EqualTo(MissionOutcome.Failed));
            Assert.That(ignoredTransitions, Is.Empty);
        });
    }

    private static MissionDefinition CreateDefinition()
    {
        var table = MechWarriorMissionTable.Load(MechWarriorMissionTableTests.CreateTableData());
        return MissionDefinition.FromMissionTable(table);
    }
}
