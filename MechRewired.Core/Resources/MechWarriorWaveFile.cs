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

namespace MechRewired.Resources;

/// <summary>
/// Decodes the uncompressed mono RIFF/WAVE resources stored alongside MW2's SFLX sounds.
/// </summary>
public sealed record MechWarriorWaveFile(int SampleRate, int BitsPerSample, byte[] Samples)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(
        (double)Samples.Length / (SampleRate * (BitsPerSample / 8)));

    public static MechWarriorWaveFile Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 12 ||
            !data.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !data.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("The sound resource does not have a valid RIFF/WAVE header.");
        }

        int? sampleRate = null;
        int? bitsPerSample = null;
        byte[] samples = null;
        var offset = 12;
        while (offset <= data.Length - 8)
        {
            var chunkName = data.AsSpan(offset, 4);
            var chunkSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4)));
            var payloadOffset = offset + 8;
            if (payloadOffset > data.Length - chunkSize)
            {
                throw new InvalidDataException("The WAVE resource contains a chunk beyond the end of the file.");
            }

            if (chunkName.SequenceEqual("fmt "u8))
            {
                if (chunkSize < 16)
                {
                    throw new InvalidDataException("The WAVE format chunk is truncated.");
                }

                var format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(payloadOffset, 2));
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(payloadOffset + 2, 2));
                if (format != 1 || channels != 1)
                {
                    throw new InvalidDataException("Only uncompressed mono PCM WAVE resources are supported.");
                }

                sampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data.AsSpan(payloadOffset + 4, 4)));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(payloadOffset + 14, 2));
                if (sampleRate <= 0 || bitsPerSample is not (8 or 16))
                {
                    throw new InvalidDataException("The WAVE resource has an unsupported sample rate or bit depth.");
                }
            }
            else if (chunkName.SequenceEqual("data"u8))
            {
                samples = data.AsSpan(payloadOffset, chunkSize).ToArray();
            }

            offset = payloadOffset + chunkSize + (chunkSize & 1);
        }

        if (!sampleRate.HasValue || !bitsPerSample.HasValue || samples == null || samples.Length == 0)
        {
            throw new InvalidDataException("The WAVE resource is missing its format or sample data.");
        }

        return new MechWarriorWaveFile(sampleRate.Value, bitsPerSample.Value, samples);
    }
}
