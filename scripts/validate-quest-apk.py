#!/usr/bin/env python3
"""Check that a Quest APK contains the minimum Mono runtime payload."""

from __future__ import annotations

import argparse
import json
import sys
import zipfile
from pathlib import Path


REQUIRED_ASSETS = (
    "assets/.godot/mono/publish/arm64/MechRewired.dll",
    "assets/.godot/mono/publish/arm64/MechRewired.runtimeconfig.json",
    "assets/.godot/mono/publish/arm64/System.Private.CoreLib.dll",
)
TEST_DATA_ASSET = "assets/TestData/MW2.PRJ"


def validate_apk(apk_path: Path, require_test_data: bool = False) -> list[str]:
    """Return structural APK errors; an empty list means validation passed."""
    try:
        with zipfile.ZipFile(apk_path) as archive:
            entries = set(archive.namelist())
            required = list(REQUIRED_ASSETS)
            if require_test_data:
                required.append(TEST_DATA_ASSET)

            errors: list[str] = []
            for name in required:
                if name not in entries:
                    errors.append(f"missing required APK asset: {name}")
                    continue
                try:
                    if archive.getinfo(name).file_size == 0:
                        errors.append(f"required APK asset is empty: {name}")
                except KeyError:
                    errors.append(f"missing required APK asset: {name}")

            runtime_config = REQUIRED_ASSETS[1]
            if runtime_config in entries:
                try:
                    payload = archive.read(runtime_config)
                    if payload:
                        json.loads(payload)
                except (KeyError, OSError, json.JSONDecodeError, UnicodeDecodeError) as error:
                    errors.append(f"invalid Mono runtime config {runtime_config}: {error}")
            return errors
    except (OSError, zipfile.BadZipFile) as error:
        return [f"cannot read APK as a ZIP archive: {error}"]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path, help="Quest APK to inspect")
    parser.add_argument(
        "--require-test-data",
        action="store_true",
        help="also require a nonempty bundled assets/TestData/MW2.PRJ",
    )
    args = parser.parse_args(argv)

    errors = validate_apk(args.apk, args.require_test_data)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        print(
            "Expected an APK exported by a Mono-enabled Godot Android build with its managed assemblies.",
            file=sys.stderr,
        )
        return 1

    print(f"Quest APK structure valid: {args.apk}")
    if args.require_test_data:
        print("Private test data present: assets/TestData/MW2.PRJ")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
