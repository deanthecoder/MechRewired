// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;
using MechRewired.Resources;
using NUnit.Framework;

namespace MechRewired.Tests.Resources;

/// <summary>
/// Verifies the lightweight original-data checks.
/// </summary>
/// <remarks>
/// Synthetic files keep commercial game data out of the test suite.
/// </remarks>
[TestFixture]
public sealed class MechWarriorResourceCheckTests
{
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
    public void GivenValidProjectArchiveCheckDosFilesReturnsArchive()
    {
        var archive = WriteProjectArchive("PROJ"u8);

        var result = MechWarriorResourceCheck.CheckDosFiles(m_tempDirectory);

        Assert.That(result.FullName, Is.EqualTo(archive.FullName));
    }

    [Test]
    public void GivenMissingProjectArchiveCheckDosFilesThrowsUsefulException()
    {
        var exception = Assert.Throws<FileNotFoundException>(() =>
            MechWarriorResourceCheck.CheckDosFiles(m_tempDirectory));

        Assert.That(exception.Message, Does.Contain(MechWarriorDataFile.ProjectArchive));
    }

    [Test]
    public void GivenWrongSignatureCheckDosFilesThrowsUsefulException()
    {
        WriteProjectArchive("NOPE"u8);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MechWarriorResourceCheck.CheckDosFiles(m_tempDirectory));

        Assert.That(exception.Message, Does.Contain("PROJ"));
    }

    [Test]
    public void GivenEmptyProjectArchiveCheckDosFilesThrowsUsefulException()
    {
        WriteProjectArchive([]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MechWarriorResourceCheck.CheckDosFiles(m_tempDirectory));

        Assert.That(exception.Message, Does.Contain("empty"));
    }

    private FileInfo WriteProjectArchive(ReadOnlySpan<byte> content)
    {
        var file = new FileInfo(Path.Combine(m_tempDirectory.FullName, MechWarriorDataFile.ProjectArchive));
        using var stream = file.Create();
        stream.Write(content);
        return file;
    }
}
