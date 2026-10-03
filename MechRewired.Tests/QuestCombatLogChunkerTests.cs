using System.Text.Json;
using System.Text;
using NUnit.Framework;

namespace MechRewired.Tests;

[TestFixture]
public sealed class QuestCombatLogChunkerTests
{
    [Test]
    public void FormatsLongRecordsAsBoundedChunksThatJoinExactly()
    {
        var record = JsonSerializer.Serialize(new { runId = "run-1", note = new string('x', 5000), unicode = "mech ⚙️" });

        var lines = QuestCombatLogChunker.Format("SUMMARY", record, 42);
        var chunks = lines.Select(line => JsonDocument.Parse(line["QUEST_COMBAT_CHUNK: ".Length..]).RootElement)
            .OrderBy(chunk => chunk.GetProperty("index").GetInt32()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(lines.All(line => line.Length <= QuestCombatLogChunker.MaxLineLength &&
                                          Encoding.UTF8.GetByteCount(line) <= QuestCombatLogChunker.MaxLineLength), Is.True);
            Assert.That(string.Concat(chunks.Select(chunk => chunk.GetProperty("data").GetString())), Is.EqualTo(record));
            Assert.That(chunks.All(chunk => chunk.GetProperty("runId").GetString() == "run-1"), Is.True);
            Assert.That(chunks.All(chunk => chunk.GetProperty("recordId").GetInt64() == 42), Is.True);
        });
    }

    [Test]
    public void PreservesAstralCharactersAtChunkBoundaries()
    {
        var json = "{\"runId\":\"run-1\",\"value\":\"" + string.Concat(Enumerable.Repeat("🌋", 500)) + "\"}";

        var lines = QuestCombatLogChunker.Format("SUMMARY", json, 43);
        var chunks = lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line["QUEST_COMBAT_CHUNK: ".Length..]);
            return document.RootElement.GetProperty("data").GetString();
        }).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(chunks), Is.EqualTo(json));
            Assert.That(chunks.All(chunk => chunk is { Length: > 0 } &&
                                            !char.IsHighSurrogate(chunk[^1]) &&
                                            !char.IsLowSurrogate(chunk[0])), Is.True);
        });
    }

    [Test]
    public void LeavesShortRecordsInLegacyFormat()
    {
        var lines = QuestCombatLogChunker.Format("SUMMARY", "{\"runId\":\"run-1\"}", 1);

        Assert.That(lines, Is.EqualTo(new[] { "QUEST_COMBAT_SUMMARY: {\"runId\":\"run-1\"}" }));
    }
}
