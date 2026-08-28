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
/// MW2 stores GPS definitions separately from deployments. Mission tables provide the authoritative
/// group-to-NAVP link where the NAVP's local group is not the deployed STAR group.
/// </remarks>
public static class MechWarriorMissionGamePieceLoader
{
    public static IReadOnlyList<MechWarriorMissionGamePiece> Load(
        MechWarriorProjectArchive archive,
        MechWarriorWorldFile scenario)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(scenario);

        var specifications = new List<(
            MechWarriorProjectEntry SourceEntry,
            MechWarriorWorldInclude Include,
            MechWarriorGamePieceSpecification Specification)>();
        var includedWorlds = new List<(
            MechWarriorProjectEntry Entry,
            MechWarriorWorldFile World)>();
        foreach (var include in scenario.Includes)
        {
            var entry = ResolveIncludeEntry(archive, include);
            if (entry == null)
            {
                continue;
            }

            var world = MechWarriorWorldFile.Load(archive.ReadEntry(entry), include.Transform);
            includedWorlds.Add((entry, world));
            specifications.AddRange(world.GamePieceSpecifications.Select(specification => (entry, include, specification)));
        }

        var specificationGroups = specifications
            .Select(item => item.Specification.GroupId)
            .ToHashSet();
        var spawnPointsByGroup = new Dictionary<int, MechWarriorWorldNavPoint>();
        foreach (var groupId in specificationGroups)
        {
            var deploymentTargets = scenario.MissionTables
                .Where(table => table.Index == groupId)
                .SelectMany(table => table.Entries)
                .Select(entry => entry.Target.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var authoredDeployments = includedWorlds
                .Where(item => deploymentTargets.Contains(Path.GetFileNameWithoutExtension(item.Entry.Name)))
                .SelectMany(item => item.World.NavPoints)
                .ToArray();
            if (authoredDeployments.Length > 1)
            {
                throw new InvalidDataException(
                    $"Scenario mission table {groupId} resolves multiple deployment points.");
            }

            if (authoredDeployments.Length == 1)
            {
                spawnPointsByGroup.Add(groupId, authoredDeployments[0]);
            }
        }

        foreach (var spawnPoint in includedWorlds
                     .SelectMany(item => item.World.NavPoints)
                     .Where(point => specificationGroups.Contains(point.GroupId)))
        {
            if (spawnPointsByGroup.ContainsKey(spawnPoint.GroupId))
            {
                continue;
            }

            if (!spawnPointsByGroup.TryAdd(spawnPoint.GroupId, spawnPoint))
            {
                throw new InvalidDataException(
                    $"Scenario contains multiple deployment points for game-piece group {spawnPoint.GroupId}.");
            }
        }

        var starsByGroup = scenario.Stars.ToDictionary(star => star.GroupId);
        var gamePieces = new List<MechWarriorMissionGamePiece>();
        foreach (var (sourceEntry, include, specification) in specifications)
        {
            if (!starsByGroup.TryGetValue(specification.GroupId, out var star))
            {
                throw new InvalidDataException(
                    $"{sourceEntry.Path} GPS group {specification.GroupId} has no STAR definition.");
            }

            if (!spawnPointsByGroup.TryGetValue(specification.GroupId, out var spawnPoint) &&
                !TryCreateIncludeDeployment(include, specification, out spawnPoint))
            {
                throw new InvalidDataException(
                    $"{sourceEntry.Path} GPS group {specification.GroupId} has no deployment linked " +
                    "through its mission table, group NAVP, or authored INCL transform.");
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

    private static bool TryCreateIncludeDeployment(
        MechWarriorWorldInclude include,
        MechWarriorGamePieceSpecification specification,
        out MechWarriorWorldNavPoint spawnPoint)
    {
        var transform = include.Transform;
        if (transform.Translation == System.Numerics.Vector3.Zero &&
            transform.RotationDegrees == System.Numerics.Vector3.Zero)
        {
            spawnPoint = null;
            return false;
        }

        // Some fixed mission game pieces are authored directly at their scenario INCL transform
        // instead of being joined to a separate NAVP.
        spawnPoint = new MechWarriorWorldNavPoint(
            transform.Translation,
            (int)MathF.Round(transform.RotationDegrees.Y),
            true,
            specification.GroupId,
            0,
            specification.ActionFlags,
            specification.DisplayName);
        return true;
    }

    private static MechWarriorProjectEntry ResolveEntry(
        MechWarriorProjectArchive archive,
        string directory,
        int resourceIndex,
        string resourceName) =>
        resourceIndex >= 0
            ? archive.GetEntry(directory, resourceIndex)
            : archive.GetEntry($"{directory}/{resourceName}.{directory}");

    private static MechWarriorProjectEntry ResolveIncludeEntry(
        MechWarriorProjectArchive archive,
        MechWarriorWorldInclude include) =>
        include.ResourceIndex >= 0
            ? archive.GetEntry("BWD", include.ResourceIndex)
            : archive.Entries.FirstOrDefault(entry => entry.Path.Equals(
                $"BWD/{include.Name}.BWD",
                StringComparison.OrdinalIgnoreCase));
}
