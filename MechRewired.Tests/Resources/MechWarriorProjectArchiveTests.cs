// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using DTC.Core;
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

/// <summary>
/// Verifies project archive indexing and bounded resource reads.
/// </summary>
/// <remarks>
/// A tiny synthetic archive exercises the real binary layout without distributing game data.
/// </remarks>
[TestFixture]
public sealed class MechWarriorProjectArchiveTests
{
    private const int DirectoryOffset = 74;
    private const int LocalHeaderOffset = 112;
    private const int LocalHeaderSize = 62;

    private TempDirectory m_tempDirectory;

    [SetUp]
    public void SetUp()
    {
        m_tempDirectory = new TempDirectory();
    }

    [TearDown]
    public void TearDown()
    {
        m_tempDirectory.Dispose();
    }

    [Test]
    public void OpenIndexesDirectoryQualifiedEntriesAndReadsTheirPayload()
    {
        byte[] expected = [1, 3, 3, 7];
        var file = WriteArchive("TEST.COL", expected);

        var archive = MechWarriorProjectArchive.Open(file);
        var entry = archive.GetEntry("pal\\test.col");

        Assert.That(entry.Path, Is.EqualTo("PAL/TEST.COL"));
        Assert.That(entry.Size, Is.EqualTo(expected.Length));
        Assert.That(archive.ReadEntry(entry), Is.EqualTo(expected));
    }

    [Test]
    public void OpenRejectsAnEntryOutsideTheArchiveWithDirectoryContext()
    {
        var file = WriteArchive("TEST.COL", [1]);
        using (var stream = file.Open(FileMode.Open, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Position = DirectoryOffset + 22 + 8;
            writer.Write(uint.MaxValue);
        }

        var exception = Assert.Throws<InvalidDataException>(() => MechWarriorProjectArchive.Open(file));

        Assert.That(exception.Message, Does.Contain("PAL entry 1"));
    }

    [Test]
    public void GetEntryReportsTheMissingResourceAndArchive()
    {
        var file = WriteArchive("TEST.COL", [1]);
        var archive = MechWarriorProjectArchive.Open(file);

        var exception = Assert.Throws<FileNotFoundException>(() => archive.GetEntry("PAL/MISSING.COL"));

        Assert.That(exception.Message, Does.Contain("PAL/MISSING.COL"));
        Assert.That(exception.Message, Does.Contain(MechWarriorDataFile.ProjectArchive));
    }

    private FileInfo WriteArchive(string resourceName, ReadOnlySpan<byte> payload)
    {
        var file = new FileInfo(Path.Combine(m_tempDirectory.FullName, MechWarriorDataFile.ProjectArchive));
        using var stream = file.Create();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);

        WriteFixedAscii(writer, "PROJ", 4);
        writer.Write((uint)0);
        stream.Position = 0x18;
        writer.Write((ushort)2);

        stream.Position = 50;
        WriteFixedAscii(writer, "PAL", 4);
        writer.Write((uint)DirectoryOffset);
        writer.Write(new byte[16]);

        stream.Position = DirectoryOffset;
        WriteFixedAscii(writer, "INDX", 4);
        writer.Write((uint)30);
        writer.Write((uint)0);
        WriteFixedAscii(writer, "PAL", 4);
        writer.Write((ushort)2);
        writer.Write((ushort)2);
        writer.Write((ushort)2);
        writer.Write((ulong)0);
        writer.Write((uint)LocalHeaderOffset);
        writer.Write((uint)(LocalHeaderSize + payload.Length));

        stream.Position = LocalHeaderOffset;
        WriteFixedAscii(writer, "DATA", 4);
        writer.Write((uint)(LocalHeaderSize + payload.Length - 8));
        writer.Write((uint)0);
        WriteFixedAscii(writer, "PAL", 4);
        writer.Write((ulong)0);
        writer.Write((ushort)2);
        writer.Write((uint)0);
        WriteFixedAscii(writer, Path.GetFileNameWithoutExtension(resourceName), 16);
        WriteFixedAscii(writer, resourceName, 16);
        writer.Write(payload);

        var archiveLength = stream.Length;
        stream.Position = 4;
        writer.Write((uint)(archiveLength - 8));
        return file;
    }

    private static void WriteFixedAscii(BinaryWriter writer, string value, int length)
    {
        var data = new byte[length];
        Encoding.ASCII.GetBytes(value.AsSpan(), data);
        writer.Write(data);
    }
}
