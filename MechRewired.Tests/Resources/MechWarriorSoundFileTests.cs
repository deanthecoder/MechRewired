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
public sealed class MechWarriorSoundFileTests
{
    [Test]
    public void LoadDecodesRawSamples()
    {
        var sound = MechWarriorSoundFile.Load(CreateSound(4, 0x05, 10, 20, 30, 40));

        Assert.That(sound.Samples, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
    }

    [Test]
    public void LoadDecodesDpcmLeastSignificantCodeFirst()
    {
        var sound = MechWarriorSoundFile.Load(CreateSound(4, 0x02, 64, 65, 0b00001010));

        Assert.That(sound.Samples, Is.EqualTo(new byte[] { 128, 130, 130, 132 }));
    }

    [Test]
    public void LoadInterpolatesReducedFrames()
    {
        var sound = MechWarriorSoundFile.Load(CreateSound(4, 0x45, 100, 200));

        Assert.That(sound.Samples, Is.EqualTo(new byte[] { 100, 150, 200, 200 }));
    }

    [Test]
    public void LoadRejectsTruncatedFrames()
    {
        Assert.That(
            () => MechWarriorSoundFile.Load(CreateSound(4, 0x05, 10)),
            Throws.TypeOf<InvalidDataException>());
    }

    private static byte[] CreateSound(ushort blockSize, params byte[] encodedBlock)
    {
        var data = new byte[14 + encodedBlock.Length];
        "SFLX"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)data.Length - 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), blockSize);
        encodedBlock.CopyTo(data, 14);
        return data;
    }
}
