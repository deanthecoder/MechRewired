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
/// Unknown tags are retained as structured diagnostics so support can grow without silently
/// losing authored behaviour.
/// </remarks>
public sealed class MechWarriorWorldFile
{
    private const int TagOffset = 0x34;
    private const float TranslationScale = 0.01f;
    private const float RotationScale = 1.0f / 65536.0f;

    private MechWarriorWorldFile(
        IReadOnlyList<MechWarriorWorldInclude> includes,
        IReadOnlyList<MechWarriorWorldObject> objects,
        IReadOnlyList<MechWarriorWorldEntity> entities,
        IReadOnlyList<MechWarriorWorldNavPoint> navPoints,
        IReadOnlyList<MechWarriorMissionTable> missionTables,
        IReadOnlyList<MechWarriorWorldPathTable> pathTables,
        IReadOnlyList<MechWarriorWorldTask> tasks,
        IReadOnlyList<MechWarriorGamePieceSpecification> gamePieceSpecifications,
        IReadOnlyList<MechWarriorMissionStar> stars,
        IReadOnlyList<MechWarriorUnknownTag> unknownTags,
        int? timeOfDay,
        MechWarriorWorldLighting lighting,
        string luminosityTable,
        float? viewDistance)
    {
        Includes = includes;
        Objects = objects;
        Entities = entities;
        NavPoints = navPoints;
        MissionTables = missionTables;
        PathTables = pathTables;
        Tasks = tasks;
        GamePieceSpecifications = gamePieceSpecifications;
        Stars = stars;
        UnknownTags = unknownTags;
        TimeOfDay = timeOfDay;
        Lighting = lighting;
        LuminosityTable = luminosityTable;
        ViewDistance = viewDistance;
    }

    public IReadOnlyList<MechWarriorWorldInclude> Includes { get; }

    public IReadOnlyList<MechWarriorWorldObject> Objects { get; }

    public IReadOnlyList<MechWarriorWorldEntity> Entities { get; }

    public IReadOnlyList<MechWarriorWorldNavPoint> NavPoints { get; }

    public IReadOnlyList<MechWarriorMissionTable> MissionTables { get; }

    public IReadOnlyList<MechWarriorWorldPathTable> PathTables { get; }

    public IReadOnlyList<MechWarriorWorldTask> Tasks { get; }

    public IReadOnlyList<MechWarriorGamePieceSpecification> GamePieceSpecifications { get; }

    public IReadOnlyList<MechWarriorMissionStar> Stars { get; }

    /// <summary>Tags preserved verbatim because this decoder has no semantic handler for them yet.</summary>
    public IReadOnlyList<MechWarriorUnknownTag> UnknownTags { get; }

    public int? TimeOfDay { get; }

    public MechWarriorWorldLighting Lighting { get; }

    public string LuminosityTable { get; }

    public float? ViewDistance { get; }

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
        var entities = new List<MechWarriorWorldEntity>();
        var navPoints = new List<MechWarriorWorldNavPoint>();
        var missionTables = new List<MechWarriorMissionTable>();
        var pathTables = new List<MechWarriorWorldPathTable>();
        var tasks = new List<MechWarriorWorldTask>();
        var gamePieceSpecifications = new List<MechWarriorGamePieceSpecification>();
        var stars = new List<MechWarriorMissionStar>();
        var unknownTags = new List<MechWarriorUnknownTag>();
        var localTransforms = new Dictionary<int, MechWarriorWorldTransform>();
        int? timeOfDay = null;
        MechWarriorWorldLighting lighting = null;
        string luminosityTable = null;
        float? viewDistance = null;
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
                    objects.Add(new MechWarriorWorldObject(
                        id,
                        relativeTo,
                        ReadUInt16(data, offset + 12),
                        ReadUInt16(data, offset + 52),
                        ReadInt16(data, offset + 56),
                        transform));
                    break;

                case "GT":
                    EnsureTagSize(tagName, tagSize, 32);
                    var destroyedObjectId = ReadInt16(data, offset + 10);
                    var hasFixedDescriptions = tagSize >= 76;
                    entities.Add(new MechWarriorWorldEntity(
                        ReadInt16(data, offset + 8),
                        destroyedObjectId < 0 ? null : destroyedObjectId,
                        ReadUInt16(data, offset + 24),
                        ReadAscii(data, offset + 32, hasFixedDescriptions ? 22 : tagSize - 32).TrimEnd(),
                        hasFixedDescriptions ? ReadAscii(data, offset + 54, 22).TrimEnd() : string.Empty,
                        ReadUInt16(data, offset + 28)));
                    break;

                case "INIT":
                    EnsureTagSize(tagName, tagSize, 28);
                    timeOfDay = ReadInt32(data, offset + 16);
                    break;

                case "LITE":
                    EnsureTagSize(tagName, tagSize, 40);
                    lighting = new MechWarriorWorldLighting(
                        new Vector3(
                            ReadInt32(data, offset + 16) * TranslationScale,
                            ReadInt32(data, offset + 20) * TranslationScale,
                            ReadInt32(data, offset + 24) * TranslationScale),
                        ReadUInt16(data, offset + 28),
                        ReadUInt16(data, offset + 30),
                        ReadInt32(data, offset + 32) * TranslationScale);
                    break;

                case "LTBL":
                    EnsureTagSize(tagName, tagSize, 18);
                    luminosityTable = ReadAscii(data, offset + 10, 8);
                    break;

