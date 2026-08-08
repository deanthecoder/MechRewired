// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Buffers.Binary;
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

[TestFixture]
public sealed class MechWarriorShapeImageTests
{
    [Test]
    public void LoadDecompressesTransparentRepeatAndLiteralRunsBottomToTop()
    {
        var data = CreateShape(
        [
            1, 1, 4, 7, 3, 8, 0,
            7, 1, 2, 3, 0
        ]);

        var image = MechWarriorShapeImage.Load(data);

        Assert.Multiple(() =>
        {
            Assert.That(image.Width, Is.EqualTo(4));
            Assert.That(image.Height, Is.EqualTo(2));
            Assert.That(image.GetPixel(0, 0), Is.EqualTo(1));
            Assert.That(image.GetPixel(1, 0), Is.EqualTo(2));
            Assert.That(image.GetPixel(2, 0), Is.EqualTo(3));
            Assert.That(image.IsOpaque(3, 0), Is.False);
            Assert.That(image.IsOpaque(0, 1), Is.False);
            Assert.That(image.GetPixel(1, 1), Is.EqualTo(7));
            Assert.That(image.GetPixel(2, 1), Is.EqualTo(7));
            Assert.That(image.GetPixel(3, 1), Is.EqualTo(8));
        });
    }

    [Test]
    public void LoadRejectsTruncatedLiteralRun()
    {
        var data = CreateShape([7, 1, 2]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MechWarriorShapeImage.Load(data));

        Assert.That(exception.Message, Does.Contain("literal shape run is truncated"));
    }

    [Test]
    public void LoadRejectsInvalidSignature()
    {
        var data = CreateShape([0]);
        data[0] = (byte)'0';

        var exception = Assert.Throws<InvalidDataException>(() =>
            MechWarriorShapeImage.Load(data));

        Assert.That(exception.Message, Does.Contain("1.10 signature"));
    }

    [Test]
    public void LoadRejectsMissingScanlines()
    {
        var data = CreateShape([0]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MechWarriorShapeImage.Load(data));

        Assert.That(exception.Message, Does.Contain("before all scanlines"));
    }

    private static byte[] CreateShape(byte[] commands)
    {
        const int frameOffset = 16;
        var data = new byte[frameOffset + 24 + commands.Length];
        "1.10"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), frameOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(frameOffset + 16), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(frameOffset + 20), 1);
        commands.CopyTo(data, frameOffset + 24);
        return data;
    }
}
