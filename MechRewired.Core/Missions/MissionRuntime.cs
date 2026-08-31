// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Missions;

/// <summary>
/// Evaluates gameplay events against a data-driven mission objective graph.
/// </summary>
/// <remarks>
/// The runtime is deterministic and host-independent so mission behavior can be tested without Godot.
/// </remarks>
public sealed class MissionRuntime
{
    private readonly Dictionary<string, MissionObjectiveState> m_states;
    private readonly HashSet<string> m_observedEvents = new(StringComparer.OrdinalIgnoreCase);

    public MissionRuntime(MissionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        m_states = definition.Objectives.ToDictionary(
            objective => objective.Id,
            objective => objective.PrerequisiteIds.Count == 0
                ? MissionObjectiveState.Active
                : MissionObjectiveState.Locked,
            StringComparer.OrdinalIgnoreCase);
    }

    public MissionDefinition Definition { get; }

    public MissionOutcome Outcome { get; private set; }

    public bool IsComplete => Outcome == MissionOutcome.Successful;

    public bool IsResolved => Outcome != MissionOutcome.Active;

    public MissionObjectiveState GetState(string objectiveId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
        if (!m_states.TryGetValue(objectiveId, out var state))
        {
            throw new KeyNotFoundException($"Mission objective '{objectiveId}' was not found.");
        }

        return state;
    }

    public IReadOnlyList<MissionObjectiveTransition> Apply(MissionEvent missionEvent)
    {
        ArgumentNullException.ThrowIfNull(missionEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(missionEvent.TargetResourceName);
        if (IsResolved)
        {
            return Array.Empty<MissionObjectiveTransition>();
        }

        m_observedEvents.Add(GetEventKey(missionEvent));
        if (Definition.FailureEvents.Any(failureEvent =>
                string.Equals(
                    GetEventKey(failureEvent),
                    GetEventKey(missionEvent),
                    StringComparison.OrdinalIgnoreCase)))
        {
            Outcome = MissionOutcome.Failed;
            return Array.Empty<MissionObjectiveTransition>();
        }

        var transitions = new List<MissionObjectiveTransition>();
        foreach (var objective in Definition.Objectives.Where(objective =>
                     GetState(objective.Id) == MissionObjectiveState.Active &&
                     IsSatisfiedBy(objective, missionEvent)))
        {
            m_states[objective.Id] = MissionObjectiveState.Completed;
            transitions.Add(new MissionObjectiveTransition(
                objective,
                MissionObjectiveState.Active,
                MissionObjectiveState.Completed));
        }

        foreach (var objective in Definition.Objectives.Where(objective =>
                     GetState(objective.Id) == MissionObjectiveState.Locked &&
                     objective.PrerequisiteIds.All(prerequisiteId =>
                         GetState(prerequisiteId) == MissionObjectiveState.Completed)))
        {
            m_states[objective.Id] = MissionObjectiveState.Active;
            transitions.Add(new MissionObjectiveTransition(
                objective,
                MissionObjectiveState.Locked,
                MissionObjectiveState.Active));
        }

        if (Definition.Objectives
            .Where(objective => !objective.IsOptional)
            .All(objective => GetState(objective.Id) == MissionObjectiveState.Completed))
        {
            Outcome = MissionOutcome.Successful;
        }

        return transitions.AsReadOnly();
    }

    /// <summary>Fails this attempt without changing the decoded objective definition.</summary>
    public bool Fail()
    {
        if (IsResolved)
        {
            return false;
        }

        Outcome = MissionOutcome.Failed;
        return true;
    }

    private bool IsSatisfiedBy(MissionObjectiveDefinition objective, MissionEvent missionEvent)
    {
        if (objective.Kind == MissionObjectiveKind.Aggregate)
        {
            return objective.AggregateRequirements.Count > 0 &&
                   objective.AggregateRequirements.All(requirement =>
                       m_observedEvents.Contains(GetEventKey(requirement)));
        }

        if (!string.Equals(
                objective.TargetResourceName,
                missionEvent.TargetResourceName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return objective.Kind switch
        {
            MissionObjectiveKind.Destroy => missionEvent.Kind == MissionEventKind.TargetDestroyed,
            MissionObjectiveKind.Inspect => missionEvent.Kind == MissionEventKind.TargetInspected,
            MissionObjectiveKind.ReachNavigationPoint or MissionObjectiveKind.Extract =>
                missionEvent.Kind == MissionEventKind.NavigationPointReached,
            _ => false
        };
    }

    private static string GetEventKey(MissionEvent missionEvent) =>
        $"{missionEvent.Kind}:{missionEvent.TargetResourceName}";
}
