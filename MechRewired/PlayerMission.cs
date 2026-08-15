// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Godot;
using MechRewired.Missions;
using MechRewired.Resources;

namespace MechRewired;

/// <summary>
/// Connects host gameplay events and original report audio to the deterministic mission runtime.
/// </summary>
/// <remarks>
/// Rendering and input can evolve independently while objective evaluation remains in the core assembly.
/// </remarks>
public partial class PlayerMission : Node
{
    private const float SuccessReportDelaySeconds = 2.1f;
    private const float StatusMessageSeconds = 4.0f;

    private readonly MissionRuntime m_runtime;
    private readonly IReadOnlyDictionary<string, AudioStreamWav> m_completionReports;
    private readonly IReadOnlyList<AudioStreamWav> m_extractionReadyReports;
    private readonly AudioStreamWav m_successReport;
    private readonly AudioStreamWav m_failureReport;
    private readonly AudioStreamPlayer m_reportPlayer;
    private readonly Queue<AudioStreamWav> m_reportQueue = new();
    private bool m_completionReported;
    private float m_statusMessageRemaining;

    public PlayerMission(MechWarriorProjectArchive archive, MissionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(definition);
        Name = "PlayerMission";
        m_runtime = new MissionRuntime(definition);
        m_completionReports = LoadCompletionReports(archive, definition);
        m_successReport = LoadReport(
            archive,
            definition.SuccessReport,
            "mission success report");
        m_failureReport = LoadReport(
            archive,
            definition.FailureReport,
            "mission failure report");
        m_extractionReadyReports =
        [
            PlayerMechSounds.LoadResource(
                archive,
                "SNDS/GENE014S.SFL",
                false,
                "all primary objectives complete report"),
            PlayerMechSounds.LoadResource(
                archive,
                "SNDS/GENE015S.SFL",
                false,
                "proceed to dust-off zone report")
        ];
        m_reportPlayer = new AudioStreamPlayer
        {
            Name = "ObjectiveReport"
        };
        m_reportPlayer.Finished += PlayNextQueuedReport;
        AddChild(m_reportPlayer);
        foreach (var objective in definition.Objectives.Where(objective =>
                     m_runtime.GetState(objective.Id) == MissionObjectiveState.Active))
        {
            GD.Print($"MechRewired: activated objective '{objective.Description}'.");
        }
    }

    public void Apply(MissionEvent missionEvent)
    {
        foreach (var transition in m_runtime.Apply(missionEvent))
        {
            if (transition.State == MissionObjectiveState.Completed)
            {
                StatusMessage = "OBJECTIVE COMPLETE";
                m_statusMessageRemaining = StatusMessageSeconds;
                GD.Print(
                    $"MechRewired: objective complete: '{transition.Objective.Description}' " +
                    $"from {missionEvent.Kind} on {missionEvent.TargetResourceName}.");
                if (m_completionReports.TryGetValue(transition.Objective.Id, out var report))
                {
                    QueueReport(report);
                }
            }
            else
            {
                GD.Print($"MechRewired: activated objective '{transition.Objective.Description}'.");
                if (transition.Objective.Kind == MissionObjectiveKind.Extract)
                {
                    foreach (var report in m_extractionReadyReports)
                    {
                        QueueReport(report);
                    }
                }
            }
        }

        if (m_runtime.IsComplete && !m_completionReported)
        {
            m_completionReported = true;
            StatusMessage = "MISSION COMPLETE";
            m_statusMessageRemaining = StatusMessageSeconds;
            GD.Print("MechRewired: all required mission objectives complete.");
            MissionCompleted?.Invoke();
            MissionResolved?.Invoke(MissionOutcome.Successful);
            if (m_successReport != null)
            {
                var timer = GetTree().CreateTimer(SuccessReportDelaySeconds);
                timer.Timeout += () =>
                {
                    QueueReport(m_successReport);
                };
            }
        }
    }

    public string StatusMessage { get; private set; } = string.Empty;

    public MissionOutcome Outcome => m_runtime.Outcome;

    public IReadOnlyList<MissionObjectiveDefinition> Objectives => m_runtime.Definition.Objectives;

