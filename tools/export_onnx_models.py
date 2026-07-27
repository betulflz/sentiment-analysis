"""SentimentAnalysis'in iki ONNX modelini PyTorch checkpoint'lerinden export eder.

    1. prosody-msp-dim.onnx   audeering/wav2vec2-large-robust-12-ft-emotion-msp-dim
                              girdi:  signal [1, N] float32  (zero-mean/unit-var standardize edilmis)
                              cikti:  logits [1, 3] = [arousal, dominance, valence], 0..1

    2. text-emotion-xlmr.onnx tabularisai/multilingual-emotion-classification
                              girdi:  input_ids [1, T] int64, attention_mask [1, T] int64
                              cikti:  logits [1, 11]

Bu adlar C# tarafinda SABIT olarak yaziliydir (OnnxProsodyEmotionModel.InputName /
OnnxTextEmotionModel.InputIdsName ...). Farkli adla export edilirse ONNX Runtime calisma
aninda "input not found" ile patlar; bu yuzden burada acikca veriliyor ve export sonrasi
dogrulaniyor.

audeering modeli standart `AutoModel` ile yuklenemez: `Wav2Vec2Model` govdesi + ozel bir
`RegressionHead` ile kaydedilmistir. Sinif burada birebir yeniden tanimlanir. (Ayni tuzak
stajyer projesindeki HuBERT SER checkpoint'inde de vardi - bkz. app/ml.py.)

Kullanim:
    python tools/export_onnx_models.py                 # ikisini de export et
    python tools/export_onnx_models.py --only prosody
    python tools/export_onnx_models.py --model-dir "C:/baska/yer"
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

# ---------------------------------------------------------------- sabitler

PROSODY_REPO = "audeering/wav2vec2-large-robust-12-ft-emotion-msp-dim"
TEXT_REPO = "tabularisai/multilingual-emotion-classification"
TURKISH_SER_REPO = "SeaBenSea/hubert-large-turkish-speech-emotion-recognition"

PROSODY_FILE = "prosody-msp-dim.onnx"
TEXT_FILE = "text-emotion-xlmr.onnx"
TURKISH_SER_FILE = "prosody-turkish-ser.onnx"

# C#'taki TurkishSerLabels.Ordered ile AYNI olmali (config.json id2label sirasi).
EXPECTED_SER_LABELS = ["angry", "calm", "happy", "sad"]

# C#'taki XlmrEmotionLabels.Ordered ile AYNI olmali. Export sirasinda modelin gercek
# config.json'i bununla karsilastirilir; ayrisirsa export durur (sessiz yanlis etiket
# eslesmesi, duygu ciktisini fark edilmeden bozar).
EXPECTED_TEXT_LABELS = [
    "anger", "contempt", "disgust", "fear", "frustration",
    "gratitude", "joy", "love", "neutral", "sadness", "surprise",
]

OPSET = 14


def default_model_dir() -> Path:
    """Uygulamanin aradigi klasor: Windows %APPDATA%, macOS ~/Library/Application Support."""
    if sys.platform == "win32":
        base = Path(os.environ.get("APPDATA", Path.home() / "AppData" / "Roaming"))
    elif sys.platform == "darwin":
        base = Path.home() / "Library" / "Application Support"
    else:
        base = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share"))
    return base / "SentimentAnalysis" / "models"


# ---------------------------------------------------------------- prozodi

def build_prosody_model():
    """audeering checkpoint'inin mimarisini kurar ve yalnizca `logits` donduren bir sarmalayici verir."""
    import torch
    from torch import nn
    from transformers import AutoConfig, Wav2Vec2Model, Wav2Vec2PreTrainedModel

    class RegressionHead(nn.Module):
        """Checkpoint'teki `classifier.dense` / `classifier.out_proj` basligi."""

        def __init__(self, config):
            super().__init__()
            self.dense = nn.Linear(config.hidden_size, config.hidden_size)
            self.dropout = nn.Dropout(config.final_dropout)
            self.out_proj = nn.Linear(config.hidden_size, config.num_labels)

        def forward(self, features):
            x = self.dropout(features)
            x = torch.tanh(self.dense(x))
            x = self.dropout(x)
            return self.out_proj(x)

    class EmotionModel(Wav2Vec2PreTrainedModel):
        def __init__(self, config):
            super().__init__(config)
            self.wav2vec2 = Wav2Vec2Model(config)
            self.classifier = RegressionHead(config)
            self.init_weights()

        def forward(self, signal):
            hidden = self.wav2vec2(signal)[0]
            pooled = torch.mean(hidden, dim=1)          # zaman ekseninde ortalama havuzlama
            return self.classifier(pooled)               # [B, 3] = arousal, dominance, valence

    config = AutoConfig.from_pretrained(PROSODY_REPO)
    model = EmotionModel.from_pretrained(PROSODY_REPO, config=config)
    model.eval()

    if config.num_labels != 3:
        raise SystemExit(
            f"HATA: prozodi modeli {config.num_labels} cikti veriyor, 3 bekleniyordu "
            "(arousal, dominance, valence). ProsodyOutputMapper bu sirayi varsayiyor."
        )
    return model


