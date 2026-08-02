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
/// Indexes and reads named resources from an original MechWarrior 2 <c>MW2.PRJ</c> archive.
/// </summary>
/// <remarks>
/// The reader is intentionally read-only and validates every offset before exposing an entry.
/// </remarks>
public sealed class MechWarriorProjectArchive
{
    private const int MainDirectoryCountOffset = 0x18;
    private const int MainDirectoryOffset = 50;
    private const int MainDirectoryEntrySize = 24;
    private const int DirectoryHeaderSize = 22;
    private const int DirectoryEntrySize = 8;
    private const int LocalFileHeaderSize = 62;
    private const int LocalFileNameOffset = 46;
    private const int LocalFileNameLength = 16;

    private readonly FileInfo m_file;
    private readonly Dictionary<string, MechWarriorProjectEntry> m_entriesByPath;
    private readonly Dictionary<(string DirectoryName, int Index), MechWarriorProjectEntry> m_entriesByIndex;

    private MechWarriorProjectArchive(FileInfo file, IReadOnlyList<MechWarriorProjectEntry> entries)
    {
        m_file = file;
        Entries = entries;
        m_entriesByPath = entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        m_entriesByIndex = entries.ToDictionary(
            entry => (entry.DirectoryName.ToUpperInvariant(), entry.Index));
    }

    public IReadOnlyList<MechWarriorProjectEntry> Entries { get; }

    /// <summary>
    /// Opens an archive and builds its resource index.
    /// </summary>
    public static MechWarriorProjectArchive Open(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var stream = file.OpenRead();
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        var entries = ReadEntries(reader);
        return new MechWarriorProjectArchive(file, entries);
    }

    /// <summary>
    /// Finds an entry by its directory-qualified path, ignoring case.
    /// </summary>
    public MechWarriorProjectEntry GetEntry(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = path.Replace('\\', '/');
        if (!m_entriesByPath.TryGetValue(normalizedPath, out var entry))
        {
            throw new FileNotFoundException($"Resource {normalizedPath} was not found in {m_file.Name}.", normalizedPath);
        }

        return entry;
    }

    /// <summary>
    /// Finds an entry using the directory-local index stored in another MW2 resource.
    /// </summary>
    public MechWarriorProjectEntry GetEntry(string directoryName, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        if (!m_entriesByIndex.TryGetValue((directoryName.ToUpperInvariant(), index), out var entry))
        {
            throw new FileNotFoundException(
                $"Resource {directoryName} entry {index} was not found in {m_file.Name}.");
        }

        return entry;
    }

    /// <summary>
    /// Reads one resource payload without extracting it to disk.
    /// </summary>
    public byte[] ReadEntry(MechWarriorProjectEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!m_entriesByPath.TryGetValue(entry.Path, out var indexedEntry) || indexedEntry != entry)
        {
            throw new ArgumentException("The resource entry does not belong to this project archive.", nameof(entry));
        }

