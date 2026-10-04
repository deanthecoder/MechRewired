import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPT_PATH = Path(__file__).with_name("validate-quest-apk.py")
SPEC = importlib.util.spec_from_file_location("validate_quest_apk", SCRIPT_PATH)
VALIDATOR = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(VALIDATOR)


class ValidateQuestApkTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.apk = Path(self.temp_dir.name) / "fixture.apk"

    def write_apk(self, files):
        with zipfile.ZipFile(self.apk, "w") as archive:
            for name, data in files.items():
                archive.writestr(name, data)

    def complete_files(self):
        return {
            VALIDATOR.REQUIRED_ASSETS[0]: b"managed assembly",
            VALIDATOR.REQUIRED_ASSETS[1]: json.dumps({"runtimeOptions": {}}).encode(),
            VALIDATOR.REQUIRED_ASSETS[2]: b"core library",
            VALIDATOR.TEST_DATA_ASSET: b"private game data",
        }

    def test_valid_mono_apk_passes_with_and_without_test_data_requirement(self):
        self.write_apk(self.complete_files())
        self.assertEqual([], VALIDATOR.validate_apk(self.apk))
        self.assertEqual([], VALIDATOR.validate_apk(self.apk, require_test_data=True))

    def test_each_required_managed_asset_must_exist(self):
        for missing in VALIDATOR.REQUIRED_ASSETS:
            with self.subTest(missing=missing):
                files = self.complete_files()
                del files[missing]
                self.write_apk(files)
                self.assertTrue(any(missing in e and "missing" in e for e in VALIDATOR.validate_apk(self.apk)))

    def test_each_required_managed_asset_must_be_nonempty(self):
        for empty in VALIDATOR.REQUIRED_ASSETS:
            with self.subTest(empty=empty):
                files = self.complete_files()
                files[empty] = b""
                self.write_apk(files)
                self.assertTrue(any(empty in e and "empty" in e for e in VALIDATOR.validate_apk(self.apk)))

    def test_malformed_runtime_config_is_rejected(self):
        files = self.complete_files()
        files[VALIDATOR.REQUIRED_ASSETS[1]] = b"{bad json"
        self.write_apk(files)
        self.assertTrue(any("invalid Mono runtime config" in e for e in VALIDATOR.validate_apk(self.apk)))

    def test_test_data_flag_requires_nonempty_data(self):
        files = self.complete_files()
        del files[VALIDATOR.TEST_DATA_ASSET]
        self.write_apk(files)
        self.assertTrue(any(VALIDATOR.TEST_DATA_ASSET in e for e in VALIDATOR.validate_apk(self.apk, True)))

        files[VALIDATOR.TEST_DATA_ASSET] = b""
        self.write_apk(files)
        self.assertTrue(any("empty" in e for e in VALIDATOR.validate_apk(self.apk, True)))
        self.assertEqual([], VALIDATOR.validate_apk(self.apk, False))

    def test_bad_zip_is_rejected(self):
        self.apk.write_bytes(b"not a zip")
        self.assertTrue(any("cannot read APK as a ZIP archive" in e for e in VALIDATOR.validate_apk(self.apk)))


if __name__ == "__main__":
    unittest.main()
