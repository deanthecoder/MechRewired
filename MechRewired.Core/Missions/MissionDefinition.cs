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

/// <summary>
/// Builds reusable objective definitions from an original MW2 mission table.
/// </summary>
/// <remarks>
/// The first decoder recognizes verified Pyre Light trigger and goal flags while retaining raw table data separately.
/// </remarks>
public sealed class MissionDefinition
{
    private const int DestroyTrigger = 0x0002;
    private const int InspectTrigger = 0x0008;
    private const int NavigationTrigger = 0x0100;
    private const byte PrimaryGoal = 0x01;
    private const byte SecondaryGoal = 0x02;
    private const byte ReturnGoal = 0x08;

    private MissionDefinition(
        int tableIndex,
        MechWarriorMissionResourceReference successReport,
        MechWarriorMissionResourceReference failureReport,
        IReadOnlyList<MissionObjectiveDefinition> objectives)
    {
        TableIndex = tableIndex;
        SuccessReport = successReport;
        FailureReport = failureReport;
        Objectives = objectives;
    }

    public int TableIndex { get; }

    public MechWarriorMissionResourceReference SuccessReport { get; }

    public MechWarriorMissionResourceReference FailureReport { get; }

    public IReadOnlyList<MissionObjectiveDefinition> Objectives { get; }

    /// <summary>Returns authored records deliberately excluded because this objective decoder cannot represent them.</summary>
    public static IReadOnlyList<MissionObjectiveExclusion> GetExcludedEntries(MechWarriorMissionTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Entries
            .Select(entry => (Entry: entry, Reason: GetExclusionReason(entry)))
            .Where(candidate => candidate.Reason != null)
            .Select(candidate => new MissionObjectiveExclusion(candidate.Entry, candidate.Reason))
            .ToArray();
    }

    public static MissionDefinition FromMissionTable(MechWarriorMissionTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var objectives = new List<MissionObjectiveDefinition>();
        var requiredNonExtractionIds = new List<string>();
        foreach (var entry in table.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Description) ||
                string.IsNullOrWhiteSpace(entry.Target.Name) ||
                !TryGetKind(entry, out var kind) ||
                !TryGetOptional(entry, out var isOptional))
            {
                continue;
            }

            var id = $"mtbl-{table.Index}-{entry.Index}";
            IReadOnlyList<string> prerequisites = Array.Empty<string>();
            if (!isOptional && kind == MissionObjectiveKind.Extract)
            {
                prerequisites = requiredNonExtractionIds.ToArray();
            }

            objectives.Add(new MissionObjectiveDefinition(
                id,
                entry.Description,
                kind,
                entry.Target.Name,
                isOptional,
                prerequisites,
                entry.SuccessReport));
            if (!isOptional && kind != MissionObjectiveKind.Extract)
            {
                requiredNonExtractionIds.Add(id);
            }
        }

        return new MissionDefinition(
            table.Index,
            table.SuccessReport,
            table.FailureReport,
            objectives.AsReadOnly());
    }

    private static bool TryGetKind(MechWarriorMissionTableEntry entry, out MissionObjectiveKind kind)
    {
        if ((entry.TriggerFlags & InspectTrigger) != 0)
        {
            kind = MissionObjectiveKind.Inspect;
            return true;
        }

        if ((entry.TriggerFlags & DestroyTrigger) != 0)
        {
            kind = MissionObjectiveKind.Destroy;
            return true;
        }

        if ((entry.TriggerFlags & NavigationTrigger) != 0)
        {
            kind = (entry.GoalFlags & ReturnGoal) != 0
                ? MissionObjectiveKind.Extract
                : MissionObjectiveKind.ReachNavigationPoint;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TryGetOptional(MechWarriorMissionTableEntry entry, out bool isOptional)
    {
        if (entry.GoalClass == 'M' && (entry.GoalFlags & (PrimaryGoal | ReturnGoal)) != 0)
        {
            isOptional = false;
            return true;
        }

        if (entry.GoalClass == 'O' && (entry.GoalFlags & SecondaryGoal) != 0)
        {
            isOptional = true;
            return true;
        }

        isOptional = false;
        return false;
    }

    private static string GetExclusionReason(MechWarriorMissionTableEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Description)) return "Record has no objective description.";
        if (string.IsNullOrWhiteSpace(entry.Target.Name)) return "Record has no target resource.";
        if (!TryGetKind(entry, out _)) return $"Trigger flags 0x{entry.TriggerFlags:X} are not supported.";
        if (!TryGetOptional(entry, out _))
            return $"Goal class '{entry.GoalClass}' with flags 0x{entry.GoalFlags:X2} is not supported.";
        return null;
    }
}

/// <summary>Explains why an authored MTBL record did not become a runtime objective.</summary>
public sealed record MissionObjectiveExclusion(MechWarriorMissionTableEntry Entry, string Reason);
