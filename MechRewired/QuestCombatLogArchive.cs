// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, compile, or distribute this software.

using Godot;
using MechRewired.Diagnostics;

namespace MechRewired;

/// <summary>Archives the tagged combat benchmark stream independently of logcat.</summary>
/// <remarks>Writes are buffered; call Flush at explicit checkpoints outside measured windows.</remarks>
public static class QuestCombatLogArchive
{
    private const string SharedMirrorRoot = "/storage/emulated/0/Download/MechRewired/benchmarks";
    private const string AdbMirrorRoot = "/storage/emulated/0/Android/data/uk.co.deanthecoder.mechrewired/files/benchmarks";
    private static readonly object Gate = new();
    private static DurableTaggedLogStore s_primary;
    private static DurableTaggedLogStore s_sharedMirror;
    private static DurableTaggedLogStore s_adbMirror;
    private static string s_runId;
    private static string s_primaryPath;
    private static string s_primaryError;
    private static string s_mirrorError;

    public static string PrimaryPath { get { lock (Gate) return s_primaryPath; } }
    public static string PrimaryError { get { lock (Gate) return s_primaryError; } }
    public static string MirrorError { get { lock (Gate) return s_mirrorError; } }

    /// <summary>Starts an archive. The private user:// copy is required; Android mirrors are best effort.</summary>
    public static bool BeginRun(string runId)
    {
        lock (Gate)
        {
            CloseStores();
            s_runId = runId;
            s_primaryError = null;
            s_mirrorError = null;
            try
            {
                s_primary = new DurableTaggedLogStore(ProjectSettings.GlobalizePath("user://benchmarks/combat"));
                s_primary.BeginRun(runId);
                s_primaryPath = s_primary.ActivePath;
            }
            catch (Exception exception)
            {
                s_primaryError = exception.ToString();
                CloseStores();
                GD.PushError("QUEST_COMBAT_ARCHIVE_PRIMARY_FAILED: " + exception.Message);
                return false;
            }

            if (OS.HasFeature("android"))
            {
                TryBeginMirror(AdbMirrorRoot, "ADB-readable app storage", ref s_adbMirror);
                TryBeginMirror(SharedMirrorRoot, "Downloads mirror", ref s_sharedMirror);
            }
            GD.Print($"QUEST_COMBAT_ARCHIVE: runId={s_runId} path={s_primaryPath} adbMirror={(s_adbMirror != null ? "ready" : "unavailable")} downloadsMirror={(s_sharedMirror != null ? "ready" : "unavailable")}");
            return true;
        }
    }

    /// <summary>Appends a complete tagged line to the primary archive and any available mirrors.</summary>
    public static bool AppendTaggedLine(string line)
    {
        lock (Gate)
        {
            if (s_primary == null)
            {
                s_primaryError ??= "BeginRun was not called.";
                GD.PushError("QUEST_COMBAT_ARCHIVE_PRIMARY_FAILED: " + s_primaryError);
                return false;
            }
            try { s_primary.AppendLine(line); }
            catch (Exception exception)
            {
                s_primaryError = exception.ToString();
                CloseStores();
                GD.PushError("QUEST_COMBAT_ARCHIVE_PRIMARY_FAILED: " + exception.Message);
                return false;
            }
            AppendMirror(s_adbMirror, line, "ADB-readable app storage");
            AppendMirror(s_sharedMirror, line, "Downloads mirror");
            return true;
        }
    }

    /// <summary>Flushes all buffered records at a logical checkpoint after timing has stopped.</summary>
    public static bool Flush()
    {
        lock (Gate)
        {
            if (s_primary == null) return s_primaryError == null;
            try { s_primary.Flush(); }
            catch (Exception exception)
            {
                s_primaryError = exception.ToString();
                CloseStores();
                GD.PushError("QUEST_COMBAT_ARCHIVE_PRIMARY_FAILED: " + exception.Message);
                return false;
            }
            FlushMirror(s_adbMirror, "ADB-readable app storage");
            FlushMirror(s_sharedMirror, "Downloads mirror");
            return true;
        }
    }

    public static void EndRun()
    {
        lock (Gate)
        {
            Flush();
            CloseStores();
            s_runId = null;
        }
    }

    private static void TryBeginMirror(string path, string label, ref DurableTaggedLogStore store)
    {
        try
        {
            store = new DurableTaggedLogStore(path);
            store.BeginRun(s_runId);
        }
        catch (Exception exception)
        {
            try { store?.Dispose(); } catch { }
            store = null;
            WarnMirror(label, exception);
        }
    }

    private static void AppendMirror(DurableTaggedLogStore store, string line, string label)
    {
        if (store == null) return;
        try { store.AppendLine(line); }
        catch (Exception exception) { DisableMirror(store, label, exception); }
    }

    private static void FlushMirror(DurableTaggedLogStore store, string label)
    {
        if (store == null) return;
        try { store.Flush(); }
        catch (Exception exception) { DisableMirror(store, label, exception); }
    }

    private static void DisableMirror(DurableTaggedLogStore store, string label, Exception exception)
    {
        WarnMirror(label, exception);
        if (ReferenceEquals(store, s_adbMirror)) s_adbMirror = null;
        if (ReferenceEquals(store, s_sharedMirror)) s_sharedMirror = null;
        try { store.Dispose(); } catch { }
    }

    private static void WarnMirror(string label, Exception exception)
    {
        s_mirrorError = (s_mirrorError == null ? "" : s_mirrorError + " | ") + label + ": " + exception;
        GD.PushWarning($"QUEST_COMBAT_ARCHIVE_MIRROR_UNAVAILABLE: {label}: {exception.Message} (primary file remains at {s_primaryPath})");
    }

    private static void CloseStores()
    {
        DisposeStore(ref s_primary);
        DisposeStore(ref s_adbMirror);
        DisposeStore(ref s_sharedMirror);
    }

    private static void DisposeStore(ref DurableTaggedLogStore store)
    {
        try { store?.Dispose(); } catch { /* Existing primary/mirror error remains authoritative. */ }
        store = null;
    }
}
