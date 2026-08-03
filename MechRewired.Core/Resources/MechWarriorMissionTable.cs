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
        int unknownHeaderValue,
        MechWarriorMissionResourceReference successReport,
        MechWarriorMissionResourceReference failureReport,
        IReadOnlyList<MechWarriorMissionTableEntry> entries)
    {
        Index = index;
        UnknownHeaderValue = unknownHeaderValue;
        SuccessReport = successReport;
        FailureReport = failureReport;
        Entries = entries;
    }

    public int Index { get; }

    public int UnknownHeaderValue { get; }

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
        for (var offset = 14; offset < 46; offset += 4)
        {
            var condition = ReadCondition(data, offset);
            if (condition.Opcode != '\xff')
            {
                conditions.Add(condition);
            }
        }

        return new MechWarriorMissionTableEntry(
            index,
            ReadInt32(data, 0),
            (char)data[4],
            ReadCondition(data, 10),
            conditions.AsReadOnly(),
            (char)data[47],
            data[46],
            ReadUInt16(data, 48),
            ReadReference(data, 50, 52),
            ReadReference(data, 61, 63),
            ReadReference(data, 72, 74),
            ReadInt32(data, 83),
            NormalizeWhitespace(ReadAscii(data, 87, 64)));
    }

    private static MechWarriorMissionCondition ReadCondition(ReadOnlySpan<byte> data, int offset) =>
        new((char)data[offset], data[offset + 1] | data[offset + 2] << 8 | data[offset + 3] << 16);

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
