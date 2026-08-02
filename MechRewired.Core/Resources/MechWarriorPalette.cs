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

namespace MechRewired.Resources;

/// <summary>
/// Represents a 256-color DOS MechWarrior 2 palette converted to 8-bit RGB channels.
/// </summary>
/// <remarks>
/// Original <c>.COL</c> files store three 6-bit VGA DAC channel values per color.
/// </remarks>
public sealed class MechWarriorPalette
{
    public const int ColorCount = 256;
    public const int DataSize = ColorCount * 3;

    private readonly Rgb[] m_colors;

    private MechWarriorPalette(Rgb[] colors)
    {
        m_colors = colors;
    }

    public Rgb this[int index] => m_colors[index];

    public IReadOnlyList<Rgb> Colors => m_colors;

    /// <summary>
    /// Loads a palette from its raw <c>.COL</c> payload.
    /// </summary>
    public static MechWarriorPalette Load(ReadOnlySpan<byte> data)
    {
        if (data.Length != DataSize)
        {
            throw new InvalidDataException($"A MechWarrior 2 palette must contain exactly {DataSize} bytes, not {data.Length}.");
        }

        var colors = new Rgb[ColorCount];
        for (var index = 0; index < colors.Length; index++)
        {
            var dataOffset = index * 3;
            colors[index] = new Rgb(
                ExpandVgaChannel(data[dataOffset]),
                ExpandVgaChannel(data[dataOffset + 1]),
                ExpandVgaChannel(data[dataOffset + 2]));
        }

        return new MechWarriorPalette(colors);
    }

    private static byte ExpandVgaChannel(byte value)
    {
        if (value > 63)
        {
            throw new InvalidDataException($"Palette channel value {value} is outside the VGA DAC range 0-63.");
        }

        return (byte)((value << 2) | (value >> 4));
    }
}
