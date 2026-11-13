"""Dataset identity and participant-disjoint pilot preparation; no model dependencies."""
import hashlib
import json
import pathlib
import subprocess
import wave


def file_sha256(path):
    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def audio_hashes(manifest, root):
    result = {}
    for row in manifest["recordings"]:
        value = file_sha256(pathlib.Path(root) / row["audio"])
        if row.get("audioSha256", value) != value:
            raise ValueError("Ses dosyası hazırlanmış veri kümesinden farklı: " + row["id"])
        result[row["id"]] = value
    return result


def prepare(source, destination):
    source, destination = pathlib.Path(source).resolve(), pathlib.Path(destination).resolve()
    manifest = json.loads(source.read_text(encoding="utf-8"))
    rows = manifest.get("recordings")
    if not isinstance(rows, list) or not rows:
        raise ValueError("recordings listesi boş olamaz.")
    participants, hashes, ids = {}, {}, set()
    for row in rows:
        if row.get("referenceStatus") != "reviewed":
            raise ValueError("İnsan tarafından kontrol edilmiş referans gerekli: referenceStatus=reviewed.")
        if row["id"] in ids:
            raise ValueError("Kayıt id yineleniyor.")
        ids.add(row["id"])
        split = row.get("split")
        if split not in ("development", "test"):
            raise ValueError("Her kayıtta split=development veya test gerekli.")
        people = row.get("participantIds")
        if not isinstance(people, list) or not people or not all(isinstance(p, str) and p for p in people):
            raise ValueError("Anonim participantIds listesi gerekli.")
        for person in people:
            if participants.get(person, split) != split:
                raise ValueError("Aynı katılımcı geliştirme/test ayrımına sızıyor: " + person)
            participants[person] = split
        if row.get("sourceKind") not in ("real", "synthetic", "public"):
            raise ValueError("sourceKind=real, public veya synthetic gerekli.")
        conditions = row.get("conditions")
        if not isinstance(conditions, list) or not conditions or not all(isinstance(c, str) and c for c in conditions):
            raise ValueError("Kayıt koşullarını belirten conditions listesi gerekli.")
        audio = (source.parent / row["audio"]).resolve()
        try:
            with wave.open(str(audio), "rb") as stream:
                if (stream.getnchannels(), stream.getframerate(), stream.getsampwidth()) != (1, 16000, 2):
                    raise ValueError("Pilot WAV girdisi 16 kHz mono PCM16 olmalı.")
                seconds = stream.getnframes() / stream.getframerate()
        except wave.Error as error:
            raise ValueError("Geçersiz PCM WAV.") from error
        if seconds <= 0 or abs(seconds - row["durationSeconds"]) > 0.05:
            raise ValueError("Manifest süre bilgisi gerçek WAV ile uyuşmuyor.")
        digest = file_sha256(audio)
        if hashes.get(digest, split) != split:
            raise ValueError("Aynı ses geliştirme/test ayrımına sızıyor.")
        hashes[digest] = split
        row.update(audio=str(audio), audioSha256=digest, durationSeconds=seconds)
    manifest["datasetProtocol"] = "participant-disjoint-v1"
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("x", encoding="utf-8") as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2, allow_nan=False)
    return manifest


def source_state():
    root = pathlib.Path(__file__).resolve().parents[2]
    def git(*args):
        return subprocess.run(["git", *args], cwd=root, capture_output=True, check=True).stdout
    try:
        revision = git("rev-parse", "HEAD").decode().strip()
        dirty = bool(git("status", "--porcelain"))
        paths = git("ls-files", "-z", "--cached", "--others", "--exclude-standard").decode().split("\0")
        digest = hashlib.sha256()
        for name in sorted(set(paths)):
            path = root / name
            if name.startswith(("src/", "tools/")) and path.suffix in (".cs", ".csproj", ".py", ".axaml"):
                digest.update(name.encode() + b"\0")
                digest.update(path.read_bytes() if path.is_file() else b"<deleted>")
        return {"gitRevision": revision, "dirty": dirty, "sourceSha256": digest.hexdigest()}
    except (OSError, subprocess.CalledProcessError):
        return {"gitRevision": None, "dirty": None, "sourceSha256": None}
