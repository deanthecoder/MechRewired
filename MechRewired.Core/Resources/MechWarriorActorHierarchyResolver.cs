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

/// <summary>Resolves every BWD actor to the authored root of its destruction assembly.</summary>
public static class MechWarriorActorHierarchyResolver
{
    /// <summary>
    /// Maps an actor to itself when it has no destructible parent, otherwise to its highest ancestor.
    /// </summary>
    public static IReadOnlyDictionary<MechWarriorLevelActor, MechWarriorLevelActor> ResolveRoots(
        MechWarriorLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        var parentByChild = MechWarriorActorDestructionLinkResolver.Resolve(level)
            .ToDictionary(link => link.Child, link => link.Parent);
        var roots = new Dictionary<MechWarriorLevelActor, MechWarriorLevelActor>();
        foreach (var actor in level.Actors)
        {
            var root = actor;
            var ancestors = new HashSet<MechWarriorLevelActor>();
            while (parentByChild.TryGetValue(root, out var parent))
            {
                if (!ancestors.Add(root))
                {
                    throw new InvalidDataException(
                        $"BWD actor hierarchy contains a cycle at {actor.SourceEntry.Path} object {actor.ObjectId}.");
                }

                root = parent;
            }

            roots.Add(actor, root);
        }

        return roots.AsReadOnly();
    }
}
