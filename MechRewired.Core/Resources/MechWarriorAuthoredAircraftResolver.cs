// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Globalization;

namespace MechRewired.Resources;

/// <summary>Validates and resolves BWD path/audio tasks into playable aircraft plans.</summary>
public static class MechWarriorAuthoredAircraftResolver
{
    public static IReadOnlyList<MechWarriorAuthoredAircraftPlan> Resolve(MechWarriorLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var plans = new List<MechWarriorAuthoredAircraftPlan>();
        foreach (var source in level.Sources.Where(source => HasTaskArgument(source.World, "recon")))
        {
            var pathTask = source.World.Tasks.FirstOrDefault(task => task.Type == 5);
            if (pathTask == null || !TryReadTaskTarget(pathTask, out var motionObjectId, out var pathArguments))
            {
                throw new InvalidDataException(
                    $"{source.Entry.Path} declares recon behavior without a valid path task.");
            }

            var path = source.World.PathTables.FirstOrDefault(candidate =>
                pathArguments.Any(argument => argument.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)));
            var motionObject = source.World.Objects.FirstOrDefault(candidate => candidate.Id == motionObjectId);
            if (path == null || motionObject == null || path.Points.Count == 0)
            {
                throw new InvalidDataException(
                    $"{source.Entry.Path} path task '{pathTask.Command}' has no matching PTBL or motion object.");
            }

            var soundTask = source.World.Tasks.FirstOrDefault(task => task.Type == 4);
            if (soundTask == null ||
                !TryReadTaskTarget(soundTask, out var soundObjectId, out var soundArguments) ||
                soundArguments.Length < 2)
            {
                throw new InvalidDataException(
                    $"{source.Entry.Path} recon path has no valid authored sound task.");
            }

            var maximumSoundDistance = float.TryParse(
                soundArguments[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var authoredDistance)
                ? authoredDistance
                : throw new InvalidDataException(
                    $"{source.Entry.Path} sound task '{soundTask.Command}' has an invalid distance.");
            var actor = level.Actors.FirstOrDefault(candidate =>
                candidate.Health > 0 &&
                candidate.SourceEntry.Path.Equals(source.Entry.Path, StringComparison.OrdinalIgnoreCase) &&
                candidate.Components.Any(component => component.Id == soundObjectId));
            if (actor == null)
            {
                throw new InvalidDataException(
                    $"{source.Entry.Path} recon path has no damageable actor owning sound object {soundObjectId}.");
            }

            var rotor = actor.Components.FirstOrDefault(component => component.Id != soundObjectId);
            if (rotor == null)
            {
                throw new InvalidDataException(
                    $"{source.Entry.Path} recon actor {actor.ObjectId} has no subordinate rotor component.");
            }

            plans.Add(new MechWarriorAuthoredAircraftPlan(
                source,
                actor,
                motionObject,
                path,
                pathTask,
                pathArguments.Any(argument =>
                    argument.Equals("rotate", StringComparison.OrdinalIgnoreCase)),
                soundObjectId,
                maximumSoundDistance,
                soundArguments[1],
                soundArguments.Length >= 3 && soundArguments[2] != "0",
                rotor));
        }

        return plans.AsReadOnly();
    }

    private static bool HasTaskArgument(MechWarriorWorldFile world, string argument) =>
        world.Tasks.Any(task =>
            task.Command.Split([';', ','], StringSplitOptions.TrimEntries)
                .Any(candidate => candidate.Equals(argument, StringComparison.OrdinalIgnoreCase)));

    private static bool TryReadTaskTarget(
        MechWarriorWorldTask task,
        out int objectId,
        out string[] arguments)
    {
        var separator = task.Command.IndexOf(';');
        if (separator <= 0 ||
            !int.TryParse(task.Command.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId))
        {
            objectId = -1;
            arguments = Array.Empty<string>();
            return false;
        }

        arguments = task.Command[(separator + 1)..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return true;
    }
}
