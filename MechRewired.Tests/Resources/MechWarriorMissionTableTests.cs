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
        Assert.That(table.MissionTimeSeconds, Is.EqualTo(1500));
        Assert.That(table.Entries, Has.Count.EqualTo(3));
        var destroy = table.Entries[0];
        Assert.Multiple(() =>
        {
            Assert.That(destroy.Action, Is.EqualTo(MechWarriorMissionAction.Destroy));
            Assert.That(destroy.ControlAction, Is.EqualTo(MechWarriorMissionControlAction.None));
            Assert.That(destroy.VisibilityCode, Is.EqualTo('V'));
            Assert.That(destroy.ConditionLogic, Is.EqualTo(MechWarriorMissionConditionLogic.Any));
            Assert.That(destroy.ActivationConditions, Is.EqualTo(new[]
            {
                new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Completed, 0, 0)
            }));
            Assert.That(destroy.TimeSeconds, Is.Zero);
            Assert.That(destroy.GoalClass, Is.EqualTo('M'));
            Assert.That(destroy.GoalFlags, Is.EqualTo(1));
            Assert.That(destroy.MechsPerTarget, Is.Zero);
            Assert.That(destroy.DoNotDisturb, Is.Zero);
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
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 1500);
        WriteReference(data, 8, 10, 119, "gene001S");
        WriteReference(data, 19, 21, 118, "gene001F");
        WriteEntry(
            data.AsSpan(headerSize, recordSize),
            MechWarriorMissionAction.Destroy,
            'V',
            [new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Completed, 0, 0)],
            'M',
            1,
            337,
            "yell002S",
            1383,
            "yellare6",
            "Destroy Chemical Plant at Nav Epsilon");
        WriteEntry(
            data.AsSpan(headerSize + recordSize, recordSize),
            MechWarriorMissionAction.Recon,
            'V',
            [new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Completed, 0, 0)],
            'M',
            1,
            336,
            "yell001S",
            1384,
            "yellare5",
            "Inspect Firebase Wreckage at Nav Zeta");
        WriteEntry(
            data.AsSpan(headerSize + recordSize * 2, recordSize),
            MechWarriorMissionAction.GoTo,
            'H',
            [
                new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Succeeded, 0, 0),
                new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Succeeded, 1, 0)
            ],
            'M',
            8,
            147,
            "genegogS",
            1393,
            "yellnav3",
            "Dustoff site: Nav Eta");
        return data;
    }

    internal static byte[] CreateAggregateTableData()
    {
        const int headerSize = 30;
        const int recordSize = 151;
        var baseData = CreateTableData();
        var data = new byte[headerSize + recordSize * 6];
        baseData.CopyTo(data, 0);
        WriteEntry(
            data.AsSpan(headerSize + recordSize * 3, recordSize),
            MechWarriorMissionAction.Destroy,
            'H',
            [new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Completed, 0, 0)],
            'O',
            0,
            -1,
            string.Empty,
            1408,
            "enemy1",
            "Enemy Mech Destroyed");
        WriteEntry(
            data.AsSpan(headerSize + recordSize * 4, recordSize),
            MechWarriorMissionAction.Destroy,
            'H',
            [new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Completed, 0, 0)],
            'O',
            0,
            -1,
            string.Empty,
            1409,
            "enemy2",
            "Enemy Mech Destroyed");
        WriteEntry(
            data.AsSpan(headerSize + recordSize * 5, recordSize),
            MechWarriorMissionAction.Wait,
            'V',
            [
                new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Succeeded, 3, 0),
                new MechWarriorMissionCondition(MechWarriorMissionConditionResult.Succeeded, 4, 0)
            ],
            'O',
            2,
            120,
            "gene002S",
            1403,
            "start",
            "Destroy all Enemy Mechs");
        return data;
    }

    private static void WriteEntry(
        Span<byte> data,
        MechWarriorMissionAction action,
        char visibility,
        IReadOnlyList<MechWarriorMissionCondition> conditions,
        char goalClass,
        byte goalFlags,
        short successIndex,
        string successName,
        short targetIndex,
        string targetName,
        string description)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data, (ushort)action);
        data[4] = (byte)visibility;
        BinaryPrimitives.WriteInt32LittleEndian(data[6..],
            conditions.Count > 1 ? (int)MechWarriorMissionConditionLogic.All : 0);
        for (var offset = 10; offset < 42; offset += 4)
        {
            data.Slice(offset, 4).Fill(0xff);
            data[offset + 3] = 0;
        }
        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            var offset = 10 + index * 4;
            data[offset] = (byte)condition.Result;
            data[offset + 1] = condition.ObjectiveIndex;
            data[offset + 2] = condition.TableIndex;
            data[offset + 3] = 0;
        }

        data[46] = goalFlags;
        data[47] = (byte)goalClass;
        WriteReference(data, 50, 52, successIndex, successName);
        WriteReference(data, 61, 63, -1, string.Empty);
        WriteReference(data, 72, 74, targetIndex, targetName);
        BinaryPrimitives.WriteInt16LittleEndian(data[83..], -1);
        BinaryPrimitives.WriteInt16LittleEndian(data[85..], -1);
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