        var data = new byte[indexedEntry.Size];
        using var stream = m_file.OpenRead();
        stream.Position = indexedEntry.Offset;
        stream.ReadExactly(data);
        return data;
    }

    private static IReadOnlyList<MechWarriorProjectEntry> ReadEntries(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        EnsureRange(stream, 0, MainDirectoryOffset, "project header");
        if (ReadAscii(reader, 0, 4) != "PROJ")
        {
            throw new InvalidDataException("The project archive does not have the expected PROJ signature.");
        }

        var mainDirectoryCount = ReadUInt16(reader, MainDirectoryCountOffset);
        if (mainDirectoryCount == 0)
        {
            throw new InvalidDataException("The project archive contains no main directories.");
        }

        // MW2's count includes the FREE record preceding the usable directory table.
        var usableDirectoryCount = mainDirectoryCount - 1;
        EnsureRange(
            stream,
            MainDirectoryOffset,
            checked(usableDirectoryCount * MainDirectoryEntrySize),
            "main directory table");

        var entries = new List<MechWarriorProjectEntry>();
        for (var directoryIndex = 0; directoryIndex < usableDirectoryCount; directoryIndex++)
        {
            var mainEntryOffset = MainDirectoryOffset + directoryIndex * MainDirectoryEntrySize;
            var directoryName = ReadAscii(reader, mainEntryOffset, 4);
            var directoryOffset = ReadUInt32(reader, mainEntryOffset + 4);
            ReadDirectory(reader, directoryName, directoryOffset, entries);
        }

        return entries.AsReadOnly();
    }

    private static void ReadDirectory(
        BinaryReader reader,
        string directoryName,
        long directoryOffset,
        ICollection<MechWarriorProjectEntry> entries)
    {
        var stream = reader.BaseStream;
        EnsureRange(stream, directoryOffset, DirectoryHeaderSize, $"{directoryName} directory header");
        if (ReadAscii(reader, directoryOffset, 4) != "INDX")
        {
            throw new InvalidDataException($"Directory {directoryName} does not have the expected INDX signature.");
        }

        var directoryEntryCount = ReadUInt16(reader, directoryOffset + 20);
        var directoryEntriesOffset = directoryOffset + DirectoryHeaderSize;
        EnsureRange(
            stream,
            directoryEntriesOffset,
            checked(directoryEntryCount * DirectoryEntrySize),
            $"{directoryName} directory entries");

        for (var entryIndex = 0; entryIndex < directoryEntryCount; entryIndex++)
        {
            var entryOffset = directoryEntriesOffset + entryIndex * DirectoryEntrySize;
            var localHeaderOffset = ReadUInt32(reader, entryOffset);
            var storedSize = ReadUInt32(reader, entryOffset + 4);
            if (localHeaderOffset == 0 && storedSize == 0)
            {
                continue;
            }

            if (storedSize < LocalFileHeaderSize)
            {
                throw new InvalidDataException($"Directory {directoryName} entry {entryIndex} is smaller than its local header.");
            }

            EnsureRange(stream, localHeaderOffset, storedSize, $"{directoryName} entry {entryIndex}");
            if (ReadAscii(reader, localHeaderOffset, 4) != "DATA")
            {
                throw new InvalidDataException($"Directory {directoryName} entry {entryIndex} does not have the expected DATA signature.");
            }

            var name = ReadAscii(reader, localHeaderOffset + LocalFileNameOffset, LocalFileNameLength);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var payloadSize = checked((int)(storedSize - LocalFileHeaderSize));
            entries.Add(new MechWarriorProjectEntry(
                directoryName,
                entryIndex,
                name,
                localHeaderOffset + LocalFileHeaderSize,
                payloadSize));
        }
    }

    private static ushort ReadUInt16(BinaryReader reader, long offset)
    {
        Span<byte> data = stackalloc byte[sizeof(ushort)];
        ReadExactly(reader, offset, data);
        return BinaryPrimitives.ReadUInt16LittleEndian(data);
    }

    private static uint ReadUInt32(BinaryReader reader, long offset)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        ReadExactly(reader, offset, data);
        return BinaryPrimitives.ReadUInt32LittleEndian(data);
    }

    private static string ReadAscii(BinaryReader reader, long offset, int length)
    {
        var data = new byte[length];
        ReadExactly(reader, offset, data);
        var terminator = Array.IndexOf(data, (byte)0);
        return Encoding.ASCII.GetString(data, 0, terminator < 0 ? data.Length : terminator);
    }

    private static void ReadExactly(BinaryReader reader, long offset, Span<byte> data)
    {
        EnsureRange(reader.BaseStream, offset, data.Length, "archive data");
        reader.BaseStream.Position = offset;
        reader.BaseStream.ReadExactly(data);
    }

    private static void EnsureRange(Stream stream, long offset, long length, string description)
    {
        if (offset < 0 || length < 0 || offset > stream.Length - length)
        {
            throw new InvalidDataException(
                $"The {description} range ({offset:N0} + {length:N0} bytes) exceeds the {stream.Length:N0}-byte project archive.");
        }
    }
}
