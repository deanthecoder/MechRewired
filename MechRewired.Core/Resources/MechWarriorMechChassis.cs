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
using MechRewired.Simulation;

namespace MechRewired.Resources;

/// <summary>
/// Decodes the highest-detail object hierarchy and weapon markers from an original mech chassis BWD.
/// </summary>
/// <remarks>
/// Mech BWDs repeat object IDs across REPR blocks; the first block contains the DOS high-detail representation used by this slice.
/// </remarks>
public sealed class MechWarriorMechChassis
{
    private const int TagOffset = 0x34;
    private const float TranslationScale = 0.01f;
    private const float RotationScale = 1.0f / 65536.0f;

    private MechWarriorMechChassis(
        IReadOnlyList<MechWarriorWorldObject> objects,
        IReadOnlyList<int> thingObjectIds,
        IReadOnlyList<MechWarriorPointOfFire> pointsOfFire,
        IReadOnlyDictionary<int, MechDamageSection> damageSectionsByObjectId)
    {
        Objects = objects;
        ThingObjectIds = thingObjectIds;
        PointsOfFire = pointsOfFire;
        DamageSectionsByObjectId = damageSectionsByObjectId;
    }

    public IReadOnlyList<MechWarriorWorldObject> Objects { get; }

    public IReadOnlyList<int> ThingObjectIds { get; }

    public IReadOnlyList<MechWarriorPointOfFire> PointsOfFire { get; }

    public IReadOnlyDictionary<int, MechDamageSection> DamageSectionsByObjectId { get; }

    public static MechWarriorMechChassis Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < TagOffset || !data[..3].SequenceEqual("BWD"u8))
        {
            throw new InvalidDataException("The mech chassis does not have the expected BWD signature.");
        }

        var objects = new List<MechWarriorWorldObject>();
        var thingObjectIds = new List<int>();
        var pointsOfFire = new List<MechWarriorPointOfFire>();
        var damageSectionsByObjectId = new Dictionary<int, MechDamageSection>();
        var transformsById = new Dictionary<int, MechWarriorWorldTransform>();
        var representationStarted = false;
        var representationComplete = false;
        for (var offset = TagOffset; offset < data.Length;)
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
                case "REPR":
                    if (representationStarted)
                    {
                        representationComplete = true;
                    }
                    else
                    {
                        representationStarted = true;
                    }

                    break;

                case "OBJ" when !representationComplete:
                    EnsureTagSize(tagName, tagSize, 60);
                    var id = ReadInt16(data, offset + 8);
                    var relativeTo = ReadInt16(data, offset + 10);
                    if (representationStarted || id == 0)
                    {
                        var baseTransform = MechWarriorWorldTransform.Identity;
                        if (relativeTo >= 0 && !transformsById.TryGetValue(relativeTo, out baseTransform))
                        {
                            throw new InvalidDataException(
                                $"Mech chassis object {id} refers to missing object {relativeTo}.");
                        }

                        var localTransform = ReadTransform(
                            data,
                            offset + 14,
                            MechWarriorWorldTransform.Identity);
                        var transform = MechWarriorWorldTransform.Combine(baseTransform, localTransform);
                        transformsById.Add(id, transform);
                        objects.Add(new MechWarriorWorldObject(
                            id,
                            relativeTo,
                            ReadUInt16(data, offset + 12),
                            ReadUInt16(data, offset + 52),
                            ReadInt16(data, offset + 56),
                            transform,
                            localTransform));
                    }

                    break;

                case "THNG":
                    EnsureTagSize(tagName, tagSize, 12);
                    thingObjectIds.Add(ReadInt32(data, offset + 8));
                    break;

                case "POFO":
                    EnsureTagSize(tagName, tagSize, 12);
                    pointsOfFire.Add(new MechWarriorPointOfFire(
                        ReadInt16(data, offset + 8),
                        ReadInt16(data, offset + 10)));
                    break;

                case "OBJL":
                    EnsureTagSize(tagName, tagSize, 12);
                    var damageObjectId = ReadInt16(data, offset + 8);
                    var damageSection = ReadDamageSection(data, offset + 10);
                    if (damageSectionsByObjectId.TryGetValue(damageObjectId, out var existingSection) &&
                        existingSection != damageSection)
                    {
                        throw new InvalidDataException(
                            $"Mech chassis object {damageObjectId} has conflicting damage groups " +
                            $"{existingSection} and {damageSection}.");
                    }

                    damageSectionsByObjectId[damageObjectId] = damageSection;
                    break;
            }

            offset += tagSize;
        }

        if (!representationStarted || objects.Count <= 1)
        {
            throw new InvalidDataException("The mech chassis contains no renderable REPR object hierarchy.");
        }

        return new MechWarriorMechChassis(
            objects.AsReadOnly(),
            thingObjectIds.AsReadOnly(),
            pointsOfFire.AsReadOnly(),
            damageSectionsByObjectId);
    }

    private static MechDamageSection ReadDamageSection(ReadOnlySpan<byte> data, int offset) =>
        ReadInt16(data, offset) switch
        {
            1 => MechDamageSection.Head,
            2 => MechDamageSection.RightTorso,
            3 => MechDamageSection.CenterTorso,
            4 => MechDamageSection.LeftTorso,
            5 => MechDamageSection.RightArm,
            6 => MechDamageSection.LeftArm,
            7 => MechDamageSection.RightLeg,
            8 => MechDamageSection.LeftLeg,
            var group => throw new InvalidDataException(
                $"Mech chassis damage group {group} is outside the supported range 1-8.")
        };

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

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));

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
                $"The {description} range ({offset:N0} + {length:N0} bytes) exceeds the {data.Length:N0}-byte resource.");
        }
    }
}

/// <summary>
/// Associates a weapon firing-point identifier with its chassis object marker.
/// </summary>
public sealed record MechWarriorPointOfFire(int ObjectId, int Id)
{
    /// <summary>
    /// The chassis damage section whose authored weapon muzzle this marker represents.
    /// </summary>
    public MechDamageSection Section => Id switch
    {
        0 => MechDamageSection.Head,
        1 => MechDamageSection.RightTorso,
        2 => MechDamageSection.CenterTorso,
        3 => MechDamageSection.LeftTorso,
        4 => MechDamageSection.RightArm,
        5 => MechDamageSection.LeftArm,
        6 => MechDamageSection.RightLeg,
        7 => MechDamageSection.LeftLeg,
        _ => throw new InvalidDataException($"Mech chassis POFO identifier {Id} is outside the supported range 0-7.")
    };
}