def export_prosody(out_path: Path) -> None:
    import torch

    print(f"[1/2] Prozodi modeli indiriliyor: {PROSODY_REPO}")
    model = build_prosody_model()

    # 2 saniyelik 16 kHz ornek girdi. Zaman ekseni dinamik isaretlendigi icin
    # calisma aninda herhangi bir uzunluk verilebilir.
    dummy = torch.randn(1, 32000, dtype=torch.float32)

    print(f"      ONNX'e export ediliyor (opset {OPSET})...")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    tmp = out_path.with_suffix(".onnx.partial")

    with torch.no_grad():
        torch.onnx.export(
            model,
            (dummy,),
            str(tmp),
            input_names=["signal"],
            output_names=["logits"],
            dynamic_axes={"signal": {0: "batch", 1: "samples"},
                          "logits": {0: "batch"}},
            opset_version=OPSET,
            do_constant_folding=True,
        )

    verify_onnx(tmp, expected_inputs=["signal"], expected_outputs=["logits"])
    tmp.replace(out_path)
    print(f"      tamam -> {out_path}  ({out_path.stat().st_size / 1e6:.0f} MB)")


# ---------------------------------------------------------------- metin

def export_text(out_path: Path) -> None:
    import torch
    from transformers import AutoConfig, AutoModelForSequenceClassification

    print(f"[2/2] Metin duygu modeli indiriliyor: {TEXT_REPO}")
    config = AutoConfig.from_pretrained(TEXT_REPO)

    # Etiket sirasi dogrulamasi - C# tarafi bu sirayi sabit varsayiyor.
    id2label = {int(k): v for k, v in config.id2label.items()}
    actual = [id2label[i].strip().lower() for i in sorted(id2label)]
    if actual != EXPECTED_TEXT_LABELS:
        raise SystemExit(
            "HATA: model etiket sirasi C#'taki XlmrEmotionLabels.Ordered ile uyusmuyor.\n"
            f"  modelde : {actual}\n"
            f"  beklenen: {EXPECTED_TEXT_LABELS}\n"
            "Bu sessizce yanlis duygu uretir. XlmrEmotionLabels.cs guncellenmeli "
            "(ve TextVaLookup.Table'da her etiketin karsiligi bulunmali)."
        )
    print(f"      etiket sirasi dogrulandi ({len(actual)} sinif)")

    model = AutoModelForSequenceClassification.from_pretrained(TEXT_REPO, config=config)
    model.eval()

    class LogitsOnly(torch.nn.Module):
        """HF modeli bir dataclass dondurur; ONNX duz tensor ister."""

        def __init__(self, inner):
            super().__init__()
            self.inner = inner

        def forward(self, input_ids, attention_mask):
            return self.inner(input_ids=input_ids, attention_mask=attention_mask).logits

    wrapper = LogitsOnly(model).eval()

    seq = 16
    dummy_ids = torch.ones(1, seq, dtype=torch.int64)
    dummy_mask = torch.ones(1, seq, dtype=torch.int64)

    print(f"      ONNX'e export ediliyor (opset {OPSET})...")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    tmp = out_path.with_suffix(".onnx.partial")

    with torch.no_grad():
        torch.onnx.export(
            wrapper,
            (dummy_ids, dummy_mask),
            str(tmp),
            input_names=["input_ids", "attention_mask"],
            output_names=["logits"],
            dynamic_axes={"input_ids": {0: "batch", 1: "sequence"},
                          "attention_mask": {0: "batch", 1: "sequence"},
                          "logits": {0: "batch"}},
            opset_version=OPSET,
            do_constant_folding=True,
        )

    verify_onnx(tmp, expected_inputs=["input_ids", "attention_mask"], expected_outputs=["logits"])
    tmp.replace(out_path)
    print(f"      tamam -> {out_path}  ({out_path.stat().st_size / 1e6:.0f} MB)")


# ---------------------------------------------------------------- turkce SER

