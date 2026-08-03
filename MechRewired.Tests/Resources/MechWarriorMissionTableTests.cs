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
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

/// <summary>
/// Verifies decoding of original mission-table records.
/// </summary>
/// <remarks>
/// Fixtures use the fixed layout observed in DOS scenario BWD resources.
/// </remarks>
[TestFixture]
public sealed class MechWarriorMissionTableTests
{
    [Test]
    public void LoadsResourceLinksAndObjectiveMetadata()
    {
        var data = CreateTableData();

        var table = MechWarriorMissionTable.Load(data);

        Assert.That(table.Index, Is.Zero);
        Assert.That(table.Entries, Has.Count.EqualTo(3));
        var destroy = table.Entries[0];
        Assert.Multiple(() =>
        {
            Assert.That(destroy.TriggerFlags, Is.EqualTo(0x0002));
            Assert.That(destroy.VisibilityCode, Is.EqualTo('V'));
            Assert.That(destroy.Trigger, Is.EqualTo(new MechWarriorMissionCondition('C', 0)));
            Assert.That(destroy.GoalClass, Is.EqualTo('M'));
            Assert.That(destroy.GoalFlags, Is.EqualTo(1));
            Assert.That(destroy.SuccessReport.ResourceIndex, Is.EqualTo(337));
            Assert.That(destroy.SuccessReport.Name, Is.EqualTo("yell002S"));
            Assert.That(destroy.Target.ResourceIndex, Is.EqualTo(1383));
            Assert.That(destroy.Target.Name, Is.EqualTo("yellare6"));
            Assert.That(destroy.Description, Is.EqualTo("Destroy Chemical Plant at Nav Epsilon"));
        });
    }

    [Test]
    public void RejectsPartialRecords()
    {
        var data = new byte[30 + 150];

        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorMissionTable.Load(data));

        Assert.That(exception.Message, Does.Contain("151-byte records"));
    }

    internal static byte[] CreateTableData()
    {
        const int headerSize = 30;
        const int recordSize = 151;
        var data = new byte[headerSize + recordSize * 3];
        WriteReference(data, 8, 10, 119, "gene001S");
        WriteReference(data, 19, 21, 118, "gene001F");
        WriteEntry(
            data.AsSpan(headerSize, recordSize),
            0x0002,
            'V',
            'C',
            'M',
            1,
            337,
            "yell002S",
            1383,
            "yellare6",
            "Destroy Chemical Plant at Nav Epsilon");
        WriteEntry(
            data.AsSpan(headerSize + recordSize, recordSize),
            0x0008,
            'V',
            'C',
            'M',
            1,
            336,
            "yell001S",
            1384,
            "yellare5",
            "Inspect Firebase Wreckage at Nav Zeta");
        WriteEntry(
            data.AsSpan(headerSize + recordSize * 2, recordSize),
            0x0100,
            'H',
            'S',
            'M',
            8,
            147,
            "genegogS",
            1393,
            "yellnav3",
            "Dustoff site: Nav Eta");
        return data;
    }

    private static void WriteEntry(
        Span<byte> data,
        int triggerFlags,
        char visibility,
        char trigger,
        char goalClass,
        byte goalFlags,
        short successIndex,
        string successName,
        short targetIndex,
        string targetName,
        string description)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data, triggerFlags);
        data[4] = (byte)visibility;
        data[10] = (byte)trigger;
        for (var offset = 14; offset < 46; offset += 4)
        {
            data.Slice(offset, 4).Fill(0xff);
            data[offset + 3] = 0;
        }

        data[46] = goalFlags;
        data[47] = (byte)goalClass;
        WriteReference(data, 50, 52, successIndex, successName);
        WriteReference(data, 61, 63, -1, string.Empty);
        WriteReference(data, 72, 74, targetIndex, targetName);
        BinaryPrimitives.WriteInt32LittleEndian(data[83..], -1);
        Encoding.ASCII.GetBytes(description, data[87..]);
    }

    private static void WriteReference(
        Span<byte> data,
        int indexOffset,
        int nameOffset,
        short index,
        string name)
    {
        BinaryPrimitives.WriteInt16LittleEndian(data[indexOffset..], index);
        Encoding.ASCII.GetBytes(name, data[nameOffset..]);
    }
}
