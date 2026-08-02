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
        IReadOnlyList<MechWarriorLevelObject> objects,
        IReadOnlyList<MechWarriorLevelActor> actors)
    {
        Sources = sources;
        Objects = objects;
        TerrainObjects = objects.Where(levelObject => levelObject.Kind == MechWarriorLevelObjectKind.Terrain).ToArray();
        SceneryObjects = objects.Where(levelObject => levelObject.Kind == MechWarriorLevelObjectKind.Scenery).ToArray();
        DebrisObjects = objects.Where(levelObject => levelObject.Kind == MechWarriorLevelObjectKind.Debris).ToArray();
        StaticObjects = objects.Where(levelObject => levelObject.Kind != MechWarriorLevelObjectKind.Actor).ToArray();
        Actors = actors;
    }

    public IReadOnlyList<MechWarriorLevelSource> Sources { get; }

    public IReadOnlyList<MechWarriorLevelObject> Objects { get; }

    public IReadOnlyList<MechWarriorLevelObject> TerrainObjects { get; }

    public IReadOnlyList<MechWarriorLevelObject> SceneryObjects { get; }

    public IReadOnlyList<MechWarriorLevelObject> DebrisObjects { get; }

    public IReadOnlyList<MechWarriorLevelObject> StaticObjects { get; }

    public IReadOnlyList<MechWarriorLevelActor> Actors { get; }

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
        var actors = new List<MechWarriorLevelActor>();
        var activeIncludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LoadSource(
            archive,
            archive.GetEntry(resourcePath),
            null,
            sources,
            objects,
            actors,
            activeIncludes,
            shouldFollowInclude);
        return new MechWarriorLevel(sources.AsReadOnly(), objects.AsReadOnly(), actors.AsReadOnly());
    }

    private static void LoadSource(
        MechWarriorProjectArchive archive,
        MechWarriorProjectEntry entry,
        MechWarriorWorldTransform parentTransform,
        ICollection<MechWarriorLevelSource> sources,
        ICollection<MechWarriorLevelObject> objects,
        ICollection<MechWarriorLevelActor> actors,
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
            var worldObjectsById = world.Objects.ToDictionary(worldObject => worldObject.Id);
            var activeActorObjectIds = new Dictionary<int, IReadOnlySet<int>>();
            var destroyedActorObjectIds = new Dictionary<int, IReadOnlySet<int>>();
            var claimedObjectIds = new HashSet<int>();
            var entityRootIds = world.Entities.Select(entity => entity.ObjectId).ToHashSet();
            foreach (var entity in world.Entities)
            {
                var activeIds = FindAssemblyObjectIds(entity.ObjectId, worldObjectsById, entityRootIds);
                activeActorObjectIds.Add(entity.ObjectId, activeIds);
                claimedObjectIds.UnionWith(activeIds);

                IReadOnlySet<int> destroyedIds = new HashSet<int>();
                if (entity.DestroyedObjectId.HasValue)
                {
                    destroyedIds = FindAssemblyObjectIds(
                        entity.DestroyedObjectId.Value,
                        worldObjectsById,
                        entityRootIds);
                    claimedObjectIds.UnionWith(destroyedIds);
                }

                destroyedActorObjectIds.Add(entity.ObjectId, destroyedIds);
            }

            var resolvedObjectsById = new Dictionary<int, MechWarriorLevelObject>();
            foreach (var worldObject in world.Objects)
            {
                var modelEntry = archive.GetEntry("POLY", worldObject.ModelResourceIndex);
                var kind = claimedObjectIds.Contains(worldObject.Id)
                    ? MechWarriorLevelObjectKind.Actor
                    : worldObject.RelativeToId == -1
                        ? MechWarriorLevelObjectKind.Debris
                        : modelEntry.Name.StartsWith("T_", StringComparison.OrdinalIgnoreCase)
                            ? MechWarriorLevelObjectKind.Terrain
                            : MechWarriorLevelObjectKind.Scenery;
                var levelObject = new MechWarriorLevelObject(
                    worldObject.Id,
                    worldObject.RelativeToId,
                    worldObject.CollisionType,
                    worldObject.ObjectType,
                    kind,
                    entry,
                    modelEntry,
                    worldObject.Transform);
                objects.Add(levelObject);
                resolvedObjectsById.Add(levelObject.Id, levelObject);
            }

            foreach (var entity in world.Entities)
            {
                actors.Add(new MechWarriorLevelActor(
                    entry,
                    entity.ObjectId,
                    entity.DestroyedObjectId,
                    entity.Health,
                    entity.Description,
                    ResolveObjects(activeActorObjectIds[entity.ObjectId], resolvedObjectsById),
                    ResolveObjects(destroyedActorObjectIds[entity.ObjectId], resolvedObjectsById)));
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
                    actors,
                    activeIncludes,
                    shouldFollowInclude);
            }
        }
        finally
        {
            activeIncludes.Remove(entry.Path);
        }
    }

    private static IReadOnlySet<int> FindAssemblyObjectIds(
        int rootObjectId,
        IReadOnlyDictionary<int, MechWarriorWorldObject> objectsById,
        IReadOnlySet<int> entityRootIds)
    {
        if (!objectsById.ContainsKey(rootObjectId))
        {
            throw new InvalidDataException($"BWD gameplay entity refers to missing object {rootObjectId}.");
        }

        var objectIds = new HashSet<int> { rootObjectId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var worldObject in objectsById.Values)
            {
                var isAnotherEntityRoot = worldObject.Id != rootObjectId && entityRootIds.Contains(worldObject.Id);
                if (!isAnotherEntityRoot &&
                    !objectIds.Contains(worldObject.Id) &&
                    objectIds.Contains(worldObject.RelativeToId))
                {
                    objectIds.Add(worldObject.Id);
                    changed = true;
                }
            }
        }

        return objectIds;
    }

    private static IReadOnlyList<MechWarriorLevelObject> ResolveObjects(
        IEnumerable<int> objectIds,
        IReadOnlyDictionary<int, MechWarriorLevelObject> objectsById) =>
        objectIds.Select(objectId => objectsById[objectId]).OrderBy(levelObject => levelObject.Id).ToArray();
}
