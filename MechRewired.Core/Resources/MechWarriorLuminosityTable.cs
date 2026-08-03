// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Resources;

/// <summary>
/// Maps an MW2 polygon palette index to one of sixteen source illumination levels.
/// </summary>
/// <remarks>
/// DOS MW2 used these mappings to select pre-shaded palette ramps rather than physically lighting RGB colors.
/// </remarks>
public sealed class MechWarriorLuminosityTable
{
    public const int LevelCount = 16;
    public const int PaletteColorCount = 256;
    private const int DataLength = LevelCount * PaletteColorCount;

    private readonly byte[] m_paletteIndices;

    private MechWarriorLuminosityTable(byte[] paletteIndices)
    {
        m_paletteIndices = paletteIndices;
    }

    /// <summary>
    /// Decodes a complete 16-by-256 luminosity mapping table.
    /// </summary>
    public static MechWarriorLuminosityTable Load(ReadOnlySpan<byte> data)
    {
        if (data.Length != DataLength)
        {
            throw new InvalidDataException(
                $"The luminosity table is {data.Length:N0} bytes; expected {DataLength:N0}.");
        }

        return new MechWarriorLuminosityTable(data.ToArray());
    }

    /// <summary>
    /// Gets the mapped palette index, where level zero is brightest and fifteen is darkest.
    /// </summary>
    public byte GetPaletteIndex(byte paletteIndex, int illuminationLevel)
    {
        if (illuminationLevel is < 0 or >= LevelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(illuminationLevel));
        }

        return m_paletteIndices[illuminationLevel * PaletteColorCount + paletteIndex];
    }
}
