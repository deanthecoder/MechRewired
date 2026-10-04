"""Isolated regression tests for the Quest export wrapper."""

import json
import os
import platform
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
QUEST_SCRIPT = REPO_ROOT / "scripts" / "quest.sh"
MSBUILD_OVERRIDES = (
    "MSBUILD_EXE_PATH",
    "MSBuildSDKsPath",
    "MSBuildExtensionsPath",
    "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR",
    "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR",
    "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER",
)
DOTNET_PATH_SETTINGS = (
    "DOTNET_ROOT",
    "DOTNET_ROOT_ARM64",
    "DOTNET_ROOT_X64",
    "DOTNET_HOST_PATH",
)


def macos_system_dotnet_dir():
    """Return the selected system runtime dir when this host can exercise it."""
    if platform.system() != "Darwin":
        return None
    root = Path("/usr/local/share/dotnet")
    if platform.machine() == "x86_64" and os.access(root / "x64" / "dotnet", os.X_OK):
        return root / "x64"
    if os.access(root / "dotnet", os.X_OK):
        return root
    return None


class QuestExportScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.root = Path(self.temp_dir.name)
        self.scripts_dir = self.root / "scripts"
        self.project_dir = self.root / "MechRewired"
        self.build_dir = self.project_dir / "builds"
        self.android_dir = self.project_dir / "android"
        self.android_build = self.android_dir / "build"
        self.signing_dir = self.root / "fake-signing"
        self.trace = self.root / "trace.jsonl"
        for path in (self.scripts_dir, self.project_dir, self.android_build, self.signing_dir,
                     self.root / "local" / "game-data"):
            path.mkdir(parents=True, exist_ok=True)

        shutil.copy2(QUEST_SCRIPT, self.scripts_dir / "quest.sh")
        (self.project_dir / "project.godot").write_text("project-original\n", encoding="utf-8")
        (self.project_dir / "MechRewired.sln").write_text("solution\n", encoding="utf-8")
        (self.project_dir / "export_presets.cfg").write_text('include_filter=""\n', encoding="utf-8")
        self.manifest_path = self.android_build / "src" / "main" / "AndroidManifest.xml"
        self.manifest_path.parent.mkdir(parents=True)
        self.manifest_original = (
            '<manifest xmlns:android="http://schemas.android.com/apk/res/android">\n'
            '  <application>\n    <supports-screens android:anyDensity="true" />\n'
            '  </application>\n</manifest>\n'
        )
        self.manifest_path.write_text(self.manifest_original, encoding="utf-8")
        (self.android_dir / ".build_version").write_text("test-template\n", encoding="utf-8")
        (self.android_build / "gradlew").write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")

        self.game_data = self.root / "local" / "game-data" / "MW2.PRJ"
        self.game_data.write_bytes(b"fixture private game data")
        (self.signing_dir / "mechrewired-release.keystore").write_bytes(b"test-only placeholder")
        (self.signing_dir / "keystore-password").write_text("not-a-real-secret\n", encoding="utf-8")

        self.godot = self.root / "fake-godot"
        self.godot.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, sys\n"
            "with open(os.environ['FAKE_TRACE'], 'a', encoding='utf-8') as trace:\n"
            "    trace.write(json.dumps({'args': sys.argv[1:], 'overrides': {\n"
            + ",\n".join(
                f"        {name!r}: os.environ.get({name!r}, '<unset>')"
                for name in MSBUILD_OVERRIDES
            )
            + "\n    }, 'dotnetPathSettings': {\n"
            + ",\n".join(
                f"        {name!r}: os.environ.get({name!r}, '<unset>')"
                for name in DOTNET_PATH_SETTINGS
            )
            + ",\n        'PATH': os.environ.get('PATH', '')\n"
            "    }}) + '\\n')\n"
            "mode = os.environ.get('FAKE_GODOT_MODE', 'success')\n"
            "if mode == 'managed-failure':\n"
            "    print('Export .NET Project: Failed to build project. Check MSBuild output.')\n"
            "elif mode == 'native-failure':\n"
            "    print('fake Android export failed')\n"
            "else:\n"
            "    print('fake Android export succeeded')\n"
            "sys.exit(7 if mode == 'native-failure' else 0)\n",
            encoding="utf-8",
        )
        self.godot.chmod(0o755)

        validator = self.scripts_dir / "validate-quest-apk.py"
        validator.write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, sys\n"
            "with open(os.environ['FAKE_TRACE'], 'a', encoding='utf-8') as trace:\n"
            "    trace.write(json.dumps({'validator': sys.argv[1:]}) + '\\n')\n",
            encoding="utf-8",
        )

    def run_build(self, mode="success", poison_msbuild=True, poison_dotnet=False):
        env = os.environ.copy()
        env.update({
            "GODOT_BIN": str(self.godot),
            "QUEST_SIGNING_DIR": str(self.signing_dir),
            "QUEST_INCLUDE_TEST_DATA": "1",
            "MW2_PRJ": str(self.game_data),
            "FAKE_TRACE": str(self.trace),
            "FAKE_GODOT_MODE": mode,
            "JAVA_HOME": str(self.root / "fake-java"),
            "ANDROID_HOME": str(self.root / "fake-android-sdk"),
        })
        if poison_msbuild:
            env.update({name: f"inherited-bad-{name}" for name in MSBUILD_OVERRIDES})
        else:
            for name in MSBUILD_OVERRIDES:
                env.pop(name, None)
        if poison_dotnet:
            private_root = str(self.root / "poison-dotnet")
            env["PATH"] = private_root + os.pathsep + env.get("PATH", "")
            env["DOTNET_ROOT"] = private_root
            env["DOTNET_ROOT_ARM64"] = private_root
            env["DOTNET_ROOT_X64"] = private_root
            env["DOTNET_HOST_PATH"] = str(Path(private_root) / "dotnet")
        return subprocess.run(
            ["bash", str(self.scripts_dir / "quest.sh"), "build"],
            cwd=self.root,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )

    def trace_rows(self):
        if not self.trace.exists():
            return []
        return [json.loads(line) for line in self.trace.read_text(encoding="utf-8").splitlines()]

    def assert_configs_restored(self):
        self.assertEqual("project-original\n", (self.project_dir / "project.godot").read_text(encoding="utf-8"))
        self.assertEqual(self.manifest_original, self.manifest_path.read_text(encoding="utf-8"))
        self.assertEqual('include_filter=""\n', (self.project_dir / "export_presets.cfg").read_text(encoding="utf-8"))
        self.assertFalse((self.project_dir / "TestData").exists())

    def assert_persistent_export_log_contains(self, content):
        export_log = self.build_dir / "MechRewired-Quest3.apk.export.log"
        self.assertIn(content, export_log.read_text(encoding="utf-8"))

    def test_build_clears_inherited_msbuild_overrides_and_restores_temporary_files(self):
        result = self.run_build()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        rows = self.trace_rows()
        godot_rows = [row for row in rows if "args" in row]
        self.assertEqual(1, len(godot_rows))
        self.assertEqual({name: "<unset>" for name in MSBUILD_OVERRIDES}, godot_rows[0]["overrides"])
        self.assertEqual(1, len([row for row in rows if "validator" in row]))
        self.assert_persistent_export_log_contains("fake Android export succeeded")
        self.assert_configs_restored()

    def test_managed_publish_failure_with_zero_godot_exit_stops_before_validator(self):
        result = self.run_build(mode="managed-failure")

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Quest Release export failed", result.stderr)
        self.assertEqual(0, len([row for row in self.trace_rows() if "validator" in row]))
        self.assert_persistent_export_log_contains("Export .NET Project: Failed to build project.")
        self.assert_configs_restored()

    def test_nonzero_godot_export_stops_before_validator_and_restores_files(self):
        result = self.run_build(mode="native-failure")

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Quest Release export failed", result.stderr)
        self.assertEqual(0, len([row for row in self.trace_rows() if "validator" in row]))
        self.assert_persistent_export_log_contains("fake Android export failed")
        self.assert_configs_restored()

    def test_success_reaches_validator_and_restores_manifest_and_test_staging(self):
        result = self.run_build(poison_msbuild=False)

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertTrue(any("validator" in row for row in self.trace_rows()))
        self.assert_persistent_export_log_contains("fake Android export succeeded")
        self.assert_configs_restored()

    @unittest.skipUnless(
        macos_system_dotnet_dir() is not None,
        "requires macOS with the Godot-preferred system .NET runtime installed",
    )
    def test_macos_system_dotnet_path_and_roots_override_private_dotnet_environment(self):
        result = self.run_build(poison_dotnet=True)

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("Quest .NET SDK:", result.stdout)
        godot_rows = [row for row in self.trace_rows() if "args" in row]
        self.assertEqual(1, len(godot_rows))
        captured = godot_rows[0]["dotnetPathSettings"]
        system_dir = str(macos_system_dotnet_dir())
        self.assertEqual(system_dir, captured["DOTNET_ROOT"])
        self.assertEqual(system_dir, captured["DOTNET_ROOT_ARM64"])
        self.assertEqual(system_dir, captured["DOTNET_ROOT_X64"])
        self.assertEqual(str(Path(system_dir) / "dotnet"), captured["DOTNET_HOST_PATH"])
        self.assertEqual(system_dir, captured["PATH"].split(os.pathsep, 1)[0])
        self.assertNotIn(str(self.root / "poison-dotnet"), captured["PATH"].split(os.pathsep, 1)[0])
        self.assert_configs_restored()

if __name__ == "__main__":
    unittest.main()
