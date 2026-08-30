// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Resources;

namespace MechRewired.Missions;

/// <summary>Classifies a mission-fidelity discrepancy without coupling archive decoding to Godot.</summary>
public enum MissionFidelityFindingKind
{
    UnknownBwdTag,
    UnsupportedTask,
    ExcludedMissionTableRecord,
    MissingRuntimeContent,
    PartialSupport,
    ProceduralFallback,
    ReservedSetPiece,
    MissingMaterialMapping
}

public enum MissionFidelitySeverity { Info, Warning }

/// <summary>A machine-readable finding tied to an original resource and authored record identity.</summary>
public sealed record MissionFidelityFinding(
    MissionFidelityFindingKind Kind,
    MissionFidelitySeverity Severity,
    string SourceResource,
    string Identity,
    string Reason);

/// <summary>Records the content that was actually instantiated by a mission startup.</summary>
public sealed class MissionRuntimeContent
{
    private readonly HashSet<string> m_combatants = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_actorActiveRepresentations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_actorDestroyedRepresentations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_navigationPoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_objectives = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_aircraft = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> m_effects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MissionFidelityFinding> m_reportedFindings = [];

    public IReadOnlySet<string> Combatants => m_combatants;
    public IReadOnlySet<string> ActorActiveRepresentations => m_actorActiveRepresentations;
    public IReadOnlySet<string> ActorDestroyedRepresentations => m_actorDestroyedRepresentations;
    public IReadOnlySet<string> NavigationPoints => m_navigationPoints;
    public IReadOnlySet<string> Objectives => m_objectives;
    public IReadOnlySet<string> Aircraft => m_aircraft;
    public IReadOnlySet<string> Effects => m_effects;
    public IReadOnlyList<MissionFidelityFinding> ReportedFindings => m_reportedFindings;

    public void AddCombatant(MechWarriorMissionGamePiece gamePiece) =>
        m_combatants.Add(GamePieceKey(gamePiece));

    public void AddActorRepresentation(MechWarriorLevelActor actor, bool destroyed) =>
        (destroyed ? m_actorDestroyedRepresentations : m_actorActiveRepresentations).Add(ActorKey(actor));

    public void AddNavigationPoint(MechWarriorMissionNavigationPoint point) =>
        m_navigationPoints.Add(point.ResourceName);

    public void AddObjective(MissionObjectiveDefinition objective) =>
        m_objectives.Add(objective.Id);

    public void AddAircraft(MechWarriorLevelActor actor) =>
        m_aircraft.Add(ActorKey(actor));

    public void AddEffect(MechWarriorLevelObject effect) =>
        m_effects.Add(ObjectKey(effect.SourceEntry.Path, effect.Id));

    public void Report(MissionFidelityFindingKind kind, string sourceResource, string identity, string reason) =>
        m_reportedFindings.Add(new MissionFidelityFinding(
            kind, MissionFidelitySeverity.Warning, sourceResource, identity, reason));

    public void ReportInfo(MissionFidelityFindingKind kind, string sourceResource, string identity, string reason) =>
        m_reportedFindings.Add(new MissionFidelityFinding(
            kind, MissionFidelitySeverity.Info, sourceResource, identity, reason));

    internal static string ActorKey(MechWarriorLevelActor actor) => ObjectKey(actor.SourceEntry.Path, actor.ObjectId);
    internal static string ObjectKey(string sourceResource, int objectId) => $"{sourceResource}#{objectId}";
    internal static string GamePieceKey(MechWarriorMissionGamePiece gamePiece) =>
        $"{gamePiece.SourceEntry.Path}#{gamePiece.Specification.GroupId}#{gamePiece.ConfigurationEntry.Path}";
}

/// <summary>
/// Reconciles original mission records with the runtime inventory and emits concise, actionable findings.
/// </summary>
public sealed class MissionFidelityAudit
{
    private MissionFidelityAudit(IReadOnlyList<MissionFidelityFinding> findings)
    {
        Findings = findings;
    }

