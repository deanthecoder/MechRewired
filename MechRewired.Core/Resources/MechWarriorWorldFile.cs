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
using System.Numerics;
using System.Text;

namespace MechRewired.Resources;

/// <summary>
/// Decodes the world includes and positioned objects in an MW2 BWD resource.
/// </summary>
/// <remarks>
/// Unknown tags are deliberately skipped so support can grow alongside the playable level slice.
/// </remarks>
public sealed class MechWarriorWorldFile
{
    private const int TagOffset = 0x34;
    private const float TranslationScale = 0.01f;
    private const float RotationScale = 1.0f / 65536.0f;

    private MechWarriorWorldFile(
        IReadOnlyList<MechWarriorWorldInclude> includes,
        IReadOnlyList<MechWarriorWorldObject> objects)
    {
        Includes = includes;
        Objects = objects;
    }

    public IReadOnlyList<MechWarriorWorldInclude> Includes { get; }

    public IReadOnlyList<MechWarriorWorldObject> Objects { get; }

    /// <summary>
    /// Decodes one BWD payload, applying an optional transform supplied by its parent include.
    /// </summary>
    public static MechWarriorWorldFile Load(
        ReadOnlySpan<byte> data,
        MechWarriorWorldTransform parentTransform = null)
    {
        if (data.Length < TagOffset || !data[..3].SequenceEqual("BWD"u8))
        {
            throw new InvalidDataException("The world resource does not have the expected BWD signature.");
        }

        var includes = new List<MechWarriorWorldInclude>();
        var objects = new List<MechWarriorWorldObject>();
        var localTransforms = new Dictionary<int, MechWarriorWorldTransform>();
        var blockTransform = MechWarriorWorldTransform.Identity;
        var offset = TagOffset;
        while (offset < data.Length)
        {
            EnsureRange(data, offset, 8, "tag header");
            var tagName = Encoding.ASCII.GetString(data.Slice(offset, 4)).TrimEnd('\0');
            var tagSize = ReadInt32(data, offset + 4);
            if (tagSize < 8)
            {
                throw new InvalidDataException($"BWD tag {tagName} has invalid size {tagSize}.");
            }

            EnsureRange(data, offset, tagSize, $"{tagName} tag");
            switch (tagName)
            {
                case "BLKX":
                    EnsureTagSize(tagName, tagSize, 44);
                    blockTransform = ReadTransform(data, offset + 8, MechWarriorWorldTransform.Identity);
                    break;

                case "INCL":
                    EnsureTagSize(tagName, tagSize, 20);
                    includes.Add(new MechWarriorWorldInclude(
                        ReadInt16(data, offset + 8),
                        ReadAscii(data, offset + 10, 9),
                        blockTransform));
                    break;

                case "OBJ":
                    EnsureTagSize(tagName, tagSize, 60);
                    var id = ReadInt16(data, offset + 8);
                    var relativeTo = ReadInt16(data, offset + 10);
                    var baseTransform = parentTransform ?? MechWarriorWorldTransform.Identity;
                    if (relativeTo >= 0 && !localTransforms.TryGetValue(relativeTo, out baseTransform))
                    {
                        throw new InvalidDataException($"BWD object {id} refers to missing object {relativeTo}.");
                    }

                    var transform = ReadTransform(data, offset + 14, baseTransform);
                    localTransforms.Add(id, transform);
                    objects.Add(new MechWarriorWorldObject(id, ReadInt16(data, offset + 56), transform));
                    break;
            }

            offset += tagSize;
        }

        return new MechWarriorWorldFile(includes.AsReadOnly(), objects.AsReadOnly());
    }

    private static MechWarriorWorldTransform ReadTransform(
        ReadOnlySpan<byte> data,
        int offset,
        MechWarriorWorldTransform baseTransform)
    {
        var scale = new Vector3(
            ReadInt32(data, offset),
            ReadInt32(data, offset + 4),
            ReadInt32(data, offset + 8));
        var rotation = new Vector3(
            ReadInt32(data, offset + 12) * RotationScale,
            ReadInt32(data, offset + 16) * RotationScale,
            ReadInt32(data, offset + 20) * RotationScale);
        var translation = new Vector3(
            ReadInt32(data, offset + 24) * TranslationScale,
            ReadInt32(data, offset + 28) * TranslationScale,
            ReadInt32(data, offset + 32) * TranslationScale);
        return new MechWarriorWorldTransform(
            baseTransform.Scale * scale,
            baseTransform.RotationDegrees + rotation,
            baseTransform.Translation + translation);
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, sizeof(short)));

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));

    private static string ReadAscii(ReadOnlySpan<byte> data, int offset, int length)
    {
        var value = data.Slice(offset, length);
        var terminator = value.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator < 0 ? value : value[..terminator]);
    }

    private static void EnsureTagSize(string tagName, int actualSize, int requiredSize)
    {
        if (actualSize < requiredSize)
        {
            throw new InvalidDataException($"BWD tag {tagName} is {actualSize} bytes; expected at least {requiredSize}.");
        }
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException(
                $"The BWD {description} range ({offset:N0} + {length:N0} bytes) exceeds the {data.Length:N0}-byte resource.");
        }
    }
}
