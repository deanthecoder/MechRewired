import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SPEC = importlib.util.spec_from_file_location(
    "analyze_quest_combat", Path(__file__).with_name("analyze-quest-combat.py"))
ANALYZER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ANALYZER)


def chunk_lines(record, kind="SUMMARY", record_id=9, size=17):
    payload = json.dumps(record, separators=(",", ":"))
    pieces = [payload[index:index + size] for index in range(0, len(payload), size)]
    return ["QUEST_COMBAT_CHUNK: " + json.dumps({
        "kind": kind, "runId": record["runId"], "recordId": record_id,
        "index": index, "count": len(pieces), "data": piece,
    }) for index, piece in enumerate(pieces)]


class ReadRecordsTests(unittest.TestCase):
    def read(self, lines):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.log"
            path.write_text("\n".join(lines), encoding="utf-8")
            return ANALYZER.read_records(path)

    def test_reassembles_reordered_summary_chunks(self):
        expected = {"runId": "run-1", "trial": 2, "status": "complete", "note": 'quote " slash \\ and unicode ⚙️'}
        lines = chunk_lines(expected)

        records, malformed = self.read(list(reversed(lines)))

        self.assertEqual(records, [expected])
        self.assertEqual(malformed, 0)

    def test_missing_chunk_does_not_create_partial_record(self):
        lines = chunk_lines({"runId": "run-1", "trial": 1, "status": "complete", "padding": "x" * 100})

        records, malformed = self.read(lines[:-1])

        self.assertEqual(records, [])
        self.assertEqual(malformed, 1)

    def test_legacy_plain_summary_remains_supported(self):
        expected = {"runId": "old-run", "trial": 1, "status": "complete"}

        records, malformed = self.read(["QUEST_COMBAT_SUMMARY: " + json.dumps(expected)])

        self.assertEqual(records, [expected])
        self.assertEqual(malformed, 0)


if __name__ == "__main__":
    unittest.main()
