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

/// <summary>
/// Verifies MW2 luminosity-table decoding and lookup validation.
/// </summary>
/// <remarks>
/// Focused tests protect palette-ramp lighting because a transposed table makes scenes uniformly too dark.
/// </remarks>
[TestFixture]
public sealed class MechWarriorLuminosityTableTests
{
    [Test]
    public void LoadMapsPaletteIndicesByIlluminationLevel()
    {
        var data = new byte[MechWarriorLuminosityTable.LevelCount *
                            MechWarriorLuminosityTable.PaletteColorCount];
        data[3 * MechWarriorLuminosityTable.PaletteColorCount + 70] = 65;

        var table = MechWarriorLuminosityTable.Load(data);

        Assert.That(table.GetPaletteIndex(70, 3), Is.EqualTo(65));
    }

    [Test]
    public void LoadRejectsIncompleteTable()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            MechWarriorLuminosityTable.Load(new byte[128]));

        Assert.That(exception.Message, Does.Contain("4,096"));
    }

    [Test]
    public void GetPaletteIndexRejectsInvalidIlluminationLevel()
    {
        var table = MechWarriorLuminosityTable.Load(
            new byte[MechWarriorLuminosityTable.LevelCount *
                     MechWarriorLuminosityTable.PaletteColorCount]);

        Assert.That(
            () => table.GetPaletteIndex(70, MechWarriorLuminosityTable.LevelCount),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
