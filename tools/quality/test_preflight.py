import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import preflight


class PreflightTests(unittest.TestCase):
    def test_reports_exact_missing_models_without_loading_or_downloading(self):
        with tempfile.TemporaryDirectory() as tmp:
            report = preflight.check(pathlib.Path(tmp), "transcribe", "base", "q5_1")
            self.assertFalse(report["modelsReady"])
            self.assertEqual([m["file"] for m in report["models"]], ["ggml-base-q5_1.bin"])
            self.assertFalse(any(pathlib.Path(tmp).iterdir()))

    def test_analyze_requires_all_five_assets_and_detects_empty_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / "ggml-base-q5_1.bin").touch()
            report = preflight.check(root, "analyze", "base", "q5_1")
            self.assertEqual(len(report["models"]), 5)
            self.assertEqual(report["models"][0]["status"], "empty")
            self.assertFalse(report["modelsReady"])


if __name__ == "__main__":
    unittest.main()