    public IReadOnlyList<MissionFidelityFinding> Findings { get; }
    public int WarningCount => Findings.Count(finding => finding.Severity == MissionFidelitySeverity.Warning);

    public static MissionFidelityAudit Analyze(
        MechWarriorMissionResources resources,
        MechWarriorLevel level,
        IReadOnlyList<MechWarriorMissionNavigationPoint> navigationPoints,
        MissionDefinition definition,
        IReadOnlyList<MechWarriorMissionGamePiece> gamePieces,
        MissionRuntimeContent runtime)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(navigationPoints);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(gamePieces);
        ArgumentNullException.ThrowIfNull(runtime);
        var findings = new List<MissionFidelityFinding>();

        findings.AddRange(resources.Scenario.UnknownTags.Select(tag => new MissionFidelityFinding(
            MissionFidelityFindingKind.UnknownBwdTag,
            IsStructuralTag(tag.Name) ? MissionFidelitySeverity.Info : MissionFidelitySeverity.Warning,
            resources.ScenarioEntry.Path,
            $"{tag.Name}@0x{tag.Offset:X}",
            $"Tag is {tag.Size} bytes and has no semantic decoder.")));

        foreach (var source in level.Sources)
        {
            findings.AddRange(source.World.UnknownTags.Select(tag => new MissionFidelityFinding(
                MissionFidelityFindingKind.UnknownBwdTag,
                IsStructuralTag(tag.Name) ? MissionFidelitySeverity.Info : MissionFidelitySeverity.Warning,
                source.Entry.Path,
                $"{tag.Name}@0x{tag.Offset:X}",
                $"Tag is {tag.Size} bytes and has no semantic decoder.")));
            foreach (var (task, index) in source.World.Tasks.Select((task, index) => (task, index)))
            {
                if (!TryValidateTask(source.World, task, out var reason))
                {
                    findings.Add(new MissionFidelityFinding(
                        MissionFidelityFindingKind.UnsupportedTask,
                        MissionFidelitySeverity.Warning,
                        source.Entry.Path,
                        $"TSK[{index}] type {task.Type}",
                        reason));
                }
            }
        }

        foreach (var exclusion in MissionDefinition.GetExcludedEntries(
                     resources.Scenario.MissionTables.SingleOrDefault(table => table.Index == definition.TableIndex) ??
                     resources.Scenario.MissionTables.First()))
        {
            findings.Add(new MissionFidelityFinding(
                MissionFidelityFindingKind.ExcludedMissionTableRecord,
                MissionFidelitySeverity.Warning,
                resources.ScenarioEntry.Path,
                $"MTBL {definition.TableIndex} record {exclusion.Entry.Index}",
                exclusion.Reason));
        }

