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
/// Decodes the original <c>MECH.MTB</c> catalogue used to associate a configured MEK with its chassis.
/// </summary>
/// <remarks>
/// The configuration's first three filename characters are its catalogue abbreviation: for example,
/// <c>MDG00STD.MEK</c> resolves through <c>mdg</c> to the <c>maddog</c> BWD chassis.
/// </remarks>
public sealed class MechWarriorMechCatalog
{
    private const int HeaderSize = 4;
    private const int RecordSize = 45;
    private const int AbbreviationOffset = 3;
    private const int AbbreviationLength = 4;
    private const int ResourceNameOffset = 7;
    private const int ResourceNameLength = 9;
    private const int DisplayNameOffset = 16;
    private const int DisplayNameLength = 25;
    private const int TonnageOffset = 41;

    private MechWarriorMechCatalog(IReadOnlyList<MechWarriorMechCatalogEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<MechWarriorMechCatalogEntry> Entries { get; }

    /// <summary>Loads the unique MECH.MTB resource from a project archive.</summary>
    public static MechWarriorMechCatalog Load(MechWarriorProjectArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var entry = archive.Entries.SingleOrDefault(candidate =>
                        candidate.Name.Equals("MECH.MTB", StringComparison.OrdinalIgnoreCase)) ??
                    throw new FileNotFoundException("The project archive does not contain MECH.MTB.");
        return Load(archive.ReadEntry(entry));
    }

    /// <summary>Loads an MECH.MTB payload.</summary>
    public static MechWarriorMechCatalog Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException($"The MECH.MTB payload is {data.Length} bytes; a header is required.");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (count <= 0)
        {
            throw new InvalidDataException($"The MECH.MTB entry count must be positive; found {count}.");
        }

        var requiredLength = checked(HeaderSize + count * RecordSize);
        if (data.Length != requiredLength)
        {
            throw new InvalidDataException(
                $"The MECH.MTB payload is {data.Length} bytes; {count} entries require {requiredLength} bytes.");
        }

        var entries = new List<MechWarriorMechCatalogEntry>(count);
        for (var index = 0; index < count; index++)
        {
            var record = data.Slice(HeaderSize + index * RecordSize, RecordSize);
            var abbreviation = ReadAscii(record, AbbreviationOffset, AbbreviationLength);
            var resourceName = ReadAscii(record, ResourceNameOffset, ResourceNameLength);
            var displayName = ReadAscii(record, DisplayNameOffset, DisplayNameLength);
            var tonnage = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(TonnageOffset, sizeof(int)));
            if (string.IsNullOrWhiteSpace(abbreviation) ||
                string.IsNullOrWhiteSpace(resourceName) ||
                string.IsNullOrWhiteSpace(displayName) ||
                tonnage <= 0)
            {
                throw new InvalidDataException($"The MECH.MTB entry {index} is incomplete or invalid.");
            }

            entries.Add(new MechWarriorMechCatalogEntry(
                abbreviation,
                resourceName,
                displayName,
                tonnage));
        }

        return new MechWarriorMechCatalog(entries.AsReadOnly());
    }

    /// <summary>Resolves a configured MEK filename to its authored catalogue chassis.</summary>
    public MechWarriorMechCatalogEntry ResolveConfiguration(string mechConfigurationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mechConfigurationName);
        var name = Path.GetFileNameWithoutExtension(mechConfigurationName);
        if (name.Length < 3)
        {
            throw new InvalidDataException(
                $"MEK configuration {mechConfigurationName} does not contain a three-character chassis abbreviation.");
        }

        var abbreviation = name[..3];
        return Entries.SingleOrDefault(entry =>
                   entry.Abbreviation.Equals(abbreviation, StringComparison.OrdinalIgnoreCase)) ??
               throw new InvalidDataException(
                   $"MEK configuration {mechConfigurationName} has no MECH.MTB chassis for '{abbreviation}'.");
    }

    private static string ReadAscii(ReadOnlySpan<byte> data, int offset, int length)
    {
        var value = Encoding.ASCII.GetString(data.Slice(offset, length));
        var terminator = value.IndexOf('\0');
        return (terminator < 0 ? value : value[..terminator]).TrimEnd();
    }
}

/// <summary>One authored chassis identity from MECH.MTB.</summary>
public sealed record MechWarriorMechCatalogEntry(
    string Abbreviation,
    string ResourceName,
    string DisplayName,
    int Tonnage);
