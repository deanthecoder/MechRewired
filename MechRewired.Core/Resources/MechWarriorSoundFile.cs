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
/// Decodes MW2's block-compressed SFLX audio into unsigned 8-bit mono PCM.
/// </summary>
public sealed class MechWarriorSoundFile
{
    public const int SampleRate = 11025;

    private const int HeaderSize = 14;
    private const byte HalfFrame = 0x40;
    private const byte QuarterFrame = 0x80;

    private MechWarriorSoundFile(byte[] samples)
    {
        Samples = samples;
    }

    public IReadOnlyList<byte> Samples { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Count / SampleRate);

    public static MechWarriorSoundFile Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderSize || !data.AsSpan(0, 4).SequenceEqual("SFLX"u8))
        {
            throw new InvalidDataException("The sound resource does not have a valid SFLX header.");
        }

        var blockCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));
        var blockSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12, 2));
        if (blockCount == 0 || blockSize == 0)
        {
            throw new InvalidDataException("The SFLX sound must contain non-empty audio blocks.");
        }

        var outputSize = checked((int)blockCount * blockSize);
        var output = new byte[outputSize];
        var previousFrame = new byte[blockSize];
        Array.Fill(previousFrame, (byte)128);
        var cursor = HeaderSize;
        byte accumulator = 128;
        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            var control = ReadByte(data, ref cursor, blockIndex, "control");
            var encodedSampleCount = (control & HalfFrame) != 0
                ? blockSize / 2
                : (control & QuarterFrame) != 0
                    ? blockSize / 4
                    : blockSize;
            if (encodedSampleCount == 0)
            {
                throw new InvalidDataException($"SFLX block {blockIndex} has an invalid reduced frame size.");
            }

            var encodedFrame = new byte[encodedSampleCount];
            switch (control & 0x0f)
            {
                case 0:
                    Array.Fill(encodedFrame, (byte)128);
                    accumulator = 128;
                    break;

                case 1:
                    CopyReducedFrame(previousFrame, encodedFrame);
                    break;

                case 2:
                    DecodeDpcm(data, ref cursor, encodedFrame, ref accumulator, 1, blockIndex);
                    break;

                case 3:
                    DecodeDpcm(data, ref cursor, encodedFrame, ref accumulator, 2, blockIndex);
                    break;

                case 4:
                    DecodeDpcm(data, ref cursor, encodedFrame, ref accumulator, 4, blockIndex);
                    break;

                case 5:
                    ReadExactly(data, ref cursor, encodedFrame, blockIndex, "raw samples");
                    accumulator = encodedFrame[^1];
                    break;

                default:
                    throw new InvalidDataException(
                        $"SFLX block {blockIndex} uses unsupported control 0x{control & 0x0f:X2}.");
            }

            var frame = ExpandFrame(encodedFrame, blockSize);
            frame.CopyTo(output, blockIndex * blockSize);
            previousFrame = frame;
        }

        return new MechWarriorSoundFile(output);
    }

    private static void DecodeDpcm(
        byte[] data,
        ref int cursor,
        byte[] frame,
        ref byte accumulator,
        int bitsPerCode,
        int blockIndex)
    {
        var deltas = new int[1 << bitsPerCode];
        for (var index = 0; index < deltas.Length; index++)
        {
            deltas[index] = ReadByte(data, ref cursor, blockIndex, "delta table") * 2 - 128;
        }

        var codesPerByte = 8 / bitsPerCode;
        var codeMask = (1 << bitsPerCode) - 1;
        var sampleIndex = 0;
        while (sampleIndex < frame.Length)
        {
            var codes = ReadByte(data, ref cursor, blockIndex, "DPCM codes");
            for (var codeIndex = 0; codeIndex < codesPerByte && sampleIndex < frame.Length; codeIndex++)
            {
                var deltaIndex = codes >> (codeIndex * bitsPerCode) & codeMask;
                accumulator = unchecked((byte)(accumulator + deltas[deltaIndex]));
                frame[sampleIndex++] = accumulator;
            }
        }
    }

    private static byte[] ExpandFrame(byte[] source, int outputSize)
    {
        if (source.Length == outputSize)
        {
            return source;
        }

        var scale = outputSize / source.Length;
        var output = new byte[outputSize];
        for (var index = 0; index < output.Length; index++)
        {
            var sourceIndex = index / scale;
            var nextIndex = Math.Min(sourceIndex + 1, source.Length - 1);
            var fraction = index % scale;
            output[index] = (byte)((source[sourceIndex] * (scale - fraction) + source[nextIndex] * fraction) / scale);
        }

        return output;
    }

    private static void CopyReducedFrame(byte[] previousFrame, byte[] destination)
    {
        var scale = previousFrame.Length / destination.Length;
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = previousFrame[index * scale];
        }
    }

    private static byte ReadByte(byte[] data, ref int cursor, int blockIndex, string description)
    {
        if (cursor >= data.Length)
        {
            throw new InvalidDataException($"SFLX block {blockIndex} ends inside its {description}.");
        }

        return data[cursor++];
    }

    private static void ReadExactly(
        byte[] data,
        ref int cursor,
        byte[] destination,
        int blockIndex,
        string description)
    {
        if (cursor > data.Length - destination.Length)
        {
            throw new InvalidDataException($"SFLX block {blockIndex} ends inside its {description}.");
        }

        data.AsSpan(cursor, destination.Length).CopyTo(destination);
        cursor += destination.Length;
    }
}
