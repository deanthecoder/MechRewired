// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code or as a compiled binary, for any purpose.
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Globalization;

namespace MechRewired.Resources;

/// <summary>Describes how an original type-5 BWD path task behaves after reaching its final point.</summary>
public enum MechWarriorWorldPathPlayback
{
    OneShot,
    Repeat,
    Loop
}

/// <summary>Resolves a type-5 BWD task to its target object and authored point table.</summary>
/// <remarks>
/// The command is authored as <c>&lt;object&gt;;&lt;playback&gt;,&lt;rotation&gt;,&lt;path&gt;</c>.
/// <c>repeat</c> resets to the first point after the final point, while <c>loop</c> uses the
/// final point's authored duration to travel continuously back to the first point.
/// </remarks>
public sealed record MechWarriorWorldPathTask(
    MechWarriorWorldTask SourceTask,
    int MotionObjectId,
    MechWarriorWorldObject MotionObject,
    MechWarriorWorldPathPlayback Playback,
    bool RotateWithPath,
    MechWarriorWorldPathTable Path)
{
    public static bool TryResolve(
        MechWarriorWorldFile world,
        MechWarriorWorldTask task,
        out MechWarriorWorldPathTask pathTask,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(task);
        pathTask = null;
        if (task.Type != 5)
        {
            error = $"Task type {task.Type} is not a path task.";
            return false;
        }

        var separator = task.Command.IndexOf(';');
        if (separator <= 0 ||
            !int.TryParse(task.Command.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId))
        {
            error = $"Path command '{task.Command}' has no numeric object target.";
            return false;
        }

        var arguments = task.Command[(separator + 1)..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (arguments.Length != 3)
        {
            error = $"Path command '{task.Command}' must provide playback, rotation, and PTBL name.";
            return false;
        }

        if (!TryParsePlayback(arguments[0], out var playback))
        {
            error = $"Path playback '{arguments[0]}' in '{task.Command}' is not implemented.";
            return false;
        }

        if (!TryParseRotation(arguments[1], out var rotateWithPath))
        {
            error = $"Path rotation mode '{arguments[1]}' in '{task.Command}' is not implemented.";
            return false;
        }

        var motionObject = world.Objects.FirstOrDefault(candidate => candidate.Id == objectId);
        if (motionObject == null)
        {
            error = $"Path command '{task.Command}' refers to missing object {objectId}.";
            return false;
        }

        var path = world.PathTables.FirstOrDefault(candidate =>
            candidate.Name.Equals(arguments[2], StringComparison.OrdinalIgnoreCase));
        if (path == null || path.Points.Count == 0)
        {
            error = $"Path command '{task.Command}' has no matching non-empty PTBL '{arguments[2]}'.";
            return false;
        }

        pathTask = new MechWarriorWorldPathTask(
            task,
            objectId,
            motionObject,
            playback,
            rotateWithPath,
            path);
        error = string.Empty;
        return true;
    }

    private static bool TryParsePlayback(string value, out MechWarriorWorldPathPlayback playback)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "oneshot":
                playback = MechWarriorWorldPathPlayback.OneShot;
                return true;
            case "repeat":
                playback = MechWarriorWorldPathPlayback.Repeat;
                return true;
            case "loop":
                playback = MechWarriorWorldPathPlayback.Loop;
                return true;
            default:
                playback = default;
                return false;
        }
    }

    private static bool TryParseRotation(string value, out bool rotateWithPath)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "rotate":
                rotateWithPath = true;
                return true;
            case "norotate":
                rotateWithPath = false;
                return true;
            default:
                rotateWithPath = false;
                return false;
        }
    }
}
