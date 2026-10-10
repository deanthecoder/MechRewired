// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, compile, or distribute this software,
// for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace MechRewired.Diagnostics;

/// <summary>Buffered, line-oriented log archive with bounded run retention.</summary>
/// <remarks>Records are flushed to disk only when the caller reaches a logical checkpoint.</remarks>
public sealed class DurableTaggedLogStore : IDisposable
{
    private readonly string m_directory;
    private readonly int m_retainedRuns;
    private StreamWriter m_writer;
    private string m_activePath;

    public string ActivePath => m_activePath;

    public DurableTaggedLogStore(string directory, int retainedRuns = 10)
    {
        if (retainedRuns < 1) throw new ArgumentOutOfRangeException(nameof(retainedRuns));
        m_directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
        m_retainedRuns = retainedRuns;
    }

    public void BeginRun(string runId)
    {
        DisposeWriter();
        Directory.CreateDirectory(m_directory);
        var safeRunId = new string((runId ?? "unknown")
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        if (string.IsNullOrEmpty(safeRunId)) safeRunId = "unknown";
        m_activePath = Path.Combine(m_directory, safeRunId + ".log");
        m_writer = new StreamWriter(new FileStream(m_activePath, FileMode.Create, FileAccess.Write,
            FileShare.Read, 16 * 1024, FileOptions.None));
        PruneOldRuns();
    }

    public void AppendLine(string line)
    {
        if (m_writer == null) throw new InvalidOperationException("BeginRun must be called before writing.");
        m_writer.WriteLine(line);
    }

    /// <summary>Flushes buffered records through to storage at an explicit logical checkpoint.</summary>
    public void Flush()
    {
        if (m_writer == null) return;
        m_writer.Flush();
        ((FileStream)m_writer.BaseStream).Flush(flushToDisk: true);
    }

    public void Dispose()
    {
        try { Flush(); }
        finally { DisposeWriter(); }
    }

    private void PruneOldRuns()
    {
        var runs = Directory.EnumerateFiles(m_directory, "*.log")
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var excess = runs.Count - m_retainedRuns;
        foreach (var file in runs)
        {
            if (excess <= 0) break;
            if (string.Equals(Path.GetFullPath(file.FullName), m_activePath, StringComparison.Ordinal)) continue;
            file.Delete();
            excess--;
        }
    }

    private void DisposeWriter()
    {
        m_writer?.Dispose();
        m_writer = null;
    }
}
