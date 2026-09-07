// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.IO.Compression;
using System.Text;
using DTC.Core;
using MechRewired.Resources;
using NUnit.Framework;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;

namespace MechRewired.Tests.Resources;

/// <summary>Checks original-data import without redistributing commercial resources.</summary>
/// <remarks>Synthetic project indexes exercise validation, selection and installation failures.</remarks>
[TestFixture]
public sealed class MechWarriorDataInstallerTests
{
    private TempDirectory m_temp;
    private DirectoryInfo Destination => new(Path.Combine(m_temp.FullName, "installed"));

    [SetUp]
    public void SetUp() => m_temp = new TempDirectory();

    [TearDown]
    public void TearDown() => m_temp.Dispose();

    [TestCase("bundle/disk/mech2/", false)]
    [TestCase("bundle\\disk\\mech2\\", false)]
    [TestCase("bundle/disk/mech2/", true)]
    [TestCase("bundle\\disk\\mech2\\", true)]
    public void NestedArchiveImportsOnlyRecognizedAdjacentFiles(string prefix, bool sevenZip)
    {
        var zip = WritePackage(sevenZip, (prefix + "mw2.prj", ProjectBytes()),
            (prefix + "demodata/firelogo.mw2", new byte[] { 1, 2 }),
            (prefix + "demodata/amwlogo1.smk", new byte[] { 3, 4 }),
            (prefix + "demodata/clanselect_center.png", new byte[] { 6, 7 }),
            (prefix + "MECH2.EXE", new byte[] { 5 }),
            ("elsewhere/DEMODATA/FIRELOGO.MW2", new byte[] { 9 }),
            ("../MW2.PRJ", new byte[] { 0 }));

        var file = MechWarriorDataInstaller.Install(zip, Destination);

        Assert.That(MechWarriorDataInstaller.OpenValidatedArchive(file).Entries, Has.Count.EqualTo(9));
        Assert.That(File.ReadAllBytes(Path.Combine(Destination.FullName, "DEMODATA", "FIRELOGO.MW2")), Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(File.ReadAllBytes(Path.Combine(Destination.FullName, "DEMODATA", "AMWLOGO1.SMK")), Is.EqualTo(new byte[] { 3, 4 }));
        Assert.That(File.ReadAllBytes(Path.Combine(Destination.FullName, "DEMODATA", "CLANSELECT_CENTER.png")), Is.EqualTo(new byte[] { 6, 7 }));
        Assert.That(Directory.GetFiles(Destination.FullName, "*", SearchOption.AllDirectories), Has.Length.EqualTo(4));
        Assert.That(File.Exists(Path.Combine(m_temp.FullName, "MW2.PRJ")), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FolderAndDirectFileImportIncludeAdjacentTitleMedia(bool direct)
    {
        var directory = Directory.CreateDirectory(Path.Combine(m_temp.FullName, "source", "disk", "MECH2"));
        var project = Path.Combine(directory.FullName, "mw2.prj");
        File.WriteAllBytes(project, ProjectBytes());
        var media = Directory.CreateDirectory(Path.Combine(directory.FullName, "demodata"));
        File.WriteAllBytes(Path.Combine(media.FullName, "amwlogo1.smk"), [8]);

        var file = MechWarriorDataInstaller.Install(direct ? project : directory.Parent.Parent.FullName, Destination);

        Assert.That(file.Name, Is.EqualTo("MW2.PRJ"));
        Assert.That(File.Exists(Path.Combine(Destination.FullName, "DEMODATA", "AMWLOGO1.SMK")), Is.True);
        Assert.That(File.Exists(project), Is.True);
    }

    [Test]
    public void AmbiguousPackagesExplainHowToChooseOneInstallation()
    {
        var zip = WriteZip(("DOS/MW2.PRJ", ProjectBytes()), ("other/MW2.PRJ", ProjectBytes()));
        var error = Assert.Throws<InvalidDataException>(() => MechWarriorDataInstaller.Install(zip, Destination));
        Assert.That(error.Message, Does.Contain("Found 2 MW2.PRJ"));
        Assert.That(Destination.Exists, Is.False);
    }

    [Test]
    public void BadImportLeavesExistingInstallationUntouched()
    {
        Destination.Create();
        var original = ProjectBytes();
        File.WriteAllBytes(Path.Combine(Destination.FullName, "MW2.PRJ"), original);
        File.WriteAllText(Path.Combine(Destination.FullName, "keep.txt"), "existing");
        var zip = WriteZip(("MW2.PRJ", "PROJ"u8.ToArray()));

        Assert.Throws<InvalidDataException>(() => MechWarriorDataInstaller.Install(zip, Destination));

        Assert.That(File.ReadAllBytes(Path.Combine(Destination.FullName, "MW2.PRJ")), Is.EqualTo(original));
        Assert.That(File.ReadAllText(Path.Combine(Destination.FullName, "keep.txt")), Is.EqualTo("existing"));
        Assert.That(Directory.GetDirectories(m_temp.FullName, ".installed.*"), Is.Empty);
    }

    [Test]
    public void SuccessfulImportReplacesExistingDataWithoutLeavingBackup()
    {
        Destination.Create();
        File.WriteAllText(Path.Combine(Destination.FullName, "MW2.PRJ"), "old");
        var file = MechWarriorDataInstaller.Install(WriteZip(("MW2.PRJ", ProjectBytes())), Destination);
        Assert.That(MechWarriorDataInstaller.OpenValidatedArchive(file).Entries, Has.Count.EqualTo(9));
        Assert.That(Directory.GetDirectories(m_temp.FullName, ".installed.*"), Is.Empty);
    }

    [Test]
    public void StructurallyValidDemoIsRejectedBeforeInstall()
    {
        var zip = WriteZip(("MW2.PRJ", ProjectBytes(true)));
        var error = Assert.Throws<InvalidDataException>(() => MechWarriorDataInstaller.Install(zip, Destination));
        Assert.That(error.Message, Does.Contain("incompatible edition or demo"));
        Assert.That(error.Message, Does.Contain("PINKSCN1.BWD"));
    }

    [TestCase("../MW2.PRJ")]
    [TestCase("/MW2.PRJ")]
    [TestCase("C:\\MW2.PRJ")]
    public void UnsafePathsCannotSupplyTheArchive(string path)
    {
        var zip = WriteZip((path, ProjectBytes()));
        Assert.Throws<InvalidDataException>(() => MechWarriorDataInstaller.Install(zip, Destination));
        Assert.That(Destination.Exists, Is.False);
    }

    [Test]
    public void OversizedTitleMediaIsRejectedWithoutReplacingData()
    {
        var zip = WriteZip(("MW2.PRJ", ProjectBytes()), ("DEMODATA/FIRELOGO.MW2", new byte[8 * 1024 * 1024 + 1]));
        var error = Assert.Throws<InvalidDataException>(() => MechWarriorDataInstaller.Install(zip, Destination));
        Assert.That(error.Message, Does.Contain("size limit"));
        Assert.That(Destination.Exists, Is.False);
    }

    [Test]
    public void InstallerOnlyDemoZipReportsMissingProject()
    {
        var zip = WriteZip(("mech2dem.exe", new byte[] { 1 }));
        var error = Assert.Throws<InvalidDataException>(() => MechWarriorDataInstaller.Install(zip, Destination));
        Assert.That(error.Message, Does.Contain("No MW2.PRJ"));
    }

    [Test]
    public void MissingMechCatalogIsRejectedBeforeClanSelection()
    {
        var bytes = ProjectBytes();
        var name = Encoding.ASCII.GetBytes("MECH.MTB");
        var offset = bytes.AsSpan().IndexOf(name);
        bytes[offset] = (byte)'X';
        var error = Assert.Throws<InvalidDataException>(() =>
            MechWarriorDataInstaller.Install(WriteZip(("MW2.PRJ", bytes)), Destination));
        Assert.That(error.Message, Does.Contain("MTAB/MECH.MTB"));
    }

    [Test]
    public void AmbiguousSevenZipPreservesExistingData()
    {
        Destination.Create();
        File.WriteAllText(Path.Combine(Destination.FullName, "keep.txt"), "existing");
        var package = WritePackage(true, ("one/MW2.PRJ", ProjectBytes()), ("two/MW2.PRJ", ProjectBytes()));
        Assert.Throws<InvalidDataException>(() => MechWarriorDataInstaller.Install(package, Destination));
        Assert.That(File.ReadAllText(Path.Combine(Destination.FullName, "keep.txt")), Is.EqualTo("existing"));
    }

    private string WritePackage(bool sevenZip, params (string Path, byte[] Bytes)[] files)
    {
        if (!sevenZip)
            return WriteZip(files);
        var path = Path.Combine(m_temp.FullName, "source.7z");
        using var output = File.Create(path);
        using var writer = WriterFactory.OpenWriter(output, ArchiveType.SevenZip,
            new SevenZipWriterOptions(CompressionType.LZMA2));
        foreach (var (name, bytes) in files)
        {
            using var input = new MemoryStream(bytes);
            writer.Write(name, input, DateTime.UtcNow);
        }
        return path;
    }

    private string WriteZip(params (string Path, byte[] Bytes)[] files)
    {
        var path = Path.Combine(m_temp.FullName, "source.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in files)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }
        return path;
    }

    private static byte[] ProjectBytes(bool demo = false)
    {
        string[] paths = ["BWD/PINKSCN1.BWD", "BWD/YELLSCN1.BWD", "MEK/STM01STD.MEK", "MEK/MDG00STD.MEK",
            "PAL/CIND_DA.COL", "CEL/L2JADEFN.XEL", "CEL/L2WOLFCL.XEL", "SNDS/MECFIRE1.WAV", "MTAB/MECH.MTB"];
        if (demo)
            paths[0] = "BWD/DEMO.BWD";
        var groups = paths.GroupBy(path => path.Split('/')[0]).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write(new byte[50 + groups.Length * 24]);
        stream.Position = 0;
        writer.Write("PROJ"u8);
        stream.Position = 0x18;
        writer.Write((ushort)(groups.Length + 1));
        for (var g = 0; g < groups.Length; g++)
        {
            var directoryOffset = stream.Length;
            stream.Position = 50 + g * 24;
            WriteAscii(writer, groups[g].Key, 4);
            writer.Write((uint)directoryOffset);
            stream.Position = directoryOffset;
            var entries = groups[g].ToArray();
            writer.Write(new byte[22 + entries.Length * 8]);
            stream.Position = directoryOffset;
            writer.Write("INDX"u8);
            stream.Position = directoryOffset + 20;
            writer.Write((ushort)entries.Length);
            for (var e = 0; e < entries.Length; e++)
            {
                var localOffset = stream.Length;
                stream.Position = directoryOffset + 22 + e * 8;
                writer.Write((uint)localOffset);
                writer.Write((uint)63);
                stream.Position = localOffset;
                writer.Write(new byte[63]);
                stream.Position = localOffset;
                writer.Write("DATA"u8);
                stream.Position = localOffset + 46;
                WriteAscii(writer, entries[e].Split('/')[1], 16);
            }
        }
        return stream.ToArray();
    }

    private static void WriteAscii(BinaryWriter writer, string text, int count)
    {
        var bytes = new byte[count];
        Encoding.ASCII.GetBytes(text.AsSpan(), bytes);
        writer.Write(bytes);
    }
}
