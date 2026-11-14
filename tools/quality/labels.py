"""İnsan etiketlerini (basit metin) pilot manifestine çevirir.

Biçim — her satır bir konuşma bölgesi; boş satır ve '#' ile başlayan satırlar atlanır:

    <konuşmacı> <başlangıç>-<bitiş> <duyulduğu gibi metin>
    A 0:00-0:01.2 Tufan Bey niye sizinle çalışalım
    B 1,8-3.76 Ben niye sizinle çalışayım

Zaman: saniye (12.5 / 12,5), dk:sn (0:07.2) veya sa:dk:sn. Üst üste konuşma iki ayrı satırla yazılır.
Konuşmacı tek sözcüklük anonim etiket (A, B, K1…). Sessizlik etiketlenmez.

    python3 tools/quality/labels.py --manifest quality-local/pilot.json --id pilot-01 \\
        --labels quality-local/audio/pilot-01.txt --audio audio/pilot-01.wav \\
        --split development --participants person01,person02 --source real --conditions clean,turn-taking

`--audio` manifest dosyasına göre yoldur. Kayıt insan tarafından dinlenip kontrol edildiyse `--reviewed`
verin; aksi halde referenceStatus=pending kalır ve `benchmark.py prepare` kaydı kabul etmez.
"""
import argparse
import json
import pathlib
import re
import sys
import wave

_LINE = re.compile(r"^(\S+)\s+([0-9:.,]+)\s*-\s*([0-9:.,]+)\s+(.+)$")
SPLITS = ("development", "test")
SOURCES = ("real", "public", "synthetic")


def _seconds(token):
    parts = token.replace(",", ".").split(":")
    if not 1 <= len(parts) <= 3 or any(p == "" for p in parts):
        raise ValueError(token)
    value = 0.0
    for p in parts:
        value = value * 60 + float(p)
    return value


def parse(text):
    """Etiket metnini (transkript, konuşmacı bölgeleri) çiftine çevirir; bölgeler başlangıca göre sıralıdır."""
    rows = []
    for number, raw in enumerate(text.splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        match = _LINE.match(line)
        try:
            if not match:
                raise ValueError("biçim '<konuşmacı> <başlangıç>-<bitiş> <metin>' değil")
            speaker, start, end, words = match.group(1), _seconds(match.group(2)), _seconds(match.group(3)), match.group(4).strip()
            if end <= start:
                raise ValueError("bitiş başlangıçtan sonra olmalı")
        except ValueError as error:
            raise ValueError(f"satır {number}: {error} → {raw!r}") from None
        rows.append((start, end, speaker, words))
    if not rows:
        raise ValueError("Etiket dosyası boş: hiç konuşma satırı yok.")
    rows.sort(key=lambda r: (r[0], r[1]))
    transcript = " ".join(r[3] for r in rows)
    speakers = [{"speaker": s, "start": round(a, 3), "end": round(b, 3)} for a, b, s, _ in rows]
    return transcript, speakers


def _duration(path):
    with wave.open(str(path), "rb") as stream:
        return stream.getnframes() / float(stream.getframerate())


def upsert(manifest, record_id, labels_path, audio, split, participants, source, conditions, reviewed):
    """Kaydı manifestte oluşturur ya da aynı kimlikli kaydı tamamen değiştirir; diğer kayıtlar korunur."""
    if split not in SPLITS:
        raise ValueError(f"split {SPLITS} olmalı: {split}")
    if source not in SOURCES:
        raise ValueError(f"source {SOURCES} olmalı: {source}")
    manifest = pathlib.Path(manifest)
    duration = _duration(manifest.parent / audio)
    transcript, speakers = parse(pathlib.Path(labels_path).read_text(encoding="utf-8"))
    late = [r for r in speakers if r["end"] > duration + 0.05]
    if late:
        raise ValueError(f"Bölge ses süresini ({duration:.2f} sn) aşıyor: {late[0]}")
    row = {
        "id": record_id, "audio": str(audio), "durationSeconds": round(duration, 3), "expectedSpeakers": 0,
        "split": split, "participantIds": list(participants), "sourceKind": source,
        "conditions": list(conditions), "referenceStatus": "reviewed" if reviewed else "pending",
        "reference": {"transcript": transcript, "speakers": speakers},
    }
    data = json.loads(manifest.read_text(encoding="utf-8")) if manifest.exists() else {"recordings": []}
    rows = data.setdefault("recordings", [])
    for i, existing in enumerate(rows):
        if existing.get("id") == record_id:
            rows[i] = row
            break
    else:
        rows.append(row)
    manifest.parent.mkdir(parents=True, exist_ok=True)
    manifest.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return row


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--manifest", required=True)
    p.add_argument("--id", required=True)
    p.add_argument("--labels", required=True)
    p.add_argument("--audio", required=True, help="manifeste göre WAV yolu (16 kHz mono PCM16)")
    p.add_argument("--split", required=True, choices=SPLITS)
    p.add_argument("--participants", required=True, help="virgülle anonim kimlikler: person01,person02")
    p.add_argument("--source", required=True, choices=SOURCES)
    p.add_argument("--conditions", default="")
    p.add_argument("--reviewed", action="store_true", help="referansı bir insan dinleyip kontrol etti")
    a = p.parse_args(argv)
    try:
        row = upsert(a.manifest, a.id, a.labels, a.audio, a.split,
                     [x for x in a.participants.split(",") if x], a.source,
                     [x for x in a.conditions.split(",") if x], a.reviewed)
    except (ValueError, OSError, wave.Error) as error:
        print(f"Hata: {error}", file=sys.stderr)
        return 2
    print(f"{row['id']}: {len(row['reference']['speakers'])} bölge, {row['durationSeconds']} sn, "
          f"referenceStatus={row['referenceStatus']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