                case "VIEW":
                    EnsureTagSize(tagName, tagSize, 16);
                    viewDistance = ReadInt32(data, offset + 12) * TranslationScale;
                    if (viewDistance <= 0.0f)
                    {
                        throw new InvalidDataException($"BWD VIEW tag has invalid distance {viewDistance:F2}.");
                    }

                    break;

                case "NAVP":
                    EnsureTagSize(tagName, tagSize, 80);
                    navPoints.Add(new MechWarriorWorldNavPoint(
                        new Vector3(
                            ReadInt32(data, offset + 8) * TranslationScale,
                            ReadInt32(data, offset + 12) * TranslationScale,
                            ReadInt32(data, offset + 16) * TranslationScale),
                        ReadInt16(data, offset + 22),
                        ReadUInt16(data, offset + 24) != 0,
                        ReadUInt16(data, offset + 30),
                        ReadUInt16(data, offset + 32),
                        ReadUInt16(data, offset + 34),
                        ReadAscii(data, offset + 36, 21).TrimEnd()));
                    break;

                case "MTBL":
                    missionTables.Add(MechWarriorMissionTable.Load(data.Slice(offset + 8, tagSize - 8)));
                    break;

                case "PTBL":
                    const int pathNameSize = 64;
                    const int pathPointSize = 28;
                    EnsureTagSize(tagName, tagSize, 8 + pathNameSize + pathPointSize);
                    var pathPayloadSize = tagSize - 8 - pathNameSize;
                    if (pathPayloadSize % pathPointSize != 0)
                    {
                        throw new InvalidDataException(
                            $"BWD PTBL tag has {pathPayloadSize} point bytes; expected 28-byte records.");
                    }

                    var pathPoints = new List<MechWarriorWorldPathPoint>(pathPayloadSize / pathPointSize);
                    for (var pointOffset = offset + 8 + pathNameSize;
                         pointOffset < offset + tagSize;
                         pointOffset += pathPointSize)
                    {
                        pathPoints.Add(new MechWarriorWorldPathPoint(
                            new Vector3(
                                ReadInt32(data, pointOffset) * TranslationScale,
                                ReadInt32(data, pointOffset + 4) * TranslationScale,
                                ReadInt32(data, pointOffset + 8) * TranslationScale),
                            new Vector3(
                                ReadInt32(data, pointOffset + 12),
                                ReadInt32(data, pointOffset + 16),
                                ReadInt32(data, pointOffset + 20)),
                            ReadInt32(data, pointOffset + 24)));
                    }

                    pathTables.Add(new MechWarriorWorldPathTable(
                        ReadAscii(data, offset + 8, pathNameSize),
                        pathPoints.AsReadOnly()));
                    break;

                case "TSK":
                    EnsureTagSize(tagName, tagSize, 15);
                    tasks.Add(new MechWarriorWorldTask(
                        ReadUInt16(data, offset + 8),
                        ReadInt32(data, offset + 10),
                        ReadAscii(data, offset + 14, tagSize - 14)));
                    break;

                case "GPS":
                    EnsureTagSize(tagName, tagSize, 100);
                    gamePieceSpecifications.Add(new MechWarriorGamePieceSpecification(
                        ReadInt16(data, offset + 8),
                        ReadInt16(data, offset + 10),
                        data[offset + 12],
                        data[offset + 13],
                        ReadUInt16(data, offset + 14),
                        ReadUInt16(data, offset + 16),
                        ReadUInt16(data, offset + 18),
                        ReadUInt16(data, offset + 20),
                        ReadUInt16(data, offset + 22),
                        ReadUInt16(data, offset + 24),
                        ReadUInt16(data, offset + 32),
                        data[offset + 35],
                        ReadAscii(data, offset + 36, 9),
                        ReadAscii(data, offset + 45, 9),
                        ReadAscii(data, offset + 54, 22).TrimEnd(),
                        ReadAscii(data, offset + 76, 22).TrimEnd()));
                    break;

                case "STAR":
                    const int starRecordSize = 24;
                    var starPayloadSize = tagSize - 8;
                    if (starPayloadSize % starRecordSize != 0)
                    {
                        throw new InvalidDataException(
                            $"BWD STAR tag has {starPayloadSize} payload bytes; expected 24-byte records.");
                    }

                    for (var starOffset = offset + 8;
                         starOffset < offset + tagSize;
                         starOffset += starRecordSize)
                    {
                        var disposition = ReadInt32(data, starOffset + 4);
                        if (!Enum.IsDefined(typeof(MechWarriorMissionDisposition), disposition))
                        {
                            throw new InvalidDataException(
                                $"BWD STAR group {stars.Count} has unsupported disposition {disposition}.");
                        }

                        stars.Add(new MechWarriorMissionStar(
                            stars.Count,
                            ReadInt32(data, starOffset),
                            (MechWarriorMissionDisposition)disposition,
                            ReadAscii(data, starOffset + 8, 8)));
                    }

                    break;

                default:
                    unknownTags.Add(new MechWarriorUnknownTag(tagName, offset, tagSize));
                    break;
            }

            offset += tagSize;
        }

        return new MechWarriorWorldFile(
            includes.AsReadOnly(),
            objects.AsReadOnly(),
            entities.AsReadOnly(),
            navPoints.AsReadOnly(),
            missionTables.AsReadOnly(),
            pathTables.AsReadOnly(),
            tasks.AsReadOnly(),
            gamePieceSpecifications.AsReadOnly(),
            stars.AsReadOnly(),
            unknownTags.AsReadOnly(),
            timeOfDay,
            lighting,
            luminosityTable,
            viewDistance);
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

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));

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
