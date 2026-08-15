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
/// Resolves the resources that make up an original MW2 mission from its scenario BWD.
/// </summary>
/// <remarks>
/// Mission scenarios name their direct world includes with a shared mission prefix: for example,
/// <c>YELLSCN1</c> includes <c>YELLWLD1</c>, <c>YELLPLT1</c>, <c>YELLST01</c> and <c>YELLNAV*</c>.
/// The resolver keeps that convention in one validated place instead of scattering a mission's resource paths
/// through the runtime composition code.
/// </remarks>
public sealed class MechWarriorMissionResources
{
    private MechWarriorMissionResources(
        string missionPrefix,
        MechWarriorProjectEntry scenarioEntry,
        MechWarriorWorldFile scenario,
        MechWarriorProjectEntry paletteEntry,
        MechWarriorMissionResource level,
        MechWarriorMissionResource planet,
        MechWarriorMissionResource playerStart,
        IReadOnlyList<MechWarriorMissionResource> navigationPoints)
    {
        MissionPrefix = missionPrefix;
        ScenarioEntry = scenarioEntry;
        Scenario = scenario;
        PaletteEntry = paletteEntry;
        Level = level;
        Planet = planet;
        PlayerStart = playerStart;
        NavigationPoints = navigationPoints;
    }

    /// <summary>The shared mission resource prefix, such as <c>YELL</c>.</summary>
    public string MissionPrefix { get; }

    public MechWarriorProjectEntry ScenarioEntry { get; }

    public MechWarriorWorldFile Scenario { get; }

    public MechWarriorProjectEntry PaletteEntry { get; }

    public MechWarriorMissionResource Level { get; }

    public MechWarriorMissionResource Planet { get; }

    public MechWarriorMissionResource PlayerStart { get; }

    public IReadOnlyList<MechWarriorMissionResource> NavigationPoints { get; }

    /// <summary>The named area resources that belong to this mission's playable terrain world.</summary>
    public string AreaPrefix => MissionPrefix + "ARE";

    /// <summary>
    /// Loads a scenario and resolves its direct mission-resource references.
    /// </summary>
    public static MechWarriorMissionResources Load(
        MechWarriorProjectArchive archive,
        string scenarioPath)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioPath);

        var scenarioEntry = archive.GetEntry(scenarioPath);
        if (!scenarioEntry.DirectoryName.Equals("BWD", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Mission scenario {scenarioEntry.Path} must be a BWD resource.");
        }

        var missionPrefix = GetMissionPrefix(scenarioEntry.Name);
        var scenario = MechWarriorWorldFile.Load(archive.ReadEntry(scenarioEntry));
        // Scenario BWDs can carry placeholder INCL tags with a negative archive index. They are not resource
        // references and must remain out of the resolver rather than failing the whole mission at startup.
        var includes = scenario.Includes
            .Where(include => include.ResourceIndex >= 0)
            .Select(include => new MechWarriorMissionResource(
                archive.GetEntry("BWD", include.ResourceIndex),
                include))
            .ToArray();

        var level = FindSingle(includes, resource =>
            IsNamed(resource.Entry.Name, missionPrefix, "WLD"), "battlefield world", scenarioEntry);
        var planet = FindSingle(includes, resource =>
            IsNamed(resource.Entry.Name, missionPrefix, "PLT"), "planet world", scenarioEntry);
        var playerStart = FindPlayerStart(archive, includes, missionPrefix, scenarioEntry);
        var navigationPoints = includes
            .Where(resource => IsNumbered(resource.Entry.Name, missionPrefix, "NAV"))
            .ToArray();
        if (navigationPoints.Length == 0)
        {
            throw new InvalidDataException(
                $"{scenarioEntry.Path} contains no {missionPrefix}NAV mission-resource includes.");
        }

        var paletteEntry = archive.GetEntry($"PAL/{missionPrefix}_DA.COL");
        return new MechWarriorMissionResources(
            missionPrefix,
            scenarioEntry,
            scenario,
            paletteEntry,
            level,
            planet,
            playerStart,
            navigationPoints);
    }

    /// <summary>Gets a mission prefix from an MW2 scenario resource name.</summary>
    public static string GetMissionPrefix(string scenarioName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);
        var baseName = Path.GetFileNameWithoutExtension(scenarioName);
        var scenarioMarker = baseName.LastIndexOf("SCN", StringComparison.OrdinalIgnoreCase);
        if (scenarioMarker <= 0 || scenarioMarker == baseName.Length - 3)
        {
            throw new InvalidDataException(
                $"Mission scenario {scenarioName} does not have the expected <prefix>SCN<number> name.");
        }

        if (!baseName[(scenarioMarker + 3)..].All(char.IsAsciiDigit))
        {
            throw new InvalidDataException(
                $"Mission scenario {scenarioName} does not have a numeric scenario suffix.");
        }

        return baseName[..scenarioMarker];
    }

    private static MechWarriorMissionResource FindSingle(
        IReadOnlyList<MechWarriorMissionResource> resources,
        Func<MechWarriorMissionResource, bool> predicate,
        string description,
        MechWarriorProjectEntry scenarioEntry)
    {
        var matches = resources.Where(predicate).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException(
                $"{scenarioEntry.Path} contains {matches.Length} {description} includes; expected exactly one.");
    }

    private static MechWarriorMissionResource FindPlayerStart(
        MechWarriorProjectArchive archive,
        IReadOnlyList<MechWarriorMissionResource> resources,
        string missionPrefix,
        MechWarriorProjectEntry scenarioEntry)
    {
        // A scenario has one numbered start world for the player plus additional numbered start worlds used by
        // its game-piece groups. Group zero is the player deployment convention encoded by their NAVP records.
        var playerStarts = resources
            .Where(resource => IsNumbered(resource.Entry.Name, missionPrefix, "ST"))
            .Where(resource => MechWarriorWorldFile
                .Load(archive.ReadEntry(resource.Entry), resource.Include.Transform)
                .NavPoints
                .Any(point => point.GroupId == 0))
            .ToArray();
        return playerStarts.Length == 1
            ? playerStarts[0]
            : throw new InvalidDataException(
                $"{scenarioEntry.Path} contains {playerStarts.Length} group-zero player-start worlds; " +
                "expected exactly one.");
    }

    private static bool IsNamed(string resourceName, string prefix, string kind) =>
        Path.GetFileNameWithoutExtension(resourceName).StartsWith(
            prefix + kind,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsNumbered(string resourceName, string prefix, string kind)
    {
        var name = Path.GetFileNameWithoutExtension(resourceName);
        var stem = prefix + kind;
        return name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) &&
               name[stem.Length..].All(char.IsAsciiDigit);
    }
}

/// <summary>An original BWD resource and its authored scenario include transform.</summary>
public sealed record MechWarriorMissionResource(
    MechWarriorProjectEntry Entry,
    MechWarriorWorldInclude Include);
