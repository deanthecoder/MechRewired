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

/// <summary>The broad authored terrain family used to select renderer-only surface treatment.</summary>
public enum MechWarriorTerrainBiome
{
    Desert,
    RockyMountain
}

/// <summary>
/// Resolves the renderer biome from the original level resource hierarchy rather than campaign choice.
/// </summary>
public static class MechWarriorTerrainBiomeResolver
{
    public static MechWarriorTerrainBiome Resolve(IEnumerable<string> sourceNames)
    {
        ArgumentNullException.ThrowIfNull(sourceNames);
        return sourceNames.Any(sourceName =>
                   Path.GetFileNameWithoutExtension(sourceName)
                       .Contains("MTN", StringComparison.OrdinalIgnoreCase))
            ? MechWarriorTerrainBiome.RockyMountain
            : MechWarriorTerrainBiome.Desert;
    }
}
