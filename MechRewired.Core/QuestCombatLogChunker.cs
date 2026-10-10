// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MechRewired;

/// <summary>Formats large combat JSON records into bounded, independently identifiable log lines.</summary>
/// <remarks>
/// Long records are split only while benchmark results are being reported. The size calculation reserves
/// the JSON escaping cost of each Rune, then serializes each completed chunk once.
/// </remarks>
public static class QuestCombatLogChunker
{
    public const int MaxLineLength = 900;
    private const string ChunkPrefix = "QUEST_COMBAT_CHUNK: ";

    public static IReadOnlyList<string> Format(string kind, string json, long recordId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(json);
        var prefix = $"QUEST_COMBAT_{kind}: ";
        if (prefix.Length + json.Length <= MaxLineLength &&
            Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(json) <= MaxLineLength)
            return [$"{prefix}{json}"];

        using var document = JsonDocument.Parse(json);
        var runId = document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("runId", out var runIdElement)
            ? runIdElement.GetString() ?? string.Empty
            : string.Empty;

        // Reserve space for maximum-width chunk indexes and counts. JsonSerializer uses the default
        // JavaScript encoder: escaped Runes cost at most six ASCII characters per UTF-16 code unit.
        var metadataLength = ChunkLine(kind, runId, recordId, int.MaxValue, int.MaxValue, string.Empty).Length;
        var availableEscapedChars = MaxLineLength - metadataLength;
        if (availableEscapedChars < 12)
            throw new InvalidOperationException("Combat log chunk metadata exceeds the line limit.");

        var chunks = new List<string>();
        var current = new StringBuilder();
        var escapedChars = 0;
        foreach (var rune in json.EnumerateRunes())
        {
            var runeLogChars = JavaScriptEncoder.Default.WillEncode(rune.Value)
                ? rune.Utf16SequenceLength * 6
                : Math.Max(rune.Utf16SequenceLength, rune.Utf8SequenceLength);
            if (escapedChars + runeLogChars > availableEscapedChars)
            {
                chunks.Add(current.ToString());
                current.Clear();
                escapedChars = 0;
            }
            current.Append(rune.ToString());
            escapedChars += runeLogChars;
        }
        if (current.Length > 0) chunks.Add(current.ToString());

        var lines = chunks.Select((data, index) => ChunkLine(kind, runId, recordId, index, chunks.Count, data)).ToArray();
        if (lines.Any(line => line.Length > MaxLineLength || Encoding.UTF8.GetByteCount(line) > MaxLineLength))
            throw new InvalidOperationException("Combat log chunk exceeded the configured line limit.");
        return lines;
    }

    private static string ChunkLine(string kind, string runId, long recordId, int index, int count, string data) =>
        ChunkPrefix + JsonSerializer.Serialize(new { kind, runId, recordId, index, count, data });
}
