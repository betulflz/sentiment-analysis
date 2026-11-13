import json
import pathlib
import sys
import tempfile
import unittest
import wave

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import labels


def write_wav(path, seconds):
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(16000)
        w.writeframes(b"\x00\x00" * int(16000 * seconds))


class ParseTests(unittest.TestCase):
    def test_parses_speaker_times_and_text_in_time_order(self):
        text = "# yorum\nB 1,8-3.76 Ben niye\n\nA 0:00-0:01.2 Tufan Bey niye sizinle\n"
        transcript, speakers = labels.parse(text)
        self.assertEqual(transcript, "Tufan Bey niye sizinle Ben niye")
        self.assertEqual(speakers, [
            {"speaker": "A", "start": 0.0, "end": 1.2},
            {"speaker": "B", "start": 1.8, "end": 3.76},
        ])

    def test_accepts_hours(self):
        _, speakers = labels.parse("A 1:00:01-1:00:02.5 metin")
        self.assertEqual(speakers[0]["start"], 3601.0)
        self.assertEqual(speakers[0]["end"], 3602.5)

    def test_keeps_overlapping_regions(self):
        _, speakers = labels.parse("A 0-5 bir\nB 4-6 iki")
        self.assertEqual(len(speakers), 2)

    def test_line_number_in_error_for_bad_line(self):
        with self.assertRaisesRegex(ValueError, "satır 2"):
            labels.parse("A 0-1 iyi\nbu satır bozuk")

    def test_end_must_be_after_start(self):
        with self.assertRaisesRegex(ValueError, "satır 1"):
            labels.parse("A 5-5 sıfır süre")

    def test_empty_text_is_error(self):
        with self.assertRaisesRegex(ValueError, "boş"):
            labels.parse("# yalnız yorum\n")


class ManifestTests(unittest.TestCase):
    def test_creates_manifest_with_pending_status_and_real_duration(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            (root / "audio").mkdir()
            write_wav(root / "audio" / "p1.wav", 4.0)
            (root / "p1.txt").write_text("A 0-1.5 merhaba\nB 2-3.9 selam", encoding="utf-8")
            manifest = root / "pilot.json"
            labels.upsert(manifest, "pilot-01", root / "p1.txt", "audio/p1.wav", "development",
                          ["person01", "person02"], "public", ["clean"], reviewed=False)
            data = json.loads(manifest.read_text(encoding="utf-8"))
            row = data["recordings"][0]
            self.assertEqual(row["id"], "pilot-01")
            self.assertEqual(row["referenceStatus"], "pending")
            self.assertAlmostEqual(row["durationSeconds"], 4.0, places=3)
            self.assertEqual(row["sourceKind"], "public")
            self.assertEqual(row["reference"]["transcript"], "merhaba selam")
            self.assertEqual(len(row["reference"]["speakers"]), 2)

    def test_replaces_existing_row_and_keeps_others(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            write_wav(root / "a.wav", 3.0)
            (root / "l.txt").write_text("A 0-1 bir", encoding="utf-8")
            manifest = root / "m.json"
            manifest.write_text(json.dumps({"recordings": [{"id": "other"}, {"id": "pilot-01", "old": 1}]}), encoding="utf-8")
            labels.upsert(manifest, "pilot-01", root / "l.txt", "a.wav", "test", ["p9"], "real", [], reviewed=True)
            rows = json.loads(manifest.read_text(encoding="utf-8"))["recordings"]
            self.assertEqual([r["id"] for r in rows], ["other", "pilot-01"])
            self.assertNotIn("old", rows[1])
            self.assertEqual(rows[1]["referenceStatus"], "reviewed")

    def test_region_beyond_audio_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            write_wav(root / "a.wav", 2.0)
            (root / "l.txt").write_text("A 0-2.5 uzun", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "süre"):
                labels.upsert(root / "m.json", "x", root / "l.txt", "a.wav", "development", ["p1"], "real", [], reviewed=False)

    def test_invalid_split_or_source_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            write_wav(root / "a.wav", 2.0)
            (root / "l.txt").write_text("A 0-1 x", encoding="utf-8")
            with self.assertRaises(ValueError):
                labels.upsert(root / "m.json", "x", root / "l.txt", "a.wav", "train", ["p1"], "real", [], reviewed=False)
            with self.assertRaises(ValueError):
                labels.upsert(root / "m.json", "x", root / "l.txt", "a.wav", "test", ["p1"], "tv", [], reviewed=False)


if __name__ == "__main__":
    unittest.main()
