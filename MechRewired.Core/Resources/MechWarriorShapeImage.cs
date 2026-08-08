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
/// Represents one decompressed, palette-indexed image from an MW2 <c>SHP</c> resource.
/// </summary>
public sealed class MechWarriorShapeImage
{
    private const int FileHeaderSize = 8;
    private const int FrameTableEntrySize = 8;
    private const int FrameHeaderSize = 24;
    private const int MaximumDimension = 1000;

    private readonly byte[] m_pixels;
    private readonly bool[] m_opaquePixels;

    private MechWarriorShapeImage(int width, int height, byte[] pixels, bool[] opaquePixels)
    {
        Width = width;
        Height = height;
        m_pixels = pixels;
        m_opaquePixels = opaquePixels;
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<byte> Pixels => m_pixels;

    /// <summary>
    /// Decompresses one image from a version 1.10 MW2 shape resource.
    /// </summary>
    public static MechWarriorShapeImage Load(ReadOnlySpan<byte> data, int frameIndex = 0)
    {
        if (data.Length < FileHeaderSize || !data[..4].SequenceEqual("1.10"u8))
        {
            throw new InvalidDataException("An MW2 shape must begin with the 1.10 signature.");
        }

        var frameCount = ReadUInt32(data, 4, "shape frame count");
        if (frameCount == 0)
        {
            throw new InvalidDataException("An MW2 shape must contain at least one frame.");
        }

        if (frameIndex < 0 || (uint)frameIndex >= frameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        var frameTableOffset = checked(FileHeaderSize + frameIndex * FrameTableEntrySize);
        var frameStart = ReadOffset(data, frameTableOffset, "shape frame");
        var frameEnd = frameIndex + 1 < frameCount
            ? ReadOffset(data, frameTableOffset + FrameTableEntrySize, "next shape frame")
            : data.Length;
        if (frameEnd < frameStart || frameStart > data.Length - FrameHeaderSize)
        {
            throw new InvalidDataException("The MW2 shape frame range is invalid.");
        }

        // The frame header stores inclusive maximum X/Y bounds at +16/+20.
        var width = checked((int)ReadUInt16(data, frameStart + 16, "shape width") + 1);
        var height = checked((int)ReadUInt16(data, frameStart + 20, "shape height") + 1);
        if (width <= 1 || height <= 1 || width > MaximumDimension || height > MaximumDimension)
        {
            throw new InvalidDataException($"The MW2 shape dimensions {width}x{height} are invalid.");
        }

        var pixels = new byte[checked(width * height)];
        var opaquePixels = new bool[pixels.Length];
        var x = 0;
        var y = height - 1;
        var offset = frameStart + FrameHeaderSize;
        while (y >= 0 && offset < frameEnd)
        {
            var control = data[offset++];
            if (control == 0)
            {
                x = 0;
                y--;
                continue;
            }

            if (control == 1)
            {
                EnsureAvailable(offset, 1, frameEnd, "transparent shape run");
                Advance(data[offset++]);
                continue;
            }

            var pixelCount = control >> 1;
            if ((control & 1) == 0)
            {
                EnsureAvailable(offset, 1, frameEnd, "repeated shape run");
                var paletteIndex = data[offset++];
                for (var index = 0; index < pixelCount; index++)
                {
                    Write(paletteIndex);
                }
            }
            else
            {
                EnsureAvailable(offset, pixelCount, frameEnd, "literal shape run");
                for (var index = 0; index < pixelCount; index++)
                {
                    Write(data[offset++]);
                }
            }
        }

        if (y >= 0)
        {
            throw new InvalidDataException("The MW2 shape ended before all scanlines were decoded.");
        }

        return new MechWarriorShapeImage(width, height, pixels, opaquePixels);

        void Write(byte paletteIndex)
        {
            if (y < 0 || x >= width)
            {
                throw new InvalidDataException("The MW2 shape contains more pixels than its dimensions allow.");
            }

            pixels[y * width + x] = paletteIndex;
            opaquePixels[y * width + x] = true;
            Advance(1);
        }

        void Advance(int count)
        {
            x = checked(x + count);
            if (x > width)
            {
                throw new InvalidDataException("An MW2 shape run exceeds its scanline bounds.");
            }
        }
    }

    public byte GetPixel(int x, int y)
    {
        ValidateCoordinates(x, y);
        return m_pixels[y * Width + x];
    }

    public bool IsOpaque(int x, int y)
    {
        ValidateCoordinates(x, y);
        return m_opaquePixels[y * Width + x];
    }

    private void ValidateCoordinates(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }

    private static int ReadOffset(ReadOnlySpan<byte> data, int offset, string description)
    {
        var value = ReadUInt32(data, offset, description);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"The {description} offset is too large.");
        }

        return (int)value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, string description)
    {
        EnsureAvailable(offset, sizeof(uint), data.Length, description);
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, string description)
    {
        EnsureAvailable(offset, sizeof(ushort), data.Length, description);
        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }

    private static void EnsureAvailable(int offset, int size, int end, string description)
    {
        if (offset < 0 || size < 0 || offset > end - size)
        {
            throw new InvalidDataException($"The {description} is truncated.");
        }
    }
}