    /// <summary>
    /// Raised once when all required data-driven mission objectives complete.
    /// </summary>
    public event Action MissionCompleted;

    /// <summary>Raised once after either the decoded objectives succeed or the player attempt fails.</summary>
    public event Action<MissionOutcome> MissionResolved;

    /// <summary>Fails the current attempt and plays its archive-authored failure report where present.</summary>
    public bool Fail()
    {
        if (!m_runtime.Fail())
        {
            return false;
        }

        StatusMessage = "MISSION FAILED";
        m_statusMessageRemaining = StatusMessageSeconds;
        GD.Print("MechRewired: mission failed before all required objectives completed.");
        QueueReport(m_failureReport);
        MissionResolved?.Invoke(MissionOutcome.Failed);
        return true;
    }

    public MissionObjectiveState GetState(string objectiveId) => m_runtime.GetState(objectiveId);

    public bool IsActiveObjectiveTarget(BattlefieldActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return m_runtime.Definition.Objectives.Any(objective =>
            m_runtime.GetState(objective.Id) == MissionObjectiveState.Active &&
            string.Equals(
                objective.TargetResourceName,
                actor.SourceResourceName,
                StringComparison.OrdinalIgnoreCase) &&
            objective.Kind switch
            {
                MissionObjectiveKind.Destroy => actor.IsDamageable && !actor.IsDestroyed,
                MissionObjectiveKind.Inspect => !actor.IsDestroyed,
                _ => false
            });
    }

    public MissionObjectiveKind? GetActiveObjectiveKind(BattlefieldActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return m_runtime.Definition.Objectives
            .Where(objective =>
                m_runtime.GetState(objective.Id) == MissionObjectiveState.Active &&
                string.Equals(
                    objective.TargetResourceName,
                    actor.SourceResourceName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(objective => (MissionObjectiveKind?)objective.Kind)
            .FirstOrDefault();
    }

    public override void _Process(double delta)
    {
        if (m_statusMessageRemaining <= 0.0f)
        {
            return;
        }

        m_statusMessageRemaining -= (float)delta;
        if (m_statusMessageRemaining <= 0.0f)
        {
            StatusMessage = string.Empty;
        }
    }

    private static IReadOnlyDictionary<string, AudioStreamWav> LoadCompletionReports(
        MechWarriorProjectArchive archive,
        MissionDefinition definition)
    {
        var reports = new Dictionary<string, AudioStreamWav>(StringComparer.OrdinalIgnoreCase);
        foreach (var objective in definition.Objectives.Where(objective =>
                     objective.Kind is MissionObjectiveKind.Destroy or MissionObjectiveKind.Inspect &&
                     objective.SuccessReport.ResourceIndex.HasValue))
        {
            reports.Add(
                objective.Id,
                LoadReport(
                    archive,
                    objective.SuccessReport,
                    $"objective completion report for '{objective.Description}'"));
        }

        return reports;
    }

    private static AudioStreamWav LoadReport(
        MechWarriorProjectArchive archive,
        MechWarriorMissionResourceReference report,
        string purpose)
    {
        if (!report.ResourceIndex.HasValue || string.IsNullOrWhiteSpace(report.Name))
        {
            return null;
        }

        var reportEntry = archive.GetEntry("SNDS", report.ResourceIndex.Value);
        var expectedName = $"{report.Name}.SFL";
        if (!string.Equals(reportEntry.Name, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Mission report {report.Name} refers to SNDS index {report.ResourceIndex}, " +
                $"but the archive contains {reportEntry.Name}.");
        }

        return PlayerMechSounds.LoadResource(
            archive,
            reportEntry.Path,
            false,
            purpose);
    }

    private void QueueReport(AudioStreamWav report)
    {
        if (report == null)
        {
            return;
        }

        m_reportQueue.Enqueue(report);
        if (!m_reportPlayer.Playing)
        {
            PlayNextQueuedReport();
        }
    }

    private void PlayNextQueuedReport()
    {
        if (m_reportQueue.Count == 0)
        {
            return;
        }

        m_reportPlayer.Stream = m_reportQueue.Dequeue();
        m_reportPlayer.Play();
    }
}
