#!/usr/bin/env python3
"""Summarize QUEST_COMBAT_SUMMARY JSON records from Android logs."""

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path

PREFIX = "QUEST_COMBAT_SUMMARY:"
CHUNK_PREFIX = "QUEST_COMBAT_CHUNK:"
VARIANTS = ("baseline", "smoke-off", "weapon-lights-off")
METRICS = (
    ("hudInstrumentDraws", "HUD instrument redraws", ""),
    ("meanMs", "mean frame", " ms"),
    ("p95Ms", "p95 frame", " ms"),
    ("p99Ms", "p99 frame", " ms"),
    ("averageFps", "average FPS", ""),
    ("onePercentLowFps", "1% low FPS", ""),
    ("gpuMs", "GPU", " ms"),
    ("cpuMs", "renderer CPU", " ms"),
    ("physicsMs", "physics", " ms"),
    ("processMs", "process", " ms"),
    ("allocatedBytes", "allocated", " B"),
    ("gc0", "GC0", ""),
    ("gc1", "GC1", ""),
    ("gc2", "GC2", ""),
    ("enemyAiMs", "enemy AI total", " ms"),
    ("losMs", "LOS total", " ms"),
    ("losCalls", "LOS calls", ""),
    ("poolBuilds", "pool builds", ""),
    ("poolBuildMs", "pool build total", " ms"),
    ("playerWorldRaycastMs", "player world raycast", " ms"),
    ("playerMechRaycastMs", "player mech raycast", " ms"),
    ("weaponEffectPoolBuilds", "weapon effect pool builds", ""),
    ("weaponEffectPoolFallbacks", "weapon effect pool fallbacks", ""),
    ("weaponShots", "weapon shots", ""),
    ("enemyShots", "enemy weapon shots", ""),
    ("missileLaunches", "missile launches", ""),
    ("impacts", "impacts", ""),
    ("maxHeadTranslation", "head translation", " m"),
    ("maxHeadAngle", "head angle", " deg"),
    ("firstVolleyMeanMs", "first-volley mean frame", " ms"),
    ("firstVolleyMaxMs", "first-volley worst frame", " ms"),
    ("repeatedVolleyMeanMs", "later-volley mean frame", " ms"),
    ("firstImpactFrameMs", "first-impact frame", " ms"),
)


def read_records(path):
    records = []
    malformed = 0
    chunks = {}
    with path.open("r", encoding="utf-8-sig", errors="replace") as source:
        for line in source:
            if CHUNK_PREFIX in line:
                try:
                    chunk = json.loads(line.split(CHUNK_PREFIX, 1)[1].strip())
                    kind, run_id, record_id = chunk["kind"], chunk["runId"], chunk["recordId"]
                    index, count, data = chunk["index"], chunk["count"], chunk["data"]
                    if (not isinstance(kind, str) or not isinstance(run_id, str)
                            or isinstance(record_id, bool) or not isinstance(record_id, int)
                            or isinstance(index, bool) or not isinstance(index, int)
                            or isinstance(count, bool) or not isinstance(count, int)
                            or count < 1 or index < 0 or index >= count or not isinstance(data, str)):
                        raise ValueError("invalid chunk fields")
                    key = (kind, run_id, record_id)
                    entry = chunks.setdefault(key, {"count": count, "parts": {}, "invalid": False})
                    if entry["count"] != count or index in entry["parts"]:
                        entry["invalid"] = True
                    entry["parts"][index] = data
                except (json.JSONDecodeError, KeyError, TypeError, ValueError):
                    malformed += 1
                continue
            if PREFIX in line:
                payload = line.split(PREFIX, 1)[1].strip()
                try:
                    record = json.loads(payload)
                except (json.JSONDecodeError, TypeError):
                    malformed += 1
                    continue
                if isinstance(record, dict):
                    records.append(record)
                else:
                    malformed += 1

    for (kind, _run_id, _record_id), entry in chunks.items():
        if kind != "SUMMARY":
            continue
        if entry["invalid"] or len(entry["parts"]) != entry["count"]:
            malformed += 1
            continue
        payload = "".join(entry["parts"][index] for index in range(entry["count"]))
        try:
            record = json.loads(payload)
        except (json.JSONDecodeError, TypeError):
            malformed += 1
            continue
        if isinstance(record, dict):
            records.append(record)
        else:
            malformed += 1
    return records, malformed


def number(record, field):
    value = record.get(field)
    if isinstance(value, bool) or value is None:
        return None
    try:
        value = float(value)
    except (TypeError, ValueError):
        return None
    return value if math.isfinite(value) else None


def average(values):
    values = [value for value in values if value is not None]
    return sum(values) / len(values) if values else None


