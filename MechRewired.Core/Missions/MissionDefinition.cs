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

namespace MechRewired.Missions;

/// <summary>Builds player objectives and their dependency graph from an original MW2 mission table.</summary>
public sealed class MissionDefinition
{
    private const byte PrimaryGoal = 0x01;
    private const byte SecondaryGoal = 0x02;
    private const byte TertiaryGoal = 0x04;
    private const byte ReturnGoal = 0x08;

    private MissionDefinition(
        int tableIndex,
        int timeLimitSeconds,
        MechWarriorMissionResourceReference successReport,
        MechWarriorMissionResourceReference failureReport,
        IReadOnlyList<MissionObjectiveDefinition> objectives,
        IReadOnlyList<MissionEventReportDefinition> eventReports,
        IReadOnlyList<MissionEvent> failureEvents,
        IReadOnlySet<int> consumedEntryIndices)
    {
        TableIndex = tableIndex;
        TimeLimitSeconds = timeLimitSeconds;
        SuccessReport = successReport;
        FailureReport = failureReport;
        Objectives = objectives;
        EventReports = eventReports;
        FailureEvents = failureEvents;
        ConsumedEntryIndices = consumedEntryIndices;
    }

    public int TableIndex { get; }

    /// <summary>Original mission duration in seconds; -1 means unlimited.</summary>
    public int TimeLimitSeconds { get; }

    public MechWarriorMissionResourceReference SuccessReport { get; }

    public MechWarriorMissionResourceReference FailureReport { get; }

    public IReadOnlyList<MissionObjectiveDefinition> Objectives { get; }

    /// <summary>One-shot reports attached to direct hidden or navigation records.</summary>
    public IReadOnlyList<MissionEventReportDefinition> EventReports { get; }

    /// <summary>Direct gameplay events that activate an authored MTBL mission-failure control.</summary>
    public IReadOnlyList<MissionEvent> FailureEvents { get; }

    /// <summary>MTBL records represented either as player objectives or their internal dependency records.</summary>
    public IReadOnlySet<int> ConsumedEntryIndices { get; }

    /// <summary>Returns authored table-zero records still outside the player mission runtime.</summary>
    public static IReadOnlyList<MissionObjectiveExclusion> GetExcludedEntries(MechWarriorMissionTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var definition = FromMissionTable(table);
        return table.Entries
            .Where(entry => !definition.ConsumedEntryIndices.Contains(entry.Index))
            .Select(entry => new MissionObjectiveExclusion(entry, GetExclusionReason(entry)))
            .ToArray();
    }

