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

    private static MissionDefinition CreateDefinition()
    {
        var table = MechWarriorMissionTable.Load(MechWarriorMissionTableTests.CreateTableData());
        return MissionDefinition.FromMissionTable(table);
    }
}
