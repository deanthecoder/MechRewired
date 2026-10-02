#!/usr/bin/env python3
"""Summarize Quest benchmark summary.csv files or tagged Android logcat output.

Logcat mode extracts CSV records after ``QUEST_BENCHMARK_CSV:``. If another
CSV header appears later, it begins a new run dataset; each dataset is reported
separately so concatenated logs do not mix baselines between runs.
"""

import argparse
import csv
import math
import sys
from pathlib import Path

PREFIX = "QUEST_BENCHMARK_CSV:"
APP_FRAME_FIELD = "mean_ms"
GPU_FIELD = "renderer_gpu_ms"


def read_datasets(path):
    datasets = []
    header = None
    rows = []

    def finish():
        if header is not None:
            datasets.append((header, rows.copy()))

    with path.open("r", encoding="utf-8-sig", errors="replace", newline="") as source:
        for line in source:
            if PREFIX in line:
                line = line[line.index(PREFIX) + len(PREFIX):].strip()
            elif path.suffix.lower() != ".csv":
                continue
            else:
                line = line.strip()
            if not line:
                continue
            try:
                fields = next(csv.reader([line]))
            except csv.Error:
                continue
            if not fields:
                continue
            normalized = [field.strip().lower() for field in fields]
            if "fixture" in normalized and "variant" in normalized:
                finish()
                header = normalized
                rows = []
                continue
            if header is None:
                continue
            if len(fields) < len(header):
                fields += [""] * (len(header) - len(fields))
            rows.append(dict(zip(header, (field.strip() for field in fields))))
    finish()
    return datasets


def number(row, field):
    try:
        value = float(row.get(field, ""))
    except (TypeError, ValueError):
        return None
    return value if math.isfinite(value) else None


def positive_values(rows, field):
    return [value for row in rows if (value := number(row, field)) is not None and value > 0]


def average(values):
    return sum(values) / len(values) if values else None


def ordered(rows):
    return sorted(rows, key=lambda row: (
        number(row, "trial") is None,
        number(row, "trial") if number(row, "trial") is not None else 0.0,
    ))


def percent_reduction(baseline, candidate):
    if baseline is None or candidate is None or baseline <= 0:
        return None
    return (baseline - candidate) * 100.0 / baseline


def percent_increase(reference, candidate):
    if reference is None or candidate is None or reference <= 0:
        return None
    return (candidate - reference) * 100.0 / reference


def fmt(value, suffix=""):
    return "n/a" if value is None else f"{value:.2f}{suffix}"


def fmt_signed(value, suffix=""):
    return "n/a" if value is None else f"{value:+.2f}{suffix}"


def analyze(header, rows, run_index, output):
    required = {"fixture", "variant", APP_FRAME_FIELD}
    missing = sorted(required - set(header))
    if missing:
        print(f"Run {run_index}: skipped (missing columns: {', '.join(missing)})", file=output)
        return
    if not rows:
        print(f"Run {run_index}: no benchmark rows", file=output)
        return

    print(f"Run {run_index}", file=output)
    fixtures = sorted({row["fixture"] for row in rows if row.get("fixture")})
    for fixture in fixtures:
        fixture_rows = [row for row in rows if row.get("fixture") == fixture]
        baked_rows = [row for row in fixture_rows if row.get("variant", "").lower() == "baked-profile"]
        glass_rows = [row for row in fixture_rows if row.get("variant", "").lower() == "baked-profile-glass"]
        if baked_rows and glass_rows:
            baked_app = average(positive_values(baked_rows, APP_FRAME_FIELD))
            glass_app = average(positive_values(glass_rows, APP_FRAME_FIELD))
            baked_gpu = average(positive_values(baked_rows, GPU_FIELD))
            glass_gpu = average(positive_values(glass_rows, GPU_FIELD))
            app_cost = None if baked_app is None or glass_app is None else glass_app - baked_app
            gpu_cost = None if baked_gpu is None or glass_gpu is None else glass_gpu - baked_gpu
            print(f"  {fixture} glass incremental vs baked-profile: "
                  f"app frame {fmt_signed(app_cost, ' ms')} ({fmt_signed(percent_increase(baked_app, glass_app), '%')}); "
                  f"renderer GPU {fmt_signed(gpu_cost, ' ms')} ({fmt_signed(percent_increase(baked_gpu, glass_gpu), '%')})", file=output)

        baseline_rows = ordered([row for row in fixture_rows if row.get("variant", "").lower() == "baseline"])
        if not baseline_rows:
            print(f"  {fixture}: no baseline; baseline comparisons skipped", file=output)
            continue

        first, last = baseline_rows[0], baseline_rows[-1]
        app_baseline = average(positive_values([first, last], APP_FRAME_FIELD))
        gpu_baseline = average(positive_values([first, last], GPU_FIELD))
        first_app, last_app = number(first, APP_FRAME_FIELD), number(last, APP_FRAME_FIELD)
        if first_app is not None and first_app > 0 and last_app is not None:
            drift = abs(last_app - first_app) * 100.0 / first_app
            if drift > 10.0:
                print(f"  WARNING {fixture}: baseline drift {drift:.1f}% (first {first_app:.2f} ms, last {last_app:.2f} ms)", file=output)

        for row in fixture_rows:
            translation = number(row, "max_head_translation_m")
            angle = number(row, "max_head_angle_deg")
            if translation is not None and translation > 0.05:
                print(f"  WARNING {fixture}/{row.get('variant', '?')}: head translation {translation:.3f} m exceeds 0.05 m", file=output)
            if angle is not None and angle > 5.0:
                print(f"  WARNING {fixture}/{row.get('variant', '?')}: head rotation {angle:.1f} deg exceeds 5 deg", file=output)

        print(f"  {fixture} (baseline mean app frame {fmt(app_baseline, ' ms')}; renderer GPU {fmt(gpu_baseline, ' ms')}):", file=output)
        variants = sorted({row.get("variant", "") for row in fixture_rows
                           if row.get("variant") and row.get("variant", "").lower() != "baseline"})
        if not variants:
            print("    no non-baseline variants", file=output)
        for variant in variants:
            variant_rows = [row for row in fixture_rows if row.get("variant") == variant]
            app = average(positive_values(variant_rows, APP_FRAME_FIELD))
            gpu = average(positive_values(variant_rows, GPU_FIELD))
            app_delta = percent_reduction(app_baseline, app)
            gpu_delta = percent_reduction(gpu_baseline, gpu)
            print(f"    {variant}: app frame {fmt(app_delta, '% reduction')} ({fmt(app, ' ms')}); "
                  f"renderer GPU {fmt(gpu_delta, '% reduction')} ({fmt(gpu, ' ms')})", file=output)
    print("  Deltas show sensitivity to each setting in this fixture; they do not identify a unique bottleneck. "
          "Capped frame pacing can hide headroom in FPS.", file=output)


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Report Quest benchmark app-frame and renderer-GPU reductions by fixture and variant.",
        epilog=("Input may be summary.csv or Android logcat containing QUEST_BENCHMARK_CSV: header/row lines. "
                "Each repeated CSV header starts a separate dataset; datasets are reported independently."),
    )
    parser.add_argument("input", type=Path, help="summary.csv or Android logcat text file")
    args = parser.parse_args(argv)
    try:
        datasets = read_datasets(args.input)
    except OSError as error:
        parser.error(str(error))
    if not datasets:
        print("No benchmark CSV header or rows found.", file=sys.stderr)
        return 1
    for index, (header, rows) in enumerate(datasets, start=1):
        analyze(header, rows, index, sys.stdout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
