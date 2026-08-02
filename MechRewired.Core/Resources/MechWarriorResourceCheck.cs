// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Extensions;

namespace MechRewired.Resources;

/// <summary>
/// Performs the initial lightweight check of original MechWarrior 2 data.
/// </summary>
/// <remarks>
/// This deliberately checks only the DOS archive's presence, size and signature so development can fail early without duplicating the archive reader.
/// </remarks>
public static class MechWarriorResourceCheck
{
    private static ReadOnlySpan<byte> ProjectSignature => "PROJ"u8;

    /// <summary>
    /// Checks the required DOS files and returns the project archive.
    /// </summary>
    public static FileInfo CheckDosFiles(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!directory.Exists())
        {
            throw new DirectoryNotFoundException($"The MechWarrior 2 data directory was not found: {directory.FullName}");
        }

        var projectArchive = directory.GetFile(MechWarriorDataFile.ProjectArchive);
        if (!projectArchive.Exists())
        {
            throw new FileNotFoundException(
                $"Required MechWarrior 2 data file {MechWarriorDataFile.ProjectArchive} was not found in {directory.FullName}.",
                Path.Combine(directory.FullName, MechWarriorDataFile.ProjectArchive));
        }

        if (projectArchive.Length < ProjectSignature.Length)
        {
            throw new InvalidDataException($"{projectArchive.Name} is empty or too short to be a MechWarrior 2 project archive.");
        }

        Span<byte> signature = stackalloc byte[ProjectSignature.Length];
        using var stream = projectArchive.OpenRead();
        stream.ReadExactly(signature);
        if (!signature.SequenceEqual(ProjectSignature))
        {
            throw new InvalidDataException($"{projectArchive.Name} does not have the expected PROJ signature.");
        }

        return projectArchive;
    }
}
