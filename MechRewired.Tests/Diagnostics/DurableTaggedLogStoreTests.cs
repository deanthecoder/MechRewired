using MechRewired.Diagnostics;
using NUnit.Framework;

namespace MechRewired.Tests.Diagnostics;

[TestFixture]
public sealed class DurableTaggedLogStoreTests
{
    private string m_directory;

    [SetUp]
    public void SetUp() => m_directory = Path.Combine(Path.GetTempPath(), "mechrewired-archive-tests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_directory)) Directory.Delete(m_directory, recursive: true);
    }

    [Test]
    public void FlushAndReopenPreservesEveryTaggedRecord()
    {
        string archivePath;
        using (var archive = new DurableTaggedLogStore(m_directory))
        {
            archive.BeginRun("combat-001");
            archive.AppendLine("QUEST_COMBAT_RUN: {\"runId\":\"combat-001\"}");
            for (var trial = 1; trial <= 5; trial++)
            {
                archive.AppendLine($"QUEST_COMBAT_SUMMARY: {{\"runId\":\"combat-001\",\"trial\":{trial}}}");
                archive.AppendLine($"QUEST_COMBAT_SPIKE: {{\"runId\":\"combat-001\",\"trial\":{trial}}}");
            }
            archive.AppendLine("QUEST_COMBAT_STATUS: {\"runId\":\"combat-001\",\"status\":\"complete\"}");
            archive.Flush();
            archivePath = archive.ActivePath;
        }

        var records = File.ReadAllLines(archivePath);
        Assert.That(records, Has.Length.EqualTo(12));
        Assert.That(records[0], Does.StartWith("QUEST_COMBAT_RUN:"));
        Assert.That(records[^1], Does.Contain("\"status\":\"complete\""));
        Assert.That(records.Count(record => record.StartsWith("QUEST_COMBAT_SUMMARY:", StringComparison.Ordinal)), Is.EqualTo(5));
    }

    [Test]
    public void RetentionKeepsNewestTenRunsAndPrunesOldest()
    {
        for (var run = 1; run <= 12; run++)
        {
            using var archive = new DurableTaggedLogStore(m_directory, retainedRuns: 10);
            var id = $"run-{run:D2}";
            archive.BeginRun(id);
            archive.AppendLine("record " + id);
            archive.Flush();
        }

        var files = Directory.GetFiles(m_directory, "*.log").Select(Path.GetFileName).Order().ToArray();
        Assert.That(files, Has.Length.EqualTo(10));
        Assert.That(files, Does.Not.Contain("run-01.log"));
        Assert.That(files, Does.Not.Contain("run-02.log"));
        Assert.That(files, Does.Contain("run-03.log"));
        Assert.That(files, Does.Contain("run-12.log"));
        Assert.That(File.ReadAllText(Path.Combine(m_directory, "run-12.log")), Is.EqualTo("record run-12" + Environment.NewLine));
    }
}
