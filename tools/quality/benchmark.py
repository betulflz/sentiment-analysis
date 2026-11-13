"""Local, sequential quality pilot. Python 3.9+, standard library only."""
import argparse
import collections
import hashlib
import json
import math
import os
import pathlib
import platform
import re
import signal
import subprocess
import time
import unicodedata
import dataset
import preflight


def words(text):
    text = unicodedata.normalize("NFC", text).replace("İ", "i").replace("I", "ı").lower()
    return "".join(c if c.isalnum() else " " for c in text).split()


def edit_distance(reference, prediction):
    previous = list(range(len(prediction) + 1))
    for i, expected in enumerate(reference, 1):
        current = [i]
        for j, actual in enumerate(prediction, 1):
            current.append(min(current[-1] + 1, previous[j] + 1,
                               previous[j - 1] + (expected != actual)))
        previous = current
    return previous[-1]


def finite_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def validate_prediction(value, task, duration):
    if not isinstance(value, dict):
        raise ValueError("Motor çıktısı JSON nesnesi olmalı.")
    if "durationSeconds" in value:
        actual = value["durationSeconds"]
        if not finite_number(actual) or abs(actual - duration) > 0.05:
            raise ValueError("Ses süresi manifest ile uyuşmuyor (tolerans 50 ms).")
    if task in ("transcribe", "analyze"):
        if not isinstance(value.get("transcript"), str):
            raise ValueError("transcript metni eksik.")
        if task == "analyze":
            if type(value.get("complete")) is not bool or not isinstance(value.get("diagnostics"), dict):
                raise ValueError("analyze çıktısında complete ve diagnostics gerekli.")
            diagnostics = value["diagnostics"]
            captured, evaluated, missing = (diagnostics.get(k) for k in
                                            ("capturedSeconds", "evaluatedSeconds", "unevaluatedSeconds"))
            if (not all(finite_number(v) and v >= 0 for v in (captured, evaluated, missing))
                    or evaluated > captured + 1e-6 or captured > duration + 0.05
                    or abs(captured - evaluated - missing) > 1e-6
                    or type(diagnostics.get("hasIssues")) is not bool
                    or not isinstance(diagnostics.get("chunks"), list)):
                raise ValueError("analyze girdi kapsamı tutarsız.")
            if value["complete"] and (diagnostics["hasIssues"] or abs(captured - duration) > 0.05
                                      or missing > 1e-6 or diagnostics.get("sourceStatus") != "completed"):
                raise ValueError("Hatalı veya eksik analiz complete=true olamaz.")
    elif task == "diarize":
        if not isinstance(value.get("speakers"), list):
            raise ValueError("speakers listesi eksik.")
        for item in value["speakers"]:
            if not isinstance(item, dict) or not isinstance(item.get("speaker"), str) or not item["speaker"]:
                raise ValueError("Her bölgede anonim speaker metni gerekli.")
            start, end = item.get("start"), item.get("end")
            if not finite_number(start) or not finite_number(end) or not 0 <= start < end <= duration:
                raise ValueError("Bölge süreleri sonlu olmalı: 0 <= start < end <= durationSeconds.")
    else:
        raise ValueError("task: transcribe veya diarize olmalı.")


