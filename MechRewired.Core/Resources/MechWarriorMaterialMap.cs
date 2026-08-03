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
using System.Text;

namespace MechRewired.Resources;

/// <summary>
/// Decodes the BMPJ/BMID associations in an MW2 material-map BWD resource.
/// </summary>
public sealed class MechWarriorMaterialMap
{
    private const int TagOffset = 0x34;

    private MechWarriorMaterialMap(IReadOnlyDictionary<byte, MechWarriorMaterialImage> images)
    {
        Images = images;
    }

    public IReadOnlyDictionary<byte, MechWarriorMaterialImage> Images { get; }

    /// <summary>
    /// Loads one material bank. Mech geometry uses bank 1 in the DOS MW2_MAP1 table.
    /// </summary>
    public static MechWarriorMaterialMap Load(ReadOnlySpan<byte> data, byte materialBank)
    {
        if (data.Length < TagOffset || !data[..3].SequenceEqual("BWD"u8))
        {
            throw new InvalidDataException("The material map does not have the expected BWD signature.");
        }

        var images = new Dictionary<byte, MechWarriorMaterialImage>();
        int? imageResourceIndex = null;
        string imageName = null;
        for (var offset = TagOffset; offset < data.Length;)
        {
            EnsureRange(data, offset, 8, "tag header");
            var tagName = Encoding.ASCII.GetString(data.Slice(offset, 4)).TrimEnd('\0');
            var tagSize = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]);
            if (tagSize < 8)
            {
                throw new InvalidDataException($"Material-map tag {tagName} has invalid size {tagSize}.");
            }

            EnsureRange(data, offset, tagSize, $"{tagName} tag");
            switch (tagName)
            {
                case "BMPJ":
                    if (tagSize < 20)
                    {
                        throw new InvalidDataException("A material-map BMPJ tag must contain an image reference.");
                    }

                    imageResourceIndex = BinaryPrimitives.ReadInt16LittleEndian(data[(offset + 8)..]);
                    imageName = ReadAscii(data, offset + 10, 10);
                    break;

                case "BMID":
                    if (tagSize < 12 || imageResourceIndex == null)
                    {
                        throw new InvalidDataException("A material-map BMID tag must follow an image reference.");
                    }

                    var materialId = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 8)..]);
                    if ((byte)(materialId >> 8) == materialBank)
                    {
                        var materialIndex = (byte)materialId;
                        images[materialIndex] = new MechWarriorMaterialImage(
                            materialIndex,
                            imageResourceIndex.Value,
                            imageName);
                    }

                    break;
            }

            offset += tagSize;
        }

        return new MechWarriorMaterialMap(
            new System.Collections.ObjectModel.ReadOnlyDictionary<byte, MechWarriorMaterialImage>(images));
    }

    private static string ReadAscii(ReadOnlySpan<byte> data, int offset, int length)
    {
        var value = data.Slice(offset, length);
        var terminator = value.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator < 0 ? value : value[..terminator]);
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException(
                $"The material-map {description} range ({offset:N0} + {length:N0} bytes) exceeds the " +
                $"{data.Length:N0}-byte resource.");
        }
    }
}
