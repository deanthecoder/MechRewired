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
using SharpCompress.Archives.SevenZip;

namespace MechRewired.Resources;

/// <summary>
/// Safely imports the small set of original data files used by MechRewired.
/// </summary>
/// <remarks>
/// Imports are staged beside the destination so a bad download or incompatible edition cannot replace an existing install.
/// </remarks>
public static class MechWarriorDataInstaller
{
    private const int MaximumScannedEntries = 10_000;
    private const long MaximumProjectArchiveBytes = 256L * 1024 * 1024;
    private const long MaximumMovieBytes = 64L * 1024 * 1024;
    private const long MaximumTitleBytes = 8L * 1024 * 1024;

    private static readonly string[] RequiredResources =
    [
        "BWD/PINKSCN1.BWD", "BWD/YELLSCN1.BWD",
        "MEK/STM01STD.MEK", "MEK/MDG00STD.MEK",
        "PAL/CIND_DA.COL", "CEL/L2JADEFN.XEL",
        "CEL/L2WOLFCL.XEL", "SNDS/MECFIRE1.WAV", "MTAB/MECH.MTB"
    ];

    /// <summary>
    /// Imports one compatible DOS archive from a folder, ZIP or 7z package, or direct <c>MW2.PRJ</c> file.
    /// </summary>
    public static FileInfo Install(string sourcePath, DirectoryInfo targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(targetDirectory);

        var source = Path.GetFullPath(sourcePath);
        var target = new DirectoryInfo(Path.GetFullPath(targetDirectory.FullName));
        var parent = target.Parent ?? throw new InvalidOperationException("The data directory must have a parent directory.");
        parent.Create();
        var staging = new DirectoryInfo(Path.Combine(parent.FullName, $".{target.Name}.import-{Guid.NewGuid():N}"));
        DirectoryInfo backup = null;

        try
        {
            staging.Create();
            if (Directory.Exists(source))
            {
                CopyFromDirectory(new DirectoryInfo(source), staging);
            }
            else if (File.Exists(source))
            {
                var file = new FileInfo(source);
                if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    CopyFromZip(file, staging);
                }
                else if (file.Extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
                {
                    CopyFromSevenZip(file, staging);
                }
                else if (file.Name.Equals(MechWarriorDataFile.ProjectArchive, StringComparison.OrdinalIgnoreCase))
                {
                    CopyFromFiles([file], staging);
                }
                else
                {
                    throw new InvalidDataException("Select a MECH2 folder, a ZIP or 7z package, or the MW2.PRJ file.");
                }
            }
            else
            {
                throw new FileNotFoundException($"The selected MechWarrior 2 source was not found: {source}", source);
            }

            var installed = new FileInfo(Path.Combine(staging.FullName, MechWarriorDataFile.ProjectArchive));
            OpenValidatedArchive(installed);

            if (Directory.Exists(target.FullName))
            {
                backup = new DirectoryInfo(Path.Combine(parent.FullName, $".{target.Name}.backup-{Guid.NewGuid():N}"));
                Directory.Move(target.FullName, backup.FullName);
            }

            Directory.Move(staging.FullName, target.FullName);
            TryDelete(backup);
            return new FileInfo(Path.Combine(target.FullName, MechWarriorDataFile.ProjectArchive));
        }
        catch
        {
            if (backup != null && Directory.Exists(backup.FullName) && !Directory.Exists(target.FullName))
            {
                Directory.Move(backup.FullName, target.FullName);
            }

            throw;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Opens a project archive and verifies that it contains the DOS campaign resources MechRewired requires.
    /// </summary>
    /// <remarks>
    /// This compatibility gate checks resource presence only; it cannot guarantee that every resource will load successfully.
    /// </remarks>
    public static MechWarriorProjectArchive OpenValidatedArchive(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The MW2.PRJ archive was not found.", file.FullName);
        }

        if (file.Length > MaximumProjectArchiveBytes)
        {
            throw new InvalidDataException($"MW2.PRJ exceeds the {MaximumProjectArchiveBytes / 1024 / 1024} MB import limit.");
        }

        MechWarriorProjectArchive archive;
        try
        {
            archive = MechWarriorProjectArchive.Open(file);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            throw new InvalidDataException("MW2.PRJ is truncated or is not a supported DOS project archive.", exception);
        }

        var missing = RequiredResources.Where(resource => !archive.Entries.Any(entry =>
            entry.Path.Equals(resource, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"MW2.PRJ is an incompatible edition or demo. It is missing required campaign resources: {string.Join(", ", missing)}. Select the full DOS MECH2 folder or its MW2.PRJ file.");
        }

        return archive;
    }

    private static void CopyFromDirectory(DirectoryInfo source, DirectoryInfo staging)
    {
        var files = EnumerateFiles(source).ToArray();
        var candidates = files.Where(file => file.Name.Equals(MechWarriorDataFile.ProjectArchive, StringComparison.OrdinalIgnoreCase)).ToArray();
        CopyFromFiles(candidates, staging, files);
    }

    private static void CopyFromFiles(FileInfo[] candidates, DirectoryInfo staging, FileInfo[] allFiles = null)
    {
        var project = SelectOne(candidates);
        CopyFile(project, new FileInfo(Path.Combine(staging.FullName, MechWarriorDataFile.ProjectArchive)), MaximumProjectArchiveBytes);
        var siblingFiles = allFiles ?? EnumerateFiles(project.Directory).ToArray();
        foreach (var (name, limit) in new[]
                 {
                     ("AMWLOGO1.SMK", MaximumMovieBytes),
                     ("FIRELOGO.MW2", MaximumTitleBytes),
                     ("CLANSELECT_CENTER.png", MaximumTitleBytes)
                 })
        {
            CopyOptionalFile(siblingFiles, Path.Combine(project.DirectoryName, "DEMODATA"), name,
                staging, Path.Combine("DEMODATA", name), limit);
        }
    }

    private static void CopyFromZip(FileInfo source, DirectoryInfo staging)
    {
        using var archive = ZipFile.OpenRead(source.FullName);
        if (archive.Entries.Count > MaximumScannedEntries)
        {
            throw new InvalidDataException($"The ZIP contains more than {MaximumScannedEntries:N0} entries.");
        }

        var entries = archive.Entries.Select(entry => new ZipCandidate(entry, NormalizeZipPath(entry.FullName))).Where(candidate => candidate.Path != null).ToArray();
        var candidates = entries.Where(candidate => Path.GetFileName(candidate.Path).Equals(MechWarriorDataFile.ProjectArchive, StringComparison.OrdinalIgnoreCase)).ToArray();
        var project = SelectOne(candidates.Select(candidate => candidate.Entry).ToArray());
        var projectPath = entries.Single(candidate => candidate.Entry == project).Path;
        CopyZipEntry(project, new FileInfo(Path.Combine(staging.FullName, MechWarriorDataFile.ProjectArchive)), MaximumProjectArchiveBytes);
        var prefix = projectPath[..(projectPath.LastIndexOf('/') + 1)];
        CopyOptionalZipEntry(entries, prefix + "DEMODATA/AMWLOGO1.SMK", staging, Path.Combine("DEMODATA", "AMWLOGO1.SMK"), MaximumMovieBytes);
        CopyOptionalZipEntry(entries, prefix + "DEMODATA/FIRELOGO.MW2", staging, Path.Combine("DEMODATA", "FIRELOGO.MW2"), MaximumTitleBytes);
        CopyOptionalZipEntry(entries, prefix + "DEMODATA/CLANSELECT_CENTER.png", staging,
            Path.Combine("DEMODATA", "CLANSELECT_CENTER.png"), MaximumTitleBytes);
    }

    private static void CopyFromSevenZip(FileInfo source, DirectoryInfo staging)
    {
        using var archive = SevenZipArchive.OpenArchive(source.FullName);
        var entries = archive.Entries.Take(MaximumScannedEntries + 1).ToArray();
        if (entries.Length > MaximumScannedEntries || entries.Sum(entry => entry.Size) > 4L * 1024 * 1024 * 1024)
            throw new InvalidDataException("The 7z package is too large. Select the extracted MECH2 folder instead.");

        var candidates = entries.Where(entry => !entry.IsDirectory &&
            NormalizeZipPath(entry.Key) is { } path &&
            Path.GetFileName(path).Equals(MechWarriorDataFile.ProjectArchive, StringComparison.OrdinalIgnoreCase)).ToArray();
        var project = SelectOne(candidates);
        var projectPath = NormalizeZipPath(project.Key);
        var prefix = projectPath[..(projectPath.LastIndexOf('/') + 1)];
        var expected = new Dictionary<string, (string Destination, long Limit)>(StringComparer.OrdinalIgnoreCase)
        {
            [projectPath] = (MechWarriorDataFile.ProjectArchive, MaximumProjectArchiveBytes),
            [prefix + "DEMODATA/AMWLOGO1.SMK"] = (Path.Combine("DEMODATA", "AMWLOGO1.SMK"), MaximumMovieBytes),
            [prefix + "DEMODATA/FIRELOGO.MW2"] = (Path.Combine("DEMODATA", "FIRELOGO.MW2"), MaximumTitleBytes),
            [prefix + "DEMODATA/CLANSELECT_CENTER.png"] =
                (Path.Combine("DEMODATA", "CLANSELECT_CENTER.png"), MaximumTitleBytes)
        };
        foreach (var path in expected.Keys)
        {
            var matches = entries.Where(entry => !entry.IsDirectory &&
                string.Equals(NormalizeZipPath(entry.Key), path, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
                throw new InvalidDataException($"The 7z package contains duplicate {path} files.");
            if (matches.Any(entry => entry.IsEncrypted || entry.Size > expected[path].Limit))
                throw new InvalidDataException($"{path} is encrypted or exceeds the import size limit.");
        }

        // Solid archives must be decoded in order; never extract unrelated entries to disk.
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory || NormalizeZipPath(reader.Entry.Key) is not { } path ||
                !expected.TryGetValue(path, out var target))
                continue;
            var destination = new FileInfo(Path.Combine(staging.FullName, target.Destination));
            destination.Directory.Create();
            using var input = reader.OpenEntryStream();
            using var output = destination.Create();
            CopyBounded(input, output, target.Limit, path);
        }
    }

    private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        var count = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (var file in directory.GetFiles())
            {
                if (++count > MaximumScannedEntries)
                {
                    throw new InvalidDataException($"The selected folder contains more than {MaximumScannedEntries:N0} files.");
                }

                if ((file.Attributes & FileAttributes.ReparsePoint) == 0)
                    yield return file;
            }

            foreach (var child in directory.GetDirectories())
            {
                if (++count > MaximumScannedEntries)
                    throw new InvalidDataException("The selected folder contains too many entries. Select the MECH2 folder directly.");
                if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static T SelectOne<T>(T[] candidates) where T : class
    {
        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        var reason = candidates.Length == 0 ? "No MW2.PRJ file was found" : $"Found {candidates.Length} MW2.PRJ files";
        throw new InvalidDataException($"{reason}. Select the full DOS MECH2 folder or a single MW2.PRJ file.");
    }

    private static void CopyOptionalFile(IEnumerable<FileInfo> files, string directory, string name, DirectoryInfo staging, string destination, long maximumBytes)
    {
        var matches = files.Where(file => file.DirectoryName.Equals(directory, StringComparison.OrdinalIgnoreCase) && file.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException($"The selected package contains multiple {name} files beside MW2.PRJ.");
        }

        if (matches.Length == 1)
        {
            CopyFile(matches[0], new FileInfo(Path.Combine(staging.FullName, destination)), maximumBytes);
        }
    }

    private static void CopyOptionalZipEntry(IEnumerable<ZipCandidate> entries, string path, DirectoryInfo staging, string destination, long maximumBytes)
    {
        var matches = entries.Where(candidate => candidate.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException($"The selected ZIP contains multiple {Path.GetFileName(path)} files beside MW2.PRJ.");
        }

        if (matches.Length == 1)
        {
            CopyZipEntry(matches[0].Entry, new FileInfo(Path.Combine(staging.FullName, destination)), maximumBytes);
        }
    }

    private static void CopyFile(FileInfo source, FileInfo destination, long maximumBytes)
    {
        if (source.Length > maximumBytes)
        {
            throw new InvalidDataException($"{source.Name} exceeds the import size limit.");
        }

        destination.Directory.Create();
        using var input = source.OpenRead();
        using var output = destination.Create();
        CopyBounded(input, output, maximumBytes, source.Name);
    }

    private static void CopyZipEntry(ZipArchiveEntry source, FileInfo destination, long maximumBytes)
    {
        if (source.Length > maximumBytes)
        {
            throw new InvalidDataException($"{source.Name} exceeds the import size limit.");
        }

        destination.Directory.Create();
        using var input = source.Open();
        using var output = destination.Create();
        CopyBounded(input, output, maximumBytes, source.Name);
    }

    private static void CopyBounded(Stream input, Stream output, long maximumBytes, string name)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException($"{name} exceeds the import size limit.");
            }

            output.Write(buffer, 0, read);
        }
    }

    private static string NormalizeZipPath(string path)
    {
        if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':'))
            return null;
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is "." or ".."))
        {
            return null;
        }

        return string.Join('/', parts);
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            if (directory != null && Directory.Exists(directory.FullName))
                directory.Delete(true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ZipCandidate(ZipArchiveEntry Entry, string Path);
}
