import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
import wave

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import benchmark as b
import dataset


def wav(path):
    with wave.open(str(path), "wb") as stream:
        stream.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
        stream.writeframes(b"\0\0" * 16000)


def row(key, participant, split):
    return {"id": key, "audio": "a.wav", "durationSeconds": 1,
            "participantIds": [participant], "split": split, "sourceKind": "synthetic",
            "conditions": ["silence"], "referenceStatus": "reviewed",
            "reference": {"transcript": "", "speakers": []}}


class IterationOneTests(unittest.TestCase):
    def test_analyze_cannot_claim_complete_with_missing_input_or_failures(self):
        for captured, evaluated, issues in [(1, 0, True), (0.5, 0.5, False)]:
            with self.subTest(captured=captured), self.assertRaises(ValueError):
                b.validate_prediction({"transcript": "", "complete": True, "diagnostics": {
                    "capturedSeconds": captured, "evaluatedSeconds": evaluated,
                    "unevaluatedSeconds": captured - evaluated, "hasIssues": issues,
                    "sourceStatus": "completed", "chunks": []}}, "analyze", 1)

    def test_prepare_freezes_audio_hash_and_resolves_paths_when_manifest_moves(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            wav(root / "a.wav")
            source = root / "input.json"
            source.write_text(json.dumps({"recordings": [row("a", "person1", "test")]}))
            frozen = dataset.prepare(source, root / "other" / "frozen.json")
            self.assertEqual(frozen["recordings"][0]["audioSha256"], dataset.file_sha256(root / "a.wav"))
            self.assertTrue(pathlib.Path(frozen["recordings"][0]["audio"]).is_absolute())
            self.assertEqual(frozen["datasetProtocol"], "participant-disjoint-v1")

    def test_prepare_rejects_participant_leakage(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            wav(root / "a.wav")
            source = root / "input.json"
            source.write_text(json.dumps({"recordings": [row("a", "person1", "development"), row("b", "person1", "test")]}))
            with self.assertRaisesRegex(ValueError, "katılımcı"):
                dataset.prepare(source, root / "frozen.json")

    def test_prepare_rejects_same_audio_across_splits_even_with_different_participant_ids(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            wav(root / "a.wav")
            source = root / "input.json"
            source.write_text(json.dumps({"recordings": [row("a", "person1", "development"), row("b", "person2", "test")]}))
            with self.assertRaisesRegex(ValueError, "ses"):
                dataset.prepare(source, root / "frozen.json")

    def test_prepare_rejects_incorrect_duration(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            wav(root / "a.wav")
            item = row("a", "person1", "test")
            item["durationSeconds"] = 10
            source = root / "input.json"
            source.write_text(json.dumps({"recordings": [item]}))
            with self.assertRaisesRegex(ValueError, "süre"):
                dataset.prepare(source, root / "frozen.json")

    def test_prepare_rejects_unreviewed_reference(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            wav(root / "a.wav")
            item = row("a", "person1", "test")
            item["referenceStatus"] = "pending"
            source = root / "input.json"
            source.write_text(json.dumps({"recordings": [item]}))
            with self.assertRaisesRegex(ValueError, "referans"):
                dataset.prepare(source, root / "frozen.json")

    def test_prepare_cli_accepts_diarization_only_reference(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            wav(root / "a.wav")
            item = row("a", "person1", "test")
            del item["reference"]["transcript"]
            source = root / "input.json"
            source.write_text(json.dumps({"recordings": [item]}))
            result = subprocess.run([sys.executable, str(pathlib.Path(b.__file__)), "prepare",
                "--manifest", str(source), "--out", str(root / "frozen.json")], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_score_rejects_changed_audio(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            audio = root / "a.wav"
            wav(audio)
            manifest = {"recordings": [row("a", "person1", "test")]}
            result = {"task": "transcribe", "audioSha256": {"a": dataset.file_sha256(audio)},
                      "records": [{"id": "a", "status": "ok", "prediction": {"transcript": ""}}]}
            audio.write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "Ses"):
                b.score(manifest, result, audio_root=root)

    def test_analyze_partial_output_is_valid_evidence_but_not_a_successful_score(self):
        prediction = {"transcript": "", "complete": False,
                      "diagnostics": {"hasIssues": True, "capturedSeconds": 1,
                                      "evaluatedSeconds": 0, "unevaluatedSeconds": 1, "chunks": []}}
        b.validate_prediction(prediction, "analyze", 1)
        manifest = {"recordings": [row("a", "person1", "test")]}
        result = {"task": "analyze", "records": [{"id": "a", "status": "partial", "prediction": prediction}]}
        report = b.render_report(manifest, result)
        self.assertIn("partial", report)
        self.assertIn("hesaplanmadı", report)
        self.assertIn("sentetik", report.lower())

    def test_analyze_exit_three_preserves_partial_diagnostics(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            wav(root / "a.wav")
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"recordings": [row("a", "person1", "test")]}))
            prediction = {"transcript": "", "complete": False, "diagnostics": {
                "hasIssues": True, "capturedSeconds": 1, "evaluatedSeconds": 0,
                "unevaluatedSeconds": 1, "chunks": [{"disposition": "ProcessingFailed"}]}}
            code = "import json,sys;json.dump(" + repr(prediction) + ",open(sys.argv[1],'w'));sys.exit(3)"
            result = b.run(manifest, root / "run", "synthetic", "analyze",
                           [sys.executable, "-c", code, "{output}"], 10, 4096)
            self.assertEqual(result["records"][0]["status"], "partial")
            self.assertEqual(result["records"][0]["prediction"]["diagnostics"]["evaluatedSeconds"], 0)
            report = b.render_report(json.loads(manifest.read_text()), result, root)
            self.assertIn("ProcessingFailed", report)
            self.assertIn("hesaplanmadı", report)

    def test_report_keeps_development_and_test_scores_separate(self):
        first, second = row("a", "person1", "development"), row("b", "person2", "test")
        for item in (first, second):
            item["reference"]["transcript"] = "merhaba"
        manifest = {"recordings": [first, second]}
        result = {"task": "transcribe", "records": [
            {"id": "a", "status": "ok", "prediction": {"transcript": "merhaba"}},
            {"id": "b", "status": "ok", "prediction": {"transcript": "yanlış"}}]}
        score = b.score(manifest, result)
        self.assertEqual(score["bySplit"]["development"]["wer"], 0)
        self.assertEqual(score["bySplit"]["test"]["wer"], 1)


if __name__ == "__main__":
    unittest.main()
