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

/// <summary>
/// Loads a hierarchy of BWD resources into positioned level model instances.
/// </summary>
/// <remarks>
/// This first slice resolves includes and static geometry; mission behavior remains a later layer.
/// </remarks>
public sealed class MechWarriorLevel
{
    private MechWarriorLevel(
        IReadOnlyList<MechWarriorLevelSource> sources,
        IReadOnlyList<MechWarriorLevelObject> objects)
    {
        Sources = sources;
        Objects = objects;
    }

    public IReadOnlyList<MechWarriorLevelSource> Sources { get; }

    public IReadOnlyList<MechWarriorLevelObject> Objects { get; }

    /// <summary>
    /// Loads a BWD world and recursively follows its included BWD resources.
    /// </summary>
    public static MechWarriorLevel Load(
        MechWarriorProjectArchive archive,
        string resourcePath,
        Func<MechWarriorWorldInclude, bool> shouldFollowInclude = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);

        var sources = new List<MechWarriorLevelSource>();
        var objects = new List<MechWarriorLevelObject>();
        var activeIncludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LoadSource(
            archive,
            archive.GetEntry(resourcePath),
            null,
            sources,
            objects,
            activeIncludes,
            shouldFollowInclude);
        return new MechWarriorLevel(sources.AsReadOnly(), objects.AsReadOnly());
    }

    private static void LoadSource(
        MechWarriorProjectArchive archive,
        MechWarriorProjectEntry entry,
        MechWarriorWorldTransform parentTransform,
        ICollection<MechWarriorLevelSource> sources,
        ICollection<MechWarriorLevelObject> objects,
        ISet<string> activeIncludes,
        Func<MechWarriorWorldInclude, bool> shouldFollowInclude)
    {
        if (!activeIncludes.Add(entry.Path))
        {
            throw new InvalidDataException($"BWD include cycle detected at {entry.Path}.");
        }

        try
        {
            var world = MechWarriorWorldFile.Load(archive.ReadEntry(entry), parentTransform);
            sources.Add(new MechWarriorLevelSource(entry, world.Objects.Count));
            foreach (var worldObject in world.Objects)
            {
                var modelEntry = archive.GetEntry("POLY", worldObject.ModelResourceIndex);
                objects.Add(new MechWarriorLevelObject(worldObject.Id, modelEntry, worldObject.Transform));
            }

            foreach (var include in world.Includes)
            {
                if (shouldFollowInclude != null && !shouldFollowInclude(include))
                {
                    continue;
                }

                LoadSource(
                    archive,
                    archive.GetEntry("BWD", include.ResourceIndex),
                    include.Transform,
                    sources,
                    objects,
                    activeIncludes,
                    shouldFollowInclude);
            }
        }
        finally
        {
            activeIncludes.Remove(entry.Path);
        }
    }
}
