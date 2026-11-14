"""Metin duygu modelinin (text-emotion-xlmr.onnx) bellek dostu sürümünü üretir.

Yalnız kelime tablosu (Gather / embedding) int8'e sıkıştırılır; dikkat katmanları (MatMul) fp32 kalır.
Ölçüm (docs/research/2026-11-13-m1-bellek-olcumu.md): dosya 1,11 → 0,54 GB, tam ses hattı tepe bellek
4,75 → 3,42 GB; 60 Türkçe cümlede etiket kümesi fp32 ile 60/60 aynı. MatMul'ü de sıkıştırmak sonuçları
nötre kaydırdığı için BİLEREK yapılmaz.

Kullanım (uygulama bağımlılığı değildir; ayrı bir sanal ortamda):
    python3 -m venv .venv-quant && .venv-quant/bin/pip install onnxruntime onnx
    .venv-quant/bin/python tools/models/quantize_text_model.py <fp32-girdi.onnx> <cikti.onnx>
"""
import argparse
import os
import sys

from onnxruntime.quantization import QuantType, quantize_dynamic


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("source", help="fp32 ONNX (HF Optimum export'u)")
    p.add_argument("output", help="yazılacak int8 ONNX")
    a = p.parse_args()
    if os.path.abspath(a.source) == os.path.abspath(a.output):
        print("Girdi ve çıktı aynı dosya olamaz.", file=sys.stderr)
        return 2
    quantize_dynamic(a.source, a.output, weight_type=QuantType.QInt8,
                     op_types_to_quantize=["Gather"], per_channel=False)
    print(f"Yazıldı: {a.output} ({os.path.getsize(a.output) / 1e9:.2f} GB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