def load_manifest(path, task, require_audio=False):
    value = json.loads(path.read_text(encoding="utf-8"))
    rows = value.get("recordings")
    if not isinstance(rows, list) or not rows:
        raise ValueError("Manifest boş olamaz: recordings listesi gerekli.")
    ids = set()
    for row in rows:
        key = row.get("id", "")
        if not isinstance(key, str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", key) or key in ids:
            raise ValueError("Kayıt id tekil, 1–64 harf/rakam/_/- olmalı.")
        ids.add(key)
        duration = row.get("durationSeconds")
        if not finite_number(duration) or duration <= 0:
            raise ValueError("durationSeconds pozitif ve sonlu olmalı.")
        validate_prediction(row.get("reference"), "transcribe" if task == "analyze" else task, duration)
        count = row.get("expectedSpeakers", 0)
        if type(count) is not int or not 0 <= count <= 8:
            raise ValueError("expectedSpeakers 0–8 arası tam sayı olmalı (0=bilinmiyor).")
        if require_audio:
            audio = row.get("audio")
            if not isinstance(audio, str) or not (path.parent / audio).is_file():
                raise ValueError("Ses dosyası bulunamadı: " + str(audio))
    return value


def fingerprint(manifest):
    return hashlib.sha256(json.dumps(manifest, sort_keys=True, ensure_ascii=False).encode()).hexdigest()


def diarization(reference, prediction):
    """Exact speaker mapping; zero collar, overlap included, duplicate intervals unioned."""
    refs = list(dict.fromkeys(s["speaker"] for s in reference))
    hyps = list(dict.fromkeys(s["speaker"] for s in prediction))
    if len(refs) > 8 or len(hyps) > 32:
        raise ValueError("Pilot sınırı: en fazla 8 referans / 32 tahmini konuşmacı.")
    events = collections.defaultdict(list)
    for side, rows in enumerate([reference, prediction]):
        for s in rows:
            events[s["start"]].append((side, s["speaker"], 1))
            events[s["end"]].append((side, s["speaker"], -1))
    active = [collections.Counter(), collections.Counter()]
    cells, overlap = [], collections.defaultdict(float)
    previous = None
    for at in sorted(events):
        if previous is not None and at > previous:
            r, h = (set(k for k, v in a.items() if v > 0) for a in active)
            seconds = at - previous
            cells.append((seconds, r, h))
            for a in r:
                for z in h:
                    overlap[a, z] += seconds
        for side, speaker, delta in events[at]:
            active[side][speaker] += delta
        previous = at
    # DP assigns each hypothesis to at most one reference; extra hypotheses remain unmatched.
    states = {0: (0.0, {})}
    for h in hyps:
        updated = dict(states)
        for mask, (weight, mapping) in states.items():
            for i, r in enumerate(refs):
                if mask & (1 << i):
                    continue
                next_mask = mask | (1 << i)
                candidate = weight + overlap[r, h]
                if next_mask not in updated or candidate > updated[next_mask][0]:
                    updated[next_mask] = (candidate, dict(mapping, **{h: r}))
        states = updated
    mapping = max(states.values(), key=lambda x: x[0])[1]
    miss = false_alarm = confusion = denominator = 0.0
    for seconds, r, h in cells:
        correct = sum(mapping.get(s) in r for s in h)
        denominator += seconds * len(r)
        miss += seconds * max(0, len(r) - len(h))
        false_alarm += seconds * max(0, len(h) - len(r))
        confusion += seconds * (min(len(r), len(h)) - correct)
    errors = miss + false_alarm + confusion
    return {"der": errors / denominator if denominator else None,
            "referenceSeconds": denominator, "errorSeconds": errors,
            "missSeconds": miss, "falseAlarmSeconds": false_alarm,
            "confusionSeconds": confusion, "referenceSpeakers": len(refs),
            "predictedSpeakers": len(hyps), "speakerCountError": abs(len(refs) - len(hyps)),
            "mapping": mapping}


def score(manifest, run_result, audio_root=None):
    task = run_result["task"]
    if run_result.get("manifestSha256", fingerprint(manifest)) != fingerprint(manifest):
        raise ValueError("Manifest değişmiş; koşuyu aynı referansla skorlayın.")
    if "audioSha256" in run_result:
        if audio_root is None:
            raise ValueError("Ses hash doğrulaması için manifest klasörü gerekli.")
        if dataset.audio_hashes(manifest, audio_root) != run_result["audioSha256"]:
            raise ValueError("Ses dosyaları koşudan sonra değişmiş.")
    rows = run_result["records"]
    by_id = {r["id"]: r for r in rows}
    if len(by_id) != len(rows) or set(by_id) != {r["id"] for r in manifest["recordings"]}:
        raise ValueError("Eksik, fazla veya yinelenen kayıt var; kısmi koşu skorlanmaz.")
    totals = collections.Counter()
    details = []
    for row in manifest["recordings"]:
        result = by_id[row["id"]]
        if result["status"] != "ok":
            raise ValueError("Başarısız kayıt skorlanamaz: " + row["id"])
        prediction = result["prediction"]
        validate_prediction(prediction, task, row["durationSeconds"])
        validate_prediction(row["reference"], "transcribe" if task == "analyze" else task, row["durationSeconds"])
        if task == "analyze" and not prediction["complete"]:
            raise ValueError("Eksik analiz başarı olarak skorlanamaz.")
        if task in ("transcribe", "analyze"):
            reference = words(row["reference"]["transcript"])
            actual = words(prediction["transcript"])
            # CER uses normalized text with one space between words.
            chars, predicted_chars = " ".join(reference), " ".join(actual)
            detail = {"wordErrors": edit_distance(reference, actual), "referenceWords": len(reference),
                      "characterErrors": edit_distance(chars, predicted_chars), "referenceCharacters": len(chars)}
        else:
            detail = diarization(row["reference"]["speakers"], prediction["speakers"])
        for key, value in detail.items():
            if isinstance(value, (int, float)) and key not in ("der",):
                totals[key] += value
        details.append(dict(detail, id=row["id"]))
    summary = dict(totals)
    if task in ("transcribe", "analyze"):
        summary.update(wer=totals["wordErrors"] / totals["referenceWords"] if totals["referenceWords"] else None,
                       cer=totals["characterErrors"] / totals["referenceCharacters"] if totals["referenceCharacters"] else None)
    else:
        summary.update(der=totals["errorSeconds"] / totals["referenceSeconds"] if totals["referenceSeconds"] else None,
                       protocol={"collarSeconds": 0, "includeOverlap": True})
    result = dict(summary, task=task, records=details)
    splits = {row.get("split", "unspecified") for row in manifest["recordings"]}
    if len(splits) > 1:
        result["aggregateScope"] = "mixed splits; use bySplit.test for held-out evaluation"
        result["bySplit"] = {}
        for split in sorted(splits):
            subset = [r for r in manifest["recordings"] if r.get("split", "unspecified") == split]
            keys = {r["id"] for r in subset}
            result["bySplit"][split] = score({"recordings": subset},
                {"task": task, "records": [r for r in rows if r["id"] in keys]})
    return result


def run(manifest_path, output, engine, task, command, timeout, memory_limit_mib):
    manifest_path = pathlib.Path(manifest_path).resolve()
    manifest = load_manifest(manifest_path, task, require_audio=True)
    hashes = dataset.audio_hashes(manifest, manifest_path.parent)
    if not isinstance(command, list) or not command or not all(isinstance(a, str) for a in command):
        raise ValueError("command, JSON argv listesi olmalı.")
    if not any("{output}" in a for a in command):
        raise ValueError("command içinde {output} gerekli.")
    if not finite_number(timeout) or timeout <= 0 or not finite_number(memory_limit_mib) or memory_limit_mib <= 0:
        raise ValueError("timeout ve memory-limit-mib pozitif olmalı.")
    output = pathlib.Path(output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    report = {"version": 1, "engine": engine, "task": task, "manifestSha256": fingerprint(manifest),
              "audioSha256": hashes, "sourceState": dataset.source_state(),
              "datasetProtocol": manifest.get("datasetProtocol", "unpartitioned"),
              "platform": platform.platform(), "machine": platform.machine(), "command": command,
              "memoryBudgetMiB": memory_limit_mib, "memoryBudgetEnforced": False,
              "processMode": "fresh process per recording; sequential; includes model loading",
              "records": []}
    for row in manifest["recordings"]:
        target = output / (row["id"] + ".prediction.json")
        replacements = {"{audio}": str((manifest_path.parent / row["audio"]).resolve()),
                        "{output}": str(target), "{speakers}": str(row.get("expectedSpeakers", 0))}
        argv = list(command)
        for key, value in replacements.items():
            argv = [arg.replace(key, value) for arg in argv]
        measured = platform.system() == "Darwin"
        if measured:
            argv = ["/usr/bin/time", "-l"] + argv
        result = {"id": row["id"], "status": "failed", "peakRssBytes": None}
        started = time.monotonic()
        log_path = output / (row["id"] + ".stderr.log")
        with log_path.open("w") as err, (output / (row["id"] + ".stdout.log")).open("w") as out:
            process = None
            try:
                process = subprocess.Popen(argv, stdout=out, stderr=err, start_new_session=os.name == "posix")
                try:
                    code = process.wait(timeout=timeout)
                except subprocess.TimeoutExpired:
                    if os.name == "posix":
                        os.killpg(process.pid, signal.SIGKILL)
                    else:
                        process.kill()
                    process.wait()
                    raise ValueError("Zaman bütçesi aşıldı.")
                result["exitCode"] = code
                if code != 0 and not (task == "analyze" and code == 3):
                    raise ValueError("Motor başarısız; stderr günlüğünü inceleyin.")
                prediction = json.loads(target.read_text(encoding="utf-8"))
                validate_prediction(prediction, task, row["durationSeconds"])
                partial = task == "analyze" and not prediction["complete"]
                result.update(status="partial" if partial else "ok", prediction=prediction)
                if dataset.file_sha256(replacements["{audio}"]) != hashes[row["id"]]:
                    raise ValueError("Ses dosyası koşu sırasında değişmiş.")
            except (OSError, ValueError) as error:
                result["status"] = "failed"
                result["error"] = str(error)
            finally:
                # Ctrl+C / unexpected exceptions must not leave a model resident. The
                # child has its own session, so terminal signals do not reach it.
                if process is not None and process.poll() is None:
                    try:
                        if os.name == "posix":
                            os.killpg(process.pid, signal.SIGKILL)
                        else:
                            process.kill()
                    except ProcessLookupError:
                        pass
                    process.wait()
        result["wallSeconds"] = time.monotonic() - started
        result["realTimeFactor"] = result["wallSeconds"] / row["durationSeconds"]
        if measured:
            match = re.search(r"(\d+)\s+maximum resident set size", log_path.read_text(errors="replace"))
            if match:
                result["peakRssBytes"] = int(match.group(1))
        result["memoryWithinBudget"] = (None if result["peakRssBytes"] is None
                                        else result["peakRssBytes"] <= memory_limit_mib * 1024 * 1024)
        report["records"].append(result)
        (output / "run.json").write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False), encoding="utf-8")
    return report


