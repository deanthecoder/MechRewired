"""Mock-ADB coverage for the Quest benchmark mirror retrieval command."""

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
QUEST_SCRIPT = REPO_ROOT / "scripts" / "quest.sh"
APP_REMOTE = "/storage/emulated/0/Android/data/uk.co.deanthecoder.mechrewired/files/benchmarks"
DOWNLOADS_REMOTE = "/sdcard/Download/MechRewired/benchmarks"


class QuestFetchBenchmarksTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.scripts = self.root / "scripts"
        self.scripts.mkdir()
        self.script = self.scripts / "quest.sh"
        shutil.copy2(QUEST_SCRIPT, self.script)
        self.adb = self.root / "fake-adb"
        self.adb.write_text(
            "#!/usr/bin/env python3\n"
            "import os, shutil, sys\n"
            "args = sys.argv[1:]\n"
            "if args[:1] == ['-s']: args = args[2:]\n"
            "root = os.environ['FAKE_REMOTE_ROOT']\n"
            "if args[0] == 'get-state': sys.exit(0)\n"
            "if args[0] == 'shell' and args[1:3] == ['test', '-d']:\n"
            "    path = os.path.join(root, args[3].lstrip('/'))\n"
            "    sys.exit(0 if os.path.isdir(path) else 1)\n"
            "if args[0] == 'pull':\n"
            "    remote = os.path.join(root, args[1].rstrip('/').lstrip('/'))\n"
            "    local = args[2].rstrip('/')\n"
            "    os.makedirs(local, exist_ok=True)\n"
            "    for name in os.listdir(remote): shutil.copy2(os.path.join(remote, name), os.path.join(local, name))\n"
            "    sys.exit(0)\n"
            "sys.exit(2)\n",
            encoding="utf-8",
        )
        self.adb.chmod(0o755)

    def make_mirror(self, remote_path, name, content):
        path = self.root / "remote" / remote_path.lstrip("/")
        path.mkdir(parents=True, exist_ok=True)
        (path / name).write_text(content, encoding="utf-8")

    def run_fetch(self):
        env = os.environ.copy()
        env.update({
            "ADB_BIN": str(self.adb),
            "ANDROID_SERIAL": "quest:5555",
            "FAKE_REMOTE_ROOT": str(self.root / "remote"),
            "QUEST_ADDRESS_FILE": str(self.root / "address"),
        })
        return subprocess.run(
            ["bash", str(self.script), "fetch-benchmarks"],
            cwd=self.root,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )

    def retrieved_root(self):
        return next((self.root / "local" / "quest-benchmarks").iterdir())

    def test_keeps_preferred_app_archive_separate_from_stale_downloads(self):
        self.make_mirror(APP_REMOTE, "same-run.log", "complete app archive\n")
        self.make_mirror(DOWNLOADS_REMOTE, "same-run.log", "stale partial download\n")

        result = self.run_fetch()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        destination = self.retrieved_root()
        self.assertEqual("complete app archive\n", (destination / "app-storage" / "same-run.log").read_text(encoding="utf-8"))
        self.assertEqual("stale partial download\n", (destination / "downloads" / "same-run.log").read_text(encoding="utf-8"))
        self.assertIn("preferred app-storage", result.stdout)

    def test_falls_back_to_downloads_when_app_storage_has_no_logs(self):
        self.make_mirror(DOWNLOADS_REMOTE, "fallback.log", "download fallback\n")

        result = self.run_fetch()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        destination = self.retrieved_root()
        self.assertEqual("download fallback\n", (destination / "downloads" / "fallback.log").read_text(encoding="utf-8"))
        self.assertIn("Downloads fallback", result.stdout)


if __name__ == "__main__":
    unittest.main()
