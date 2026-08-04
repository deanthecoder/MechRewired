// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

[TestFixture]
public sealed class MechWarriorWaveFileTests
{
    [Test]
    public void LoadReadsUnsignedEightBitMonoPcm()
    {
        var data = WriteWave(11025, 8, [0, 128, 255]);

        var sound = MechWarriorWaveFile.Load(data);

        Assert.Multiple(() =>
        {
            Assert.That(sound.SampleRate, Is.EqualTo(11025));
            Assert.That(sound.BitsPerSample, Is.EqualTo(8));
            Assert.That(sound.Samples, Is.EqualTo(new byte[] { 0, 128, 255 }));
        });
    }

    private static byte[] WriteWave(int sampleRate, short bitsPerSample, byte[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        var bytesPerSample = bitsPerSample / 8;
        writer.Write(sampleRate * bytesPerSample);
        writer.Write((short)bytesPerSample);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(samples.Length);
        writer.Write(samples);
        return stream.ToArray();
    }
}