        AddMissing(
            findings,
            gamePieces.Where(piece => piece.Star.Disposition == MechWarriorMissionDisposition.Hostile)
                .Select(MissionRuntimeContent.GamePieceKey),
            runtime.Combatants,
            MissionFidelityFindingKind.MissingRuntimeContent,
            resources.ScenarioEntry.Path,
            "hostile game piece",
            "GPS/STAR hostile game piece was not instantiated as a combatant.");
        AddMissing(
            findings,
            level.Actors.Where(actor => actor.Components.Any(component =>
                !component.ModelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase)))
                .Select(MissionRuntimeContent.ActorKey),
            runtime.ActorActiveRepresentations,
            MissionFidelityFindingKind.MissingRuntimeContent,
            resources.Level.Entry.Path,
            "actor",
            "Authored active actor representation was not instantiated.");
        AddMissing(
            findings,
            level.Actors.Where(actor => actor.DestroyedComponents.Any(component =>
                !component.ModelEntry.Name.StartsWith("DUMMY", StringComparison.OrdinalIgnoreCase)))
                .Select(MissionRuntimeContent.ActorKey),
            runtime.ActorDestroyedRepresentations,
            MissionFidelityFindingKind.MissingRuntimeContent,
            resources.Level.Entry.Path,
            "destroyed actor",
            "Authored destroyed actor representation was not instantiated.");
        AddMissing(
            findings,
            navigationPoints.Select(point => point.ResourceName),
            runtime.NavigationPoints,
            MissionFidelityFindingKind.MissingRuntimeContent,
            resources.ScenarioEntry.Path,
            "navigation point",
            "Authored navigation point was not configured at runtime.");
        AddMissing(
            findings,
            definition.Objectives.Select(objective => objective.Id),
            runtime.Objectives,
            MissionFidelityFindingKind.MissingRuntimeContent,
            resources.ScenarioEntry.Path,
            "objective",
            "Supported MTBL objective was not configured at runtime.");
        AddMissing(
            findings,
            level.EffectObjects.Select(effect => MissionRuntimeContent.ObjectKey(effect.SourceEntry.Path, effect.Id)),
            runtime.Effects,
            MissionFidelityFindingKind.MissingRuntimeContent,
            resources.Level.Entry.Path,
            "effect",
            "Authored effect object was not instantiated.");

        foreach (var source in level.Sources.Where(source => HasTaskArgument(source.World, "recon")))
        {
            try
            {
                foreach (var plan in MechWarriorAuthoredAircraftResolver.Resolve(level)
                             .Where(plan => plan.Source.Entry.Path.Equals(source.Entry.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    var key = MissionRuntimeContent.ActorKey(plan.Actor);
                    if (!runtime.Aircraft.Contains(key))
                    {
                        findings.Add(new MissionFidelityFinding(
                            MissionFidelityFindingKind.MissingRuntimeContent,
                            MissionFidelitySeverity.Warning,
                            source.Entry.Path,
                            key,
                            "Authored recon aircraft path was not activated at runtime."));
                    }
                }
            }
            catch (InvalidDataException exception)
            {
                findings.Add(new MissionFidelityFinding(
                    MissionFidelityFindingKind.PartialSupport,
                    MissionFidelitySeverity.Warning,
                    source.Entry.Path,
                    "recon task",
                    exception.Message));
            }
        }

        findings.AddRange(runtime.ReportedFindings);
        return new MissionFidelityAudit(findings.AsReadOnly());
    }

    private static void AddMissing(
        ICollection<MissionFidelityFinding> findings,
        IEnumerable<string> authored,
        IReadOnlySet<string> runtime,
        MissionFidelityFindingKind kind,
        string source,
        string identityPrefix,
        string reason)
    {
        foreach (var identity in authored.Distinct(StringComparer.OrdinalIgnoreCase).Where(identity => !runtime.Contains(identity)))
        {
            findings.Add(new MissionFidelityFinding(kind, MissionFidelitySeverity.Warning, source, $"{identityPrefix} {identity}", reason));
        }
    }

    private static bool HasTaskArgument(MechWarriorWorldFile world, string argument) =>
        world.Tasks.Any(task => task.Command.Split([';', ','], StringSplitOptions.TrimEntries)
            .Any(candidate => candidate.Equals(argument, StringComparison.OrdinalIgnoreCase)));

    private static bool IsStructuralTag(string tagName) =>
        tagName is "MOFF" or "MON" or "BLK" or "ENDB";

    private static bool TryValidateTask(
        MechWarriorWorldFile world,
        MechWarriorWorldTask task,
        out string reason)
    {
        if (task.Type is not (1 or 4 or 5))
        {
            reason = $"Task type {task.Type} is not implemented.";
            return false;
        }

        var separator = task.Command.IndexOf(';');
        if (separator <= 0 || !int.TryParse(task.Command.AsSpan(0, separator), out _))
        {
            reason = $"Task type {task.Type} command '{task.Command}' has no numeric object target.";
            return false;
        }

        if (task.Type == 5 && !MechWarriorWorldPathTask.TryResolve(
                world, task, out _, out reason))
        {
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
