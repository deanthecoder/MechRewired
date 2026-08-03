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
public sealed class MechWarriorIndexedImageTests
{
    [Test]
    public void LoadReadsDimensionsAndPaletteIndices()
    {
        byte[] data = [2, 0, 2, 0, 7, 8, 9, 10];

        var image = MechWarriorIndexedImage.Load(data);

        Assert.Multiple(() =>
        {
            Assert.That(image.Width, Is.EqualTo(2));
            Assert.That(image.Height, Is.EqualTo(2));
            Assert.That(image.GetPixel(0, 0), Is.EqualTo(7));
            Assert.That(image.GetPixel(1, 1), Is.EqualTo(10));
        });
    }

    [Test]
    public void LoadRejectsPayloadWhoseSizeDoesNotMatchDimensions()
    {
        byte[] data = [2, 0, 2, 0, 7, 8, 9];

        var exception = Assert.Throws<InvalidDataException>(() =>
            MechWarriorIndexedImage.Load(data));

        Assert.That(exception.Message, Does.Contain("requires 8 bytes"));
    }
}