def fmt(value, suffix=""):
    if value is None:
        return "n/a"
    if suffix == " B":
        return f"{value:.0f}{suffix}"
    return f"{value:.2f}{suffix}"


def summarize(records, field):
    return average([number(record, field) for record in records])


def delta(base, candidate):
    if base is None or candidate is None or base == 0:
        return None
    return (candidate - base) * 100.0 / base


def pct(value):
    return "n/a" if value is None else f"{value:+.1f}%"


def identify(record):
    return f"{record.get('runId', '?')}/trial {record.get('trial', '?')}/{record.get('variant', '?')}"


def eligible(record):
    """Withhold percentages when the sample cannot support a combat comparison."""
    return (str(record.get("status", "")).lower() == "complete"
            and (number(record, "frames") or 0) > 0
            and (number(record, "meanMs") or 0) > 0
            and (number(record, "enemyShots") or 0) > 0
            and (number(record, "impacts") or 0) > 0
            and number(record, "maxHeadTranslation") is not None
            and number(record, "maxHeadTranslation") <= 0.05
            and number(record, "maxHeadAngle") is not None
            and number(record, "maxHeadAngle") <= 5)


def analyze(records, output):
    if not records:
        print("No QUEST_COMBAT_SUMMARY records found.", file=output)
        return
    # Repeated/pasted log chunks must not manufacture a second baseline.
    records = list({(str(r.get("runId", "?")), r.get("trial")): r for r in records}.values())
    by_run = defaultdict(list)
    for record in records:
        by_run[str(record.get("runId", "?"))].append(record)

    for run_id, run_records in by_run.items():
        print(f"Run {run_id}", file=output)
        for rec in sorted(run_records, key=lambda r: (str(r.get("variant", "")), number(r, "trial") or 0)):
            status = str(rec.get("status", "unknown")).lower()
            tag = identify(rec)
            print(f"  Trial {rec.get('trial', '?')} {rec.get('variant', '?')} [{status}]: "
                  f"mean {fmt(number(rec, 'meanMs'), ' ms')}, p95 {fmt(number(rec, 'p95Ms'), ' ms')}, "
                  f"GPU {fmt(number(rec, 'gpuMs'), ' ms')}, enemy shots {fmt(number(rec, 'enemyShots'))}, "
                  f"missile pool builds {fmt(number(rec, 'poolBuilds'))} / {fmt(number(rec, 'poolBuildMs'), ' ms')}; "
                  f"weapon effect pools {fmt(number(rec, 'weaponEffectPoolBuilds'))} builds / "
                  f"{fmt(number(rec, 'weaponEffectPoolFallbacks'))} fallbacks", file=output)
            for calls_key, time_key, label in (
                ("playerDirectRaycastCalls", "playerDirectRaycastMs", "player raycast"),
                (None, "playerWorldRaycastMs", "player world raycast"),
                (None, "playerMechRaycastMs", "player mech raycast"),
                ("playerDirectDamageCalls", "playerDirectDamageMs", "player damage/impact"),
                ("playerWeaponVisualCalls", "playerWeaponVisualMs", "player beam/tracer creation"),
            ):
                if time_key in rec:
                    calls, total = number(rec, calls_key) if calls_key else None, number(rec, time_key)
                    if calls_key is None:
                        print(f"    {label}: {fmt(total, ' ms')}", file=output)
                        continue
                    mean = total / calls if total is not None and calls else None
                    print(f"    {label}: {fmt(total, ' ms')} / {fmt(calls)} calls; "
                          f"{fmt(mean, ' ms/call')}", file=output)
            if status != "complete":
                print(f"  WARNING {tag}: status {status} (incomplete/death run excluded from deltas)", file=output)
            if (number(rec, "weaponShots") or 0) <= 0:
                print(f"  WARNING {tag}: no weapon shots recorded", file=output)
            if (number(rec, "enemyShots") or 0) <= 0:
                print(f"  WARNING {tag}: no enemy weapon shots recorded; enemy did not engage, so combat workload is unconfirmed", file=output)
            if (number(rec, "impacts") or 0) <= 0:
                print(f"  WARNING {tag}: no impacts recorded", file=output)
            translation = number(rec, "maxHeadTranslation")
            angle = number(rec, "maxHeadAngle")
            if translation is not None and translation > 0.05:
                print(f"  WARNING {tag}: head translation {translation:.3f} m exceeds 0.05 m", file=output)
            if angle is not None and angle > 5:
                print(f"  WARNING {tag}: head movement {angle:.1f} deg exceeds 5 deg", file=output)

        targets = sorted({str(r.get("target", "")) for r in run_records})
        for target in targets:
            target_records = [r for r in run_records if str(r.get("target", "")) == target]
            base_rows = [r for r in target_records if r.get("variant") == "baseline" and
                         eligible(r)]
            if len(base_rows) < 2:
                print(f"  {target}: variant deltas skipped (need 2 valid combat baselines for matching target; found {len(base_rows)})", file=output)
                continue
            ordered = sorted(base_rows, key=lambda r: number(r, "trial") or 0)
            drift = abs(delta(number(ordered[0], "meanMs"), number(ordered[-1], "meanMs")))
            if drift > 10:
                print(f"  WARNING {target}: baseline drift {drift:.1f}%; variant deltas withheld", file=output)
                continue
            print(f"  {target}: average of {len(base_rows)} complete baselines", file=output)
            for key, label, suffix in METRICS:
                print(f"    baseline {label}: {fmt(summarize(base_rows, key), suffix)}", file=output)
            for variant in (v for v in VARIANTS[1:] if any(r.get("variant") == v for r in target_records)):
                candidates = [r for r in target_records if r.get("variant") == variant and
                              eligible(r)]
                if not candidates:
                    print(f"    {variant}: no valid combat trial", file=output)
                    continue
                if variant == "smoke-off":
                    if not all(r.get("smoke") is True for r in base_rows):
                        print("    smoke-off: baseline smoke was disabled or unrecorded; deltas withheld", file=output)
                        continue
                    if not all(r.get("missileSmoke") is True for r in base_rows):
                        print("    smoke-off: baseline missile smoke was disabled or unrecorded; deltas withheld", file=output)
                        continue
                    if not all(r.get("smoke") is False for r in candidates):
                        print("    smoke-off: trial smoke was enabled or unrecorded; deltas withheld", file=output)
                        continue
                    if not all(r.get("missileSmoke") is False for r in candidates):
                        print("    smoke-off: trial missile smoke was enabled or unrecorded; deltas withheld", file=output)
                        continue
                    if not all(r.get("weaponLights") is True for r in base_rows + candidates):
                        print("    smoke-off: weapon lights were disabled or unrecorded; deltas withheld", file=output)
                        continue
                if variant == "weapon-lights-off":
                    if not all(r.get("smoke") is True and r.get("missileSmoke") is True
                               for r in base_rows + candidates):
                        print("    weapon-lights-off: smoke was disabled or unrecorded; deltas withheld", file=output)
                        continue
                    if not all(r.get("weaponLights") is True for r in base_rows):
                        print("    weapon-lights-off: baseline weapon lights were disabled or unrecorded; deltas withheld", file=output)
                        continue
                    if not all(r.get("weaponLights") is False for r in candidates):
                        print("    weapon-lights-off: trial weapon lights were enabled or unrecorded; deltas withheld", file=output)
                        continue
                profile_fields = ("graphicsProfile", "bakedSky", "cockpitUv", "bakedInteriorLighting")
                if any(len({json.dumps(r.get(field)) for r in base_rows + candidates}) > 1 for field in profile_fields):
                    print(f"    {variant}: graphics profiles differ; variant deltas withheld", file=output)
                    continue
                different = [field for field in ("enemyShots", "missileLaunches", "impacts")
                             if summarize(base_rows, field) is not None and summarize(candidates, field) is not None
                             and abs(summarize(candidates, field) - summarize(base_rows, field))
                             > max(2, summarize(base_rows, field) * 0.25)]
                if different:
                    print(f"    {variant}: workload differs ({', '.join(different)}); variant deltas withheld", file=output)
                    continue
                print(f"    {variant} ({len(candidates)} complete trial(s)):", file=output)
                for key, label, suffix in METRICS:
                    value = summarize(candidates, key)
                    change = delta(summarize(base_rows, key), value)
                    print(f"      {label}: {fmt(value, suffix)} ({pct(change)} vs baseline)", file=output)
        print("  Current Quest pools are prepared during mission setup; pool creation in measured combat indicates a fallback. Shader/driver caches can remain warm across reloads.", file=output)
        print("  Actual fighting can vary between trials; these summaries show sensitivity and do not establish a unique bottleneck.", file=output)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Summarize Quest combat telemetry from Android logs.")
    parser.add_argument("input", type=Path, help="Android logcat text file")
    args = parser.parse_args(argv)
    try:
        records, malformed = read_records(args.input)
    except OSError as error:
        parser.error(str(error))
    if malformed:
        print(f"WARNING: skipped {malformed} malformed combat summary line(s)", file=sys.stderr)
    analyze(records, sys.stdout)
    return 0 if records else 1


if __name__ == "__main__":
    raise SystemExit(main())
