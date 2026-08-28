// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Resources;

/// <summary>Resolves nested positive-health BWD actors into destruction-propagation links.</summary>
public static class MechWarriorActorDestructionLinkResolver
{
    public static IReadOnlyList<MechWarriorActorDestructionLink> Resolve(MechWarriorLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var links = new List<MechWarriorActorDestructionLink>();
        foreach (var source in level.Sources)
        {
            var actors = level.Actors
                .Where(actor => actor.SourceEntry.Path.Equals(
                    source.Entry.Path,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var activeComponentOwners = actors
                .SelectMany(actor => actor.Components.Select(component => (component.Id, Actor: actor)))
                .ToDictionary(item => item.Id, item => item.Actor);
            var objectsById = source.World.Objects.ToDictionary(worldObject => worldObject.Id);
            foreach (var child in actors)
            {
                if (!objectsById.TryGetValue(child.ObjectId, out var childRoot))
                {
                    throw new InvalidDataException(
                        $"{source.Entry.Path} actor {child.ObjectId} has no matching OBJ root.");
                }

                var ancestorId = childRoot.RelativeToId;
                while (ancestorId >= 0)
                {
                    if (activeComponentOwners.TryGetValue(ancestorId, out var parent) &&
                        !ReferenceEquals(parent, child))
                    {
                        links.Add(new MechWarriorActorDestructionLink(parent, child));
                        break;
                    }

                    if (!objectsById.TryGetValue(ancestorId, out var ancestor))
                    {
                        throw new InvalidDataException(
                            $"{source.Entry.Path} actor {child.ObjectId} refers to missing ancestor OBJ {ancestorId}.");
                    }

                    ancestorId = ancestor.RelativeToId;
                }
            }
        }

        return links.AsReadOnly();
    }
}
