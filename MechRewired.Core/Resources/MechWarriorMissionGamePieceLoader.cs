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
/// Resolves authored scenario game pieces across included BWD resources.
/// </summary>
/// <remarks>
/// MW2 stores GPS definitions and their NAVP deployments separately; this loader joins them without mission-name conventions.
/// </remarks>
public static class MechWarriorMissionGamePieceLoader
{
    public static IReadOnlyList<MechWarriorMissionGamePiece> Load(
        MechWarriorProjectArchive archive,
        MechWarriorWorldFile scenario)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(scenario);

        var specifications = new List<(MechWarriorProjectEntry SourceEntry, MechWarriorGamePieceSpecification Specification)>();
        var includedWorlds = new List<MechWarriorWorldFile>();
        foreach (var include in scenario.Includes.Where(include => include.ResourceIndex >= 0))
        {
            var entry = archive.GetEntry("BWD", include.ResourceIndex);
            var world = MechWarriorWorldFile.Load(archive.ReadEntry(entry), include.Transform);
            includedWorlds.Add(world);
            specifications.AddRange(world.GamePieceSpecifications.Select(specification => (entry, specification)));
        }

        var specificationGroups = specifications
            .Select(item => item.Specification.GroupId)
            .ToHashSet();
        var spawnPointsByGroup = new Dictionary<int, MechWarriorWorldNavPoint>();
        foreach (var spawnPoint in includedWorlds
                     .SelectMany(world => world.NavPoints)
                     .Where(point => specificationGroups.Contains(point.GroupId)))
        {
            if (!spawnPointsByGroup.TryAdd(spawnPoint.GroupId, spawnPoint))
            {
                throw new InvalidDataException(
                    $"Scenario contains multiple deployment points for game-piece group {spawnPoint.GroupId}.");
            }
        }

        var starsByGroup = scenario.Stars.ToDictionary(star => star.GroupId);
        var gamePieces = new List<MechWarriorMissionGamePiece>();
        foreach (var (sourceEntry, specification) in specifications)
        {
            if (!starsByGroup.TryGetValue(specification.GroupId, out var star))
            {
                throw new InvalidDataException(
                    $"{sourceEntry.Path} GPS group {specification.GroupId} has no STAR definition.");
            }

            if (!spawnPointsByGroup.TryGetValue(specification.GroupId, out var spawnPoint))
            {
                throw new InvalidDataException(
                    $"{sourceEntry.Path} GPS group {specification.GroupId} has no deployment NAVP.");
            }

            gamePieces.Add(new MechWarriorMissionGamePiece(
                sourceEntry,
                specification,
                star,
                spawnPoint,
                ResolveEntry(archive, "BWD", specification.ChassisResourceIndex, specification.ChassisName),
                ResolveEntry(archive, "MEK", specification.MechResourceIndex, specification.ConfigurationName)));
        }

        return gamePieces.AsReadOnly();
    }

    private static MechWarriorProjectEntry ResolveEntry(
        MechWarriorProjectArchive archive,
        string directory,
        int resourceIndex,
        string resourceName) =>
        resourceIndex >= 0
            ? archive.GetEntry(directory, resourceIndex)
            : archive.GetEntry($"{directory}/{resourceName}.{directory}");
}
