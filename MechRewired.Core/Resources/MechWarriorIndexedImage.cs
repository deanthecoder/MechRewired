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
/// Represents an uncompressed palette-indexed MW2 XEL image.
/// </summary>
public sealed class MechWarriorIndexedImage
{
    private const int HeaderSize = 4;

    private MechWarriorIndexedImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<byte> Pixels { get; }

    /// <summary>
    /// Decodes a width/height header followed by one palette index per pixel.
    /// </summary>
    public static MechWarriorIndexedImage Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("An XEL image must contain a four-byte dimensions header.");
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        if (width == 0 || height == 0)
        {
            throw new InvalidDataException("An XEL image must have non-zero dimensions.");
        }

        var expectedSize = checked(HeaderSize + width * height);
        if (data.Length != expectedSize)
        {
            throw new InvalidDataException(
                $"The {width}x{height} XEL image requires {expectedSize:N0} bytes, not {data.Length:N0}.");
        }

        return new MechWarriorIndexedImage(width, height, data[HeaderSize..].ToArray());
    }

    public byte GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return Pixels[y * Width + x];
    }
}
