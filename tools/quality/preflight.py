"""Read-only asset check. File presence does not validate model compatibility."""
import pathlib
import platform
import shutil


def check(models, task, model="large-v3-turbo", quantization="q5_0"):
    if task not in ("transcribe", "diarize", "analyze"):
        raise ValueError("Bilinmeyen görev.")
    if model not in ("tiny", "base", "small", "medium", "large-v3", "large-v3-turbo"):
        raise ValueError("Bilinmeyen Whisper modeli.")
    if quantization not in ("q5_0", "q5_1", "q8_0"):
        raise ValueError("Bilinmeyen kuantizasyon.")
    files = (["sherpa-onnx-pyannote-segmentation-3-0/model.onnx", "speaker-embedding.onnx"]
             if task == "diarize" else [f"ggml-{model}-{quantization}.bin"])
    if task == "analyze":
        files += ["silero_vad.onnx", "prosody-msp-dim.onnx", "text-emotion-xlmr.onnx", "sentencepiece.bpe.model"]
    root = pathlib.Path(models).expanduser().resolve()
    assets = []
    for name in files:
        path = root / name
        size = path.stat().st_size if path.is_file() else 0
        assets.append({"file": name, "bytes": size,
                       "status": "present" if size else ("empty" if path.is_file() else "missing")})
    return {"task": task, "modelsDirectory": str(root), "models": assets,
            "modelsReady": all(a["status"] == "present" for a in assets),
            "compatibilityVerified": False, "dotnet": shutil.which("dotnet"),
            "machine": platform.machine(),
            "note": "Dosya varlığı uyumluluk veya doğruluk kanıtı değildir. Veri/referans ayrıca gereklidir."}