    public static MissionDefinition FromMissionTable(MechWarriorMissionTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var consumed = new HashSet<int>();
        var objectiveEntries = table.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Description) &&
                            TryGetKind(entry, out _) &&
                            TryGetOptional(entry, out _))
            .ToArray();
        var idsByEntry = objectiveEntries.ToDictionary(
            entry => entry.Index,
            entry => $"mtbl-{table.Index}-{entry.Index}");
        var requiredNonExtractionIds = objectiveEntries
            .Where(entry => TryGetKind(entry, out var kind) &&
                            kind != MissionObjectiveKind.Extract &&
                            TryGetOptional(entry, out var isOptional) &&
                            !isOptional)
            .Select(entry => idsByEntry[entry.Index])
            .ToArray();
        var objectives = new List<MissionObjectiveDefinition>();

        foreach (var entry in objectiveEntries)
        {
            consumed.Add(entry.Index);
            TryGetKind(entry, out var kind);
            TryGetOptional(entry, out var isOptional);
            IReadOnlyList<string> prerequisites = Array.Empty<string>();
            if (!isOptional && kind == MissionObjectiveKind.Extract)
            {
                var authored = ResolveExtractionPrerequisites(table, entry, idsByEntry, consumed);
                prerequisites = authored.Count == 0 ? requiredNonExtractionIds : authored;
            }

            var aggregateRequirements = kind == MissionObjectiveKind.Aggregate
                ? ResolveAggregateRequirements(table, entry, consumed)
                : Array.Empty<MissionEvent>();
            objectives.Add(new MissionObjectiveDefinition(
                idsByEntry[entry.Index],
                entry.Description,
                kind,
                entry.Target.Name,
                isOptional,
                prerequisites,
                entry.SuccessReport,
                aggregateRequirements));
        }

        var completionReportIndices = objectiveEntries
            .Where(entry => TryGetKind(entry, out var kind) &&
                            kind is MissionObjectiveKind.Destroy or MissionObjectiveKind.Inspect)
            .Select(entry => entry.Index)
            .ToHashSet();
        var eventReports = new List<MissionEventReportDefinition>();
        foreach (var entry in table.Entries.Where(entry =>
                     entry.SuccessReport.ResourceIndex.HasValue &&
                     !completionReportIndices.Contains(entry.Index)))
        {
            if (!TryGetMissionEvent(entry, out var trigger))
            {
                continue;
            }

            consumed.Add(entry.Index);
            eventReports.Add(new MissionEventReportDefinition(trigger, entry.SuccessReport));
        }

        var failureEvents = ResolveFailureEvents(table, consumed);

        return new MissionDefinition(
            table.Index,
            table.MissionTimeSeconds,
            table.SuccessReport,
            table.FailureReport,
            objectives.AsReadOnly(),
            eventReports.AsReadOnly(),
            failureEvents,
            consumed);
    }

    private static IReadOnlyList<MissionEvent> ResolveFailureEvents(
        MechWarriorMissionTable table,
        ISet<int> consumed)
    {
        var events = new List<MissionEvent>();
        foreach (var entry in table.Entries.Where(entry =>
                     entry.ControlAction == MechWarriorMissionControlAction.FailMission))
        {
            var resolved = false;
            foreach (var condition in entry.ActivationConditions.Where(condition =>
                         condition.TableIndex == table.Index &&
                         condition.Result == MechWarriorMissionConditionResult.Completed &&
                         condition.ObjectiveIndex < table.Entries.Count))
            {
                var dependency = table.Entries[condition.ObjectiveIndex];
                if (!TryGetMissionEvent(dependency, out var failureEvent))
                {
                    continue;
                }

                consumed.Add(dependency.Index);
                events.Add(failureEvent);
                resolved = true;
            }

            if (resolved)
            {
                consumed.Add(entry.Index);
            }
        }

        return events
            .DistinctBy(GetEventKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ResolveExtractionPrerequisites(
        MechWarriorMissionTable table,
        MechWarriorMissionTableEntry extraction,
        IReadOnlyDictionary<int, string> idsByEntry,
        ISet<int> consumed)
    {
        var showRecords = table.Entries.Where(entry =>
            entry.ControlAction == MechWarriorMissionControlAction.ShowObjective &&
            entry.TargetObjectiveIndex == extraction.Index).ToArray();
        MechWarriorMissionTableEntry[] sources = showRecords.Length == 0 ? [extraction] : showRecords;
        var prerequisites = new List<string>();
        foreach (var source in sources)
        {
            consumed.Add(source.Index);
            CollectObjectiveDependencies(table, source, idsByEntry, consumed, prerequisites, new HashSet<int>());
        }

        return prerequisites.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void CollectObjectiveDependencies(
        MechWarriorMissionTable table,
        MechWarriorMissionTableEntry source,
        IReadOnlyDictionary<int, string> idsByEntry,
        ISet<int> consumed,
        ICollection<string> prerequisites,
        ISet<int> visiting)
    {
        if (!visiting.Add(source.Index))
        {
            return;
        }

        foreach (var condition in source.ActivationConditions.Where(condition => condition.TableIndex == table.Index))
        {
            if (condition.ObjectiveIndex >= table.Entries.Count)
            {
                continue;
            }

            var dependency = table.Entries[condition.ObjectiveIndex];
            consumed.Add(dependency.Index);
            if (idsByEntry.TryGetValue(dependency.Index, out var id))
            {
                prerequisites.Add(id);
            }
            else
            {
                CollectObjectiveDependencies(table, dependency, idsByEntry, consumed, prerequisites, visiting);
            }
        }

        visiting.Remove(source.Index);
    }

    private static IReadOnlyList<MissionEvent> ResolveAggregateRequirements(
        MechWarriorMissionTable table,
        MechWarriorMissionTableEntry objective,
        ISet<int> consumed)
    {
        var succeedRecords = table.Entries.Where(entry =>
            entry.ControlAction == MechWarriorMissionControlAction.SucceedObjective &&
            entry.TargetObjectiveIndex == objective.Index).ToArray();
        MechWarriorMissionTableEntry[] sources = succeedRecords.Length == 0 ? [objective] : succeedRecords;
        var requirements = new List<MissionEvent>();
        foreach (var source in sources)
        {
            consumed.Add(source.Index);
            CollectAggregateRequirements(table, source, consumed, requirements, new HashSet<int>());
        }

        return requirements
            .DistinctBy(requirement => $"{requirement.Kind}:{requirement.TargetResourceName}",
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CollectAggregateRequirements(
        MechWarriorMissionTable table,
        MechWarriorMissionTableEntry source,
        ISet<int> consumed,
        ICollection<MissionEvent> requirements,
        ISet<int> visiting)
    {
        if (!visiting.Add(source.Index))
        {
            return;
        }

        foreach (var condition in source.ActivationConditions.Where(condition => condition.TableIndex == table.Index))
        {
            if (condition.ObjectiveIndex >= table.Entries.Count)
            {
                continue;
            }

            var dependency = table.Entries[condition.ObjectiveIndex];
            consumed.Add(dependency.Index);
            if (TryGetMissionEvent(dependency, out var requirement))
            {
                requirements.Add(requirement);
            }
            else if (dependency.Action != MechWarriorMissionAction.Begin)
            {
                CollectAggregateRequirements(table, dependency, consumed, requirements, visiting);
            }
        }

        visiting.Remove(source.Index);
    }

    private static bool TryGetMissionEvent(MechWarriorMissionTableEntry entry, out MissionEvent missionEvent)
    {
        var kind = entry.Action switch
        {
            MechWarriorMissionAction.Destroy => (MissionEventKind?)MissionEventKind.TargetDestroyed,
            MechWarriorMissionAction.Recon => MissionEventKind.TargetInspected,
            MechWarriorMissionAction.GoTo or MechWarriorMissionAction.Return =>
                MissionEventKind.NavigationPointReached,
            MechWarriorMissionAction.Leave => MissionEventKind.MissionAreaBoundaryExited,
            _ => null
        };
        if (kind.HasValue && !string.IsNullOrWhiteSpace(entry.Target.Name))
        {
            missionEvent = new MissionEvent(kind.Value, entry.Target.Name);
            return true;
        }

        missionEvent = null;
        return false;
    }

    private static string GetEventKey(MissionEvent missionEvent) =>
        $"{missionEvent.Kind}:{missionEvent.TargetResourceName}";

    private static bool TryGetKind(MechWarriorMissionTableEntry entry, out MissionObjectiveKind kind)
    {
        kind = entry.Action switch
        {
            MechWarriorMissionAction.Recon => MissionObjectiveKind.Inspect,
            MechWarriorMissionAction.Destroy => MissionObjectiveKind.Destroy,
            MechWarriorMissionAction.GoTo or MechWarriorMissionAction.Return =>
                (entry.GoalFlags & ReturnGoal) != 0
                    ? MissionObjectiveKind.Extract
                    : MissionObjectiveKind.ReachNavigationPoint,
            MechWarriorMissionAction.Wait => MissionObjectiveKind.Aggregate,
            _ => default
        };
        return entry.Action is MechWarriorMissionAction.Recon or
            MechWarriorMissionAction.Destroy or
            MechWarriorMissionAction.GoTo or
            MechWarriorMissionAction.Return or
            MechWarriorMissionAction.Wait;
    }

    private static bool TryGetOptional(MechWarriorMissionTableEntry entry, out bool isOptional)
    {
        if (entry.GoalClass == 'M' && (entry.GoalFlags & (PrimaryGoal | ReturnGoal)) != 0)
        {
            isOptional = false;
            return true;
        }

        if (entry.GoalClass == 'O' && (entry.GoalFlags & (SecondaryGoal | TertiaryGoal)) != 0)
        {
            isOptional = true;
            return true;
        }

        isOptional = false;
        return false;
    }

    private static string GetExclusionReason(MechWarriorMissionTableEntry entry)
    {
        if (entry.Action == MechWarriorMissionAction.Begin)
        {
            return "Initial deployment record is not yet evaluated by the player mission runtime.";
        }

        if (entry.ControlAction != MechWarriorMissionControlAction.None)
        {
            return $"Mission-control action 0x{(ushort)entry.ControlAction:X4} is not yet evaluated.";
        }

        return $"{entry.Action} record is not represented by a player objective or dependency.";
    }
}

/// <summary>Explains why an authored MTBL record did not become part of the runtime graph.</summary>
public sealed record MissionObjectiveExclusion(MechWarriorMissionTableEntry Entry, string Reason);

/// <summary>An archive-authored report played the first time a direct MTBL event occurs.</summary>
public sealed record MissionEventReportDefinition(
    MissionEvent Trigger,
    MechWarriorMissionResourceReference Report);
