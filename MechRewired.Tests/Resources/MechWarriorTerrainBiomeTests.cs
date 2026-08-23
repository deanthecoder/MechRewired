// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

[TestFixture]
public sealed class MechWarriorTerrainBiomeTests
{
    [Test]
    public void MountainSourceSelectsRockyMountainBiome()
    {
        var biome = MechWarriorTerrainBiomeResolver.Resolve([
            "PINKWLD1.BWD",
            "PINKMTN1.BWD",
            "PINKARE1.BWD"
        ]);

        Assert.That(biome, Is.EqualTo(MechWarriorTerrainBiome.RockyMountain));
    }

    [Test]
    public void TiledTerrainSourcesRemainDesertBiome()
    {
        var biome = MechWarriorTerrainBiomeResolver.Resolve([
            "YELLWLD1.BWD",
            "YELLARE1.BWD"
        ]);

        Assert.That(biome, Is.EqualTo(MechWarriorTerrainBiome.Desert));
    }
}
