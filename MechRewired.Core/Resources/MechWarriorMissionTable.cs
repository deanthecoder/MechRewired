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
/// Decodes the mission records stored in an MW2 BWD <c>MTBL</c> tag.
/// </summary>
/// <remarks>
/// The reader preserves undecoded fields so later cross-mission comparisons do not require reparsing raw bytes.
/// </remarks>
public sealed class MechWarriorMissionTable
{
    private const int HeaderSize = 30;
    private const int RecordSize = 151;

    private MechWarriorMissionTable(
        int index,
        int missionTimeSeconds,
        MechWarriorMissionResourceReference successReport,
        MechWarriorMissionResourceReference failureReport,
        IReadOnlyList<MechWarriorMissionTableEntry> entries)
    {
        Index = index;
        MissionTimeSeconds = missionTimeSeconds;
        SuccessReport = successReport;
        FailureReport = failureReport;
        Entries = entries;
    }

    public int Index { get; }

    /// <summary>Authored mission duration in seconds; -1 means no limit.</summary>
    public int MissionTimeSeconds { get; }

    public MechWarriorMissionResourceReference SuccessReport { get; }

    public MechWarriorMissionResourceReference FailureReport { get; }

    public IReadOnlyList<MechWarriorMissionTableEntry> Entries { get; }

    public static MechWarriorMissionTable Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || (data.Length - HeaderSize) % RecordSize != 0)
        {
            throw new InvalidDataException(
                $"The MTBL payload is {data.Length} bytes; expected a {HeaderSize}-byte header " +
                $"followed by {RecordSize}-byte records.");
        }

        var entries = new List<MechWarriorMissionTableEntry>();
        var recordData = data[HeaderSize..];
        for (var index = 0; index < recordData.Length / RecordSize; index++)
        {
            entries.Add(ReadEntry(index, recordData.Slice(index * RecordSize, RecordSize)));
        }

        return new MechWarriorMissionTable(
            ReadInt32(data, 0),
            ReadInt32(data, 4),
            ReadReference(data, 8, 10),
            ReadReference(data, 19, 21),
            entries.AsReadOnly());
    }

    private static MechWarriorMissionTableEntry ReadEntry(int index, ReadOnlySpan<byte> data)
    {
        var conditions = new List<MechWarriorMissionCondition>();
        for (var offset = 10; offset < 42; offset += 4)
        {
            var condition = ReadCondition(data, offset);
            if ((byte)condition.Result != 0xff)
            {
                conditions.Add(condition);
            }
        }

        var targetObjectiveMarker = ReadInt16(data, 83);
        var targetObjectiveIndex = ReadInt16(data, 85);

        return new MechWarriorMissionTableEntry(
            index,
            (MechWarriorMissionAction)ReadUInt16(data, 0),
            (MechWarriorMissionControlAction)ReadUInt16(data, 2),
            (char)data[4],
            (MechWarriorMissionConditionLogic)ReadInt32(data, 6),
            conditions.AsReadOnly(),
            ReadInt32(data, 42),
            (char)data[47],
            data[46],
            data[48],
            data[49],
            ReadReference(data, 50, 52),
            ReadReference(data, 61, 63),
            ReadReference(data, 72, 74),
            targetObjectiveMarker,
            targetObjectiveMarker == 0 && targetObjectiveIndex >= 0 ? targetObjectiveIndex : null,
            NormalizeWhitespace(ReadAscii(data, 87, 64)));
    }

    private static MechWarriorMissionCondition ReadCondition(ReadOnlySpan<byte> data, int offset) =>
        new((MechWarriorMissionConditionResult)data[offset], data[offset + 1], data[offset + 2]);

    private static MechWarriorMissionResourceReference ReadReference(
        ReadOnlySpan<byte> data,
        int indexOffset,
        int nameOffset)
    {
        var index = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(indexOffset, sizeof(short)));
        return new MechWarriorMissionResourceReference(
            index < 0 ? null : index,
            ReadAscii(data, nameOffset, 9));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, sizeof(ushort)));

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

    private static string NormalizeWhitespace(string value) =>
        string.Join(' ', value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
}
