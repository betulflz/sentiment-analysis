import json
import math
import os
import pathlib
import signal
import subprocess
import sys
import tempfile
import time
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import benchmark as b


def seg(speaker, start, end):
    return {"speaker": speaker, "start": start, "end": end}


class MetricsTests(unittest.TestCase):
    def test_turkish_case_and_punctuation(self):
        self.assertEqual(b.words("İYİ, IŞIK!"), ["iyi", "ışık"])

    def test_word_errors_count_replacements_insertions_deletions(self):
        self.assertEqual(b.edit_distance(["bir", "iki"], ["bir", "üç", "dört"]), 2)
        self.assertEqual(b.edit_distance(["bir", "iki"], []), 2)

    def test_permuted_speaker_names_are_not_errors(self):
        result = b.diarization([seg("A", 0, 4), seg("B", 4, 10)],
                               [seg("2", 0, 4), seg("1", 4, 10)])
        self.assertEqual(result["errorSeconds"], 0)
        self.assertEqual(result["referenceSeconds"], 10)

    def test_overlap_is_scored_and_duplicate_regions_are_unioned(self):
        result = b.diarization([seg("A", 0, 10), seg("B", 5, 10), seg("A", 0, 10)],
                               [seg("x", 0, 10)])
        self.assertEqual(result["referenceSeconds"], 15)
        self.assertEqual(result["missSeconds"], 5)
        self.assertEqual(result["confusionSeconds"], 0)

    def test_false_alarm_and_confusion(self):
        result = b.diarization([seg("A", 0, 5), seg("B", 5, 10)], [seg("x", 0, 12)])
        self.assertEqual(result["falseAlarmSeconds"], 2)
        self.assertEqual(result["confusionSeconds"], 5)
        self.assertEqual(result["der"], 0.7)

    def test_empty_reference_does_not_fabricate_zero_der(self):
        result = b.diarization([], [seg("x", 0, 2)])
        self.assertIsNone(result["der"])
        self.assertEqual(result["falseAlarmSeconds"], 2)

    def test_nonfinite_and_negative_or_reversed_times_are_rejected(self):
        for start, end in [(0, math.nan), (-1, 2), (2, 1), (1, 1)]:
            with self.subTest(start=start, end=end), self.assertRaises(ValueError):
                b.validate_prediction({"speakers": [seg("x", start, end)]}, "diarize", 10)

    def test_missing_task_field_and_out_of_bounds_are_rejected(self):
        for prediction in [{}, {"speakers": [seg("x", 0, 11)]}]:
            with self.assertRaises(ValueError):
                b.validate_prediction(prediction, "diarize", 10)

    def test_missing_or_failed_records_cannot_improve_scores(self):
        manifest = {"recordings": [{"id": "one", "durationSeconds": 10,
                                     "reference": {"transcript": "merhaba"}}]}
        for records in [[], [{"id": "one", "status": "failed"}]]:
            with self.assertRaises(ValueError):
                b.score(manifest, {"task": "transcribe", "records": records})

    def test_wer_is_corpus_weighted_not_average_of_percentages(self):
        recordings = [{"id": "a", "durationSeconds": 1, "reference": {"transcript": "bir"}},
                      {"id": "b", "durationSeconds": 9, "reference": {"transcript": "bir " * 9}}]
        records = [{"id": "a", "status": "ok", "prediction": {"transcript": "iki"}},
                   {"id": "b", "status": "ok", "prediction": {"transcript": "bir " * 9}}]
        result = b.score({"recordings": recordings}, {"task": "transcribe", "records": records})
        self.assertEqual(result["wer"], 0.1)

    def test_speaker_mapping_uses_global_optimum_not_greedy(self):
        reference = [seg("A", 0, 9), seg("B", 9, 13)]
        prediction = [seg("x", 0, 5), seg("y", 5, 9), seg("x", 9, 13)]
        result = b.diarization(reference, prediction)
        self.assertEqual(result["mapping"], {"x": "B", "y": "A"})
        self.assertEqual(result["confusionSeconds"], 5)

    def test_run_uses_argv_handles_spaces_and_measures_fresh_process(self):
        with tempfile.TemporaryDirectory(prefix="quality test ") as tmp:
            root = pathlib.Path(tmp)
            (root / "sample.wav").write_bytes(b"fixture")
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"recordings": [{"id": "one", "audio": "sample.wav",
                "durationSeconds": 1, "reference": {"transcript": "merhaba"}}]}))
            command = [sys.executable, "-c",
                       "import json,sys;json.dump({'transcript':'merhaba'},open(sys.argv[1],'w'))", "{output}"]
            result = b.run(manifest, root / "run", "fixture", "transcribe", command, 10, 4096)
            self.assertEqual(result["records"][0]["status"], "ok")
            self.assertGreater(result["records"][0]["wallSeconds"], 0)
            if sys.platform == "darwin":
                self.assertGreater(result["records"][0]["peakRssBytes"], 0)
                self.assertTrue(result["records"][0]["memoryWithinBudget"])
            self.assertEqual(b.score(json.loads(manifest.read_text()), result, audio_root=root)["wer"], 0)

    def test_timeout_is_a_failed_record_not_a_zero_error_result(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / "a.wav").write_bytes(b"fixture")
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"recordings": [{"id": "a", "audio": "a.wav",
                "durationSeconds": 1, "reference": {"transcript": "x"}}]}))
            result = b.run(manifest, root / "run", "fixture", "transcribe",
                           [sys.executable, "-c", "import time;time.sleep(20)", "{output}"], 0.1, 4096)
            self.assertEqual(result["records"][0]["status"], "failed")

    @unittest.skipUnless(os.name == "posix", "POSIX process-group cleanup")
    def test_interrupt_terminates_engine_process_group(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / "a.wav").write_bytes(b"fixture")
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"recordings": [{"id": "a", "audio": "a.wav",
                "durationSeconds": 1, "reference": {"transcript": "x"}}]}))
            pidfile = root / "engine.pid"
            code = "import os,pathlib,time;pathlib.Path(" + repr(str(pidfile)) + ").write_text(str(os.getpid()));time.sleep(30)"
            command = json.dumps([sys.executable, "-c", code, "{output}"])
            harness = subprocess.Popen([sys.executable, str(pathlib.Path(b.__file__)), "run",
                "--manifest", str(manifest), "--out", str(root / "run"), "--engine", "fixture",
                "--task", "transcribe", "--command", command],
                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
            engine_pid = None
            try:
                deadline = time.monotonic() + 5
                while not pidfile.exists() and time.monotonic() < deadline:
                    time.sleep(0.02)
                self.assertTrue(pidfile.exists(), "Engine did not start")
                engine_pid = int(pidfile.read_text())
                harness.send_signal(signal.SIGINT)
                harness.wait(timeout=5)
                deadline = time.monotonic() + 2
                while time.monotonic() < deadline:
                    state = subprocess.run(["ps", "-o", "stat=", "-p", str(engine_pid)],
                                           capture_output=True, text=True).stdout.strip()
                    if not state or state.startswith("Z"):
                        break
                    time.sleep(0.02)
                self.assertTrue(not state or state.startswith("Z"), "Interrupted run left engine resident")
            finally:
                if harness.poll() is None:
                    harness.kill()
                    harness.wait()
                if engine_pid is not None:
                    try:
                        os.killpg(os.getpgid(engine_pid), signal.SIGKILL)
                    except ProcessLookupError:
                        pass


if __name__ == "__main__":
    unittest.main()