def render_report(manifest, run_result, audio_root=None):
    kinds = {r.get("sourceKind", "unknown") for r in manifest["recordings"]}
    provenance = "sentetik veri; gerçek doğruluk ölçümü değildir" if "synthetic" in kinds else (
        "kökeni belirtilmemiş veri; kalite iddiası doğrulanmamış" if "unknown" in kinds else "insan referansları ayrıca doğrulanmalıdır")
    lines = ["**İterasyon 1 başlangıç raporu**", "", "Veri: " + provenance + ".", "",
             "Veri protokolü: " + manifest.get("datasetProtocol", "belirtilmemiş") + ".", "",
             "Görev: " + run_result["task"] + ". Motor: " + run_result.get("engine", "belirtilmemiş") + ".", "",
             "Girdi kapsamı doğruluk oranı değildir. Bellek bütçesi ölçümseldir, sert sınır uygulanmaz.", "",
             "| Kayıt | Durum | Süre (sn) | RTF | Tepe RSS (MiB) |", "|---|---|---:|---:|---:|"]
    for row in run_result["records"]:
        def number(value):
            return "—" if value is None else f"{value:.3f}"
        rss = row.get("peakRssBytes")
        lines.append(f"| {row['id']} | {row['status']} | {number(row.get('wallSeconds'))} | "
                     f"{number(row.get('realTimeFactor'))} | {number(rss / 1048576 if rss is not None else None)} |")
    try:
        scores = score(manifest, run_result, audio_root)
        lines += ["", "Skorlar (0–1 oranı; düşük daha iyi):", "", "```json", json.dumps(scores, ensure_ascii=False, indent=2), "```"]
    except (OSError, ValueError) as error:
        lines += ["", "Toplu skor hesaplanmadı: " + str(error)]
    for row in run_result["records"]:
        diagnostics = row.get("prediction", {}).get("diagnostics")
        if diagnostics is not None:
            counts = collections.Counter(c.get("disposition", "unknown") for c in diagnostics.get("chunks", []))
            lines += ["", f"{row['id']} girdi gözlemi: " + json.dumps({"capturedSeconds": diagnostics.get("capturedSeconds"),
                "evaluatedSeconds": diagnostics.get("evaluatedSeconds"), "unevaluatedSeconds": diagnostics.get("unevaluatedSeconds"),
                "hasIssues": diagnostics.get("hasIssues"), "chunks": counts}, ensure_ascii=False)]
    lines += ["", "Kaynak kimliği: " + json.dumps(run_result.get("sourceState", {}), ensure_ascii=False), "",
              "Bu rapor duygu veya öneri doğruluğunu ölçmez. analyze koşusu Avalonia, SQLite, kamera ve konuşmacı geçişini içermez."]
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    runner = commands.add_parser("run")
    runner.add_argument("--manifest", type=pathlib.Path, required=True)
    runner.add_argument("--out", type=pathlib.Path, required=True)
    runner.add_argument("--engine", required=True)
    runner.add_argument("--task", choices=["transcribe", "diarize", "analyze"], required=True)
    runner.add_argument("--command", required=True, help="JSON argv; {audio}, {output}, {speakers}")
    runner.add_argument("--timeout", type=float, default=600)
    runner.add_argument("--memory-limit-mib", type=float, default=4096)
    scorer = commands.add_parser("score")
    scorer.add_argument("--manifest", type=pathlib.Path, required=True)
    scorer.add_argument("--run", type=pathlib.Path, required=True)
    preparer = commands.add_parser("prepare")
    preparer.add_argument("--manifest", type=pathlib.Path, required=True)
    preparer.add_argument("--out", type=pathlib.Path, required=True)
    reporter = commands.add_parser("report")
    reporter.add_argument("--manifest", type=pathlib.Path, required=True)
    reporter.add_argument("--run", type=pathlib.Path, required=True)
    reporter.add_argument("--out", type=pathlib.Path, required=True)
    doctor = commands.add_parser("doctor")
    doctor.add_argument("--models", type=pathlib.Path, required=True)
    doctor.add_argument("--task", choices=["transcribe", "diarize", "analyze"], required=True)
    doctor.add_argument("--model", default="large-v3-turbo")
    doctor.add_argument("--quantization", default="q5_0")
    args = parser.parse_args()
    try:
        if args.action == "doctor":
            result = preflight.check(args.models, args.task, args.model, args.quantization)
            print(json.dumps(result, ensure_ascii=False, indent=2))
            return 0 if result["modelsReady"] and result["dotnet"] else 2
        if args.action == "prepare":
            raw = json.loads(args.manifest.read_text(encoding="utf-8"))
            rows = raw.get("recordings", [])
            tasks = [task for task, field in (("transcribe", "transcript"), ("diarize", "speakers"))
                     if rows and all(isinstance(row.get("reference"), dict) and field in row["reference"] for row in rows)]
            if not tasks:
                raise ValueError("Tüm kayıtlarda ortak bir referans görevi gerekli: transcript veya speakers.")
            for task in tasks:
                load_manifest(args.manifest, task, require_audio=True)
            value = dataset.prepare(args.manifest, args.out)
            print(json.dumps({"manifest": str(args.out), "records": len(value["recordings"])}))
            return 0
        if args.action == "run":
            result = run(args.manifest, args.out, args.engine, args.task, json.loads(args.command),
                         args.timeout, args.memory_limit_mib)
            failures = sum(r["status"] != "ok" for r in result["records"])
            print(json.dumps({"report": str(args.out / "run.json"), "failedRecords": failures}))
            return 1 if failures else 0
        report = json.loads(args.run.read_text(encoding="utf-8"))
        manifest = load_manifest(args.manifest, report["task"])
        if args.action == "report":
            with args.out.open("x", encoding="utf-8") as stream:
                stream.write(render_report(manifest, report, args.manifest.resolve().parent))
            print(str(args.out))
            return 0
        print(json.dumps(score(manifest, report, args.manifest.resolve().parent), ensure_ascii=False, indent=2, allow_nan=False))
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(2, str(error) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