def export_turkish_ser(out_path: Path) -> None:
    """SeaBenSea HuBERT Turkce SER (TurEV-DB) -> ONNX.

    Bu checkpoint de standart `AutoModelForAudioClassification` ile yuklenemez: ozel bir
    `HubertForSpeechClassification` mimarisiyle kaydedilmistir ve transformers'in
    `pipeline()` fonksiyonu egitilmis siniflandirma basligini SESSIZCE atip rastgele
    baslatir (cikti gurultu olur). Sinif burada checkpoint'e uyacak sekilde tanimlanir.
    """
    import torch
    from torch import nn
    from transformers import AutoConfig, HubertModel, HubertPreTrainedModel

    print(f"[3/3] Turkce SER modeli indiriliyor: {TURKISH_SER_REPO}")
    config = AutoConfig.from_pretrained(TURKISH_SER_REPO)

    id2label = {int(k): v for k, v in config.id2label.items()}
    actual = [id2label[i].strip().lower() for i in sorted(id2label)]
    if actual != EXPECTED_SER_LABELS:
        raise SystemExit(
            "HATA: Turkce SER etiket sirasi C#'taki TurkishSerLabels.Ordered ile uyusmuyor.\n"
            f"  modelde : {actual}\n"
            f"  beklenen: {EXPECTED_SER_LABELS}\n"
            "Bu sessizce yanlis duygu uretir (ornegin 'kizgin' yerine 'mutlu')."
        )
    print(f"      etiket sirasi dogrulandi ({len(actual)} sinif: {', '.join(actual)})")

    class SpeechClassifierHead(nn.Module):
        """Checkpoint'teki `classifier.dense` / `classifier.out_proj` basligi."""

        def __init__(self, cfg):
            super().__init__()
            self.dense = nn.Linear(cfg.hidden_size, cfg.hidden_size)
            self.dropout = nn.Dropout(cfg.final_dropout)
            self.out_proj = nn.Linear(cfg.hidden_size, cfg.num_labels)

        def forward(self, features):
            x = self.dropout(features)
            x = torch.tanh(self.dense(x))
            x = self.dropout(x)
            return self.out_proj(x)

    class HubertForSpeechClassification(HubertPreTrainedModel):
        def __init__(self, cfg):
            super().__init__(cfg)
            self.hubert = HubertModel(cfg)
            self.classifier = SpeechClassifierHead(cfg)
            self.post_init()

        def forward(self, signal):
            hidden = self.hubert(signal)[0]
            return self.classifier(torch.mean(hidden, dim=1))   # pooling_mode = mean

    model = HubertForSpeechClassification.from_pretrained(TURKISH_SER_REPO, config=config).eval()

    dummy = torch.randn(1, 32000, dtype=torch.float32)   # 2 sn @ 16 kHz

    print(f"      ONNX'e export ediliyor (opset {OPSET})...")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    tmp = out_path.with_suffix(".onnx.partial")

    with torch.no_grad():
        torch.onnx.export(
            model,
            (dummy,),
            str(tmp),
            # Ayni tensor adlari: C# tarafinda tek bir ONNX prozodi calistiricisi yeter.
            input_names=["signal"],
            output_names=["logits"],
            dynamic_axes={"signal": {0: "batch", 1: "samples"}, "logits": {0: "batch"}},
            opset_version=OPSET,
            do_constant_folding=True,
        )

    verify_onnx(tmp, expected_inputs=["signal"], expected_outputs=["logits"])
    tmp.replace(out_path)
    print(f"      tamam -> {out_path}  ({out_path.stat().st_size / 1e6:.0f} MB)")


# ---------------------------------------------------------------- dogrulama

def verify_onnx(path: Path, expected_inputs: list[str], expected_outputs: list[str]) -> None:
    """Grafi yukleyip tensor adlarini kontrol eder. Ad uyusmazligi C# tarafinda
    ancak calisma aninda patlardi; burada export aninda yakalanir."""
    try:
        import onnx
    except ImportError:
        print("      UYARI: `onnx` paketi yok, tensor adlari dogrulanamadi "
              "(pip install onnx ile dogrulama acilir).")
        return

    model = onnx.load(str(path))
    onnx.checker.check_model(model)

    names_in = [i.name for i in model.graph.input]
    names_out = [o.name for o in model.graph.output]

    missing_in = [n for n in expected_inputs if n not in names_in]
    missing_out = [n for n in expected_outputs if n not in names_out]
    if missing_in or missing_out:
        raise SystemExit(
            f"HATA: {path.name} beklenen tensor adlarini tasimiyor.\n"
            f"  eksik girdi : {missing_in} (grafikte: {names_in})\n"
            f"  eksik cikti : {missing_out} (grafikte: {names_out})"
        )
    print(f"      dogrulandi: girdi={names_in} cikti={names_out}")


# ---------------------------------------------------------------- main

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model-dir", type=Path, default=default_model_dir(),
                        help="hedef klasor (varsayilan: uygulamanin aradigi yer)")
    parser.add_argument("--only", choices=["prosody", "text", "turkish-ser"],
                        help="yalnizca birini export et")
    parser.add_argument("--force", action="store_true", help="var olan dosyalari yeniden uret")
    args = parser.parse_args()

    try:
        import torch  # noqa: F401
        import transformers  # noqa: F401
    except ImportError as exc:
        print(f"HATA: gerekli paket eksik ({exc.name}).", file=sys.stderr)
        print("Kurulum: pip install torch transformers onnx sentencepiece protobuf", file=sys.stderr)
        return 1

    model_dir: Path = args.model_dir
    model_dir.mkdir(parents=True, exist_ok=True)
    print(f"Hedef klasor: {model_dir}\n")

    jobs = []
    if args.only in (None, "prosody"):
        jobs.append((PROSODY_FILE, export_prosody))
    if args.only in (None, "text"):
        jobs.append((TEXT_FILE, export_text))
    if args.only in (None, "turkish-ser"):
        jobs.append((TURKISH_SER_FILE, export_turkish_ser))

    for filename, fn in jobs:
        dest = model_dir / filename
        if dest.exists() and not args.force:
            print(f"[ATLA] {filename} zaten var ({dest.stat().st_size / 1e6:.0f} MB). "
                  "Yeniden uretmek icin --force.\n")
            continue
        fn(dest)
        print()

    print("Bitti.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
