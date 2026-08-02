// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

/// <summary>
/// Verifies DOS palette decoding.
/// </summary>
/// <remarks>
/// Tests cover both VGA channel expansion and malformed source data.
/// </remarks>
[TestFixture]
public sealed class MechWarriorPaletteTests
{
    [Test]
    public void LoadExpandsSixBitVgaChannelsAcrossTheFullEightBitRange()
    {
        var data = new byte[MechWarriorPalette.DataSize];
        data[3] = 63;
        data[4] = 32;
        data[5] = 16;

        var palette = MechWarriorPalette.Load(data);

        Assert.That(palette.Colors, Has.Count.EqualTo(MechWarriorPalette.ColorCount));
        Assert.That(palette[0], Is.EqualTo(Rgb.Black));
        Assert.That(palette[1], Is.EqualTo(new Rgb(255, 130, 65)));
    }

    [Test]
    public void LoadRejectsTheWrongPayloadSize()
    {
        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorPalette.Load(new byte[767]));

        Assert.That(exception.Message, Does.Contain("768"));
    }

    [Test]
    public void LoadRejectsChannelsOutsideTheVgaRange()
    {
        var data = new byte[MechWarriorPalette.DataSize];
        data[42] = 64;

        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorPalette.Load(data));

        Assert.That(exception.Message, Does.Contain("0-63"));
    }
}
