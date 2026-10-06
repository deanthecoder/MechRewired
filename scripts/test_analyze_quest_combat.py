import importlib.util
import io
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


class AnalyzeVariantTests(unittest.TestCase):
    @staticmethod
    def record(trial, variant, smoke, weapon_lights, mean_ms=10):
        return {
            "runId": "run-1", "trial": trial, "variant": variant, "target": "Enemy",
            "status": "complete", "frames": 100, "meanMs": mean_ms,
            "weaponShots": 3, "enemyShots": 3, "impacts": 3, "missileLaunches": 2,
            "maxHeadTranslation": 0, "maxHeadAngle": 0,
            "smoke": smoke, "missileSmoke": smoke, "weaponLights": weapon_lights,
            "playerWorldRaycastMs": 1, "playerMechRaycastMs": 2,
            "weaponEffectPoolBuilds": 0, "weaponEffectPoolFallbacks": 0,
        }

    def test_missile_terrain_timing_reports_total_and_cost_per_query(self):
        record = self.record(1, "baseline", True, True)
        record.update(missileTerrainQueryCalls=20, missileTerrainQueryMs=10)
        output = io.StringIO()

        ANALYZER.analyze([record], output)

        self.assertIn("missile terrain query: 10.00 ms / 20.00 calls; 0.50 ms/call", output.getvalue())

    def test_isolated_variants_compare_against_bracketing_baselines(self):
        records = [
            self.record(1, "baseline", True, True),
            self.record(2, "smoke-off", False, True),
            self.record(3, "weapon-lights-off", True, False),
            self.record(4, "baseline", True, True),
        ]
        output = io.StringIO()

        ANALYZER.analyze(records, output)

        self.assertIn("smoke-off (1 complete trial(s))", output.getvalue())
        self.assertIn("weapon-lights-off (1 complete trial(s))", output.getvalue())
        self.assertIn("player world raycast: 1.00 ms", output.getvalue())
        self.assertIn("weapon effect pools 0.00 builds / 0.00 fallbacks", output.getvalue())

    def test_weapon_light_delta_is_withheld_when_actual_state_differs(self):
        records = [
            self.record(1, "baseline", True, True),
            self.record(2, "smoke-off", False, True),
            self.record(3, "weapon-lights-off", True, True),
            self.record(4, "baseline", True, True),
        ]
        output = io.StringIO()

        ANALYZER.analyze(records, output)

        self.assertIn("weapon-lights-off: trial weapon lights were enabled or unrecorded", output.getvalue())

    def test_weapon_light_delta_is_withheld_when_smoke_state_varies(self):
        records = [
            self.record(1, "baseline", True, True),
            self.record(2, "smoke-off", False, True),
            self.record(3, "weapon-lights-off", False, False),
            self.record(4, "baseline", True, True),
        ]
        output = io.StringIO()

        ANALYZER.analyze(records, output)

        self.assertIn("weapon-lights-off: smoke was disabled or unrecorded", output.getvalue())

    def focused_record(self, trial, variant, mean_ms=10):
        record = self.record(trial, variant, True, True, mean_ms)
        record.update(schema=7, effectPolicy="focused-effects-v1",
                      enemyMissileSmoke=variant != "missile-smoke-reduced",
                      missileSmokeScale=0.75 if variant == "missile-smoke-reduced" else 1.0,
                      projectileLightScale=0.5 if variant == "projectile-lights-small" else 1.0)
        return record

    def test_focused_effect_candidates_compare_with_expected_individual_settings(self):
        records = [
            self.focused_record(1, "baseline"),
            self.focused_record(2, "missile-smoke-reduced"),
            self.focused_record(3, "projectile-lights-small"),
            self.focused_record(4, "baseline"),
        ]
        output = io.StringIO()

        ANALYZER.analyze(records, output)

        self.assertIn("missile-smoke-reduced (1 complete trial(s))", output.getvalue())
        self.assertIn("projectile-lights-small (1 complete trial(s))", output.getvalue())

    def test_focused_effect_delta_is_withheld_for_mismatched_baseline_settings(self):
        first = self.focused_record(1, "baseline")
        second = self.focused_record(4, "baseline")
        second["projectileLightScale"] = 0.5
        records = [first, self.focused_record(2, "missile-smoke-reduced"),
                   self.focused_record(3, "projectile-lights-small"), second]
        output = io.StringIO()

        ANALYZER.analyze(records, output)

        self.assertIn("projectile-lights-small: baseline projectile light scale was not 1.0", output.getvalue())

    def test_focused_effect_delta_is_withheld_when_candidate_changes_unrelated_effect(self):
        smoke_candidate = self.focused_record(2, "missile-smoke-reduced")
        smoke_candidate["projectileLightScale"] = 0.5
        light_candidate = self.focused_record(3, "projectile-lights-small")
        light_candidate["missileSmokeScale"] = 0.75
        records = [self.focused_record(1, "baseline"), smoke_candidate,
                   light_candidate, self.focused_record(4, "baseline")]
        output = io.StringIO()

        ANALYZER.analyze(records, output)

        self.assertIn("missile-smoke-reduced: projectile light scale changed", output.getvalue())
        self.assertIn("projectile-lights-small: missile smoke scale changed", output.getvalue())


if __name__ == "__main__":
    unittest.main()
