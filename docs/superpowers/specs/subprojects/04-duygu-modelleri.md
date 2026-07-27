# Alt-Proje 04 — Duygu Modelleri (Metin + Prozodi)

> Bağımlılık: 01, 03 · İnşa sırası: **4** · Katman: `Infrastructure/Emotion`
> **Durum:** Brainstorm ile somutlaştırıldı (2026-07-25). İki bağımsız dilime bölündü:
> **04a Prozodi (önce)** → **04b Metin (sonra)**. Prozodi daha düşük risklidir (ham ses,
> tokenizer yok, ONNX'e hazır) ve füzyonun güçlü eksenini (arousal) hemen besler.

## Hedef
İki duygu modelini **ONNX Runtime (CoreML EP)** ile lokal çalıştırmak; her biri
**valence-arousal** üretir. Sidecar escape-hatch aynı arayüz ardında **kontrat olarak** hazır (D3/D4).

## Sözleşmeler
```csharp
public interface ITextEmotionModel   { Task<TextEmotion>    AnalyzeAsync(Transcript t, CancellationToken ct); }
public interface IProsodyEmotionModel { Task<ProsodyEmotion> AnalyzeAsync(AudioChunk c, CancellationToken ct); }
```

## Karar: Runtime yolu
- **Her iki model saf ONNX-.NET** (tek runtime, temiz paketleme). Prozodi ham ses → tokenizer yok.
  Metin SentencePiece gerektirir → **`Microsoft.ML.Tokenizers`** ile .NET'te (uyum plan içinde
  küçük bir doğrulama adımıyla güvenceye alınır).
- **Sidecar (Python/gRPC) implemente EDİLMEZ**, yalnızca kontratı tanımlanır (escape-hatch, D4).

## 04a — Prozodi modeli (önce)
- **Başlangıç adayı:** [`audeering/wav2vec2-large-robust-12-ft-emotion-msp-dim`](https://huggingface.co/audeering/wav2vec2-large-robust-12-ft-emotion-msp-dim)
  — arousal/dominance/**valence** (0..1), standart wav2vec2 mimarisi, **hazır ONNX export'u var** (Zenodo),
  24→12 katman budanmış (hafif). MSP-Podcast (İngilizce) eğitimli; **prozodi dil-agnostik (D7)**, Türkçe için değişmez.
  Ultra-hafif alternatif: **Wav2Small** (72K param) — 8GB'da çok rahat, eval'de kıyaslanır.
- **Çıktı eşlemesi:** audeering valence 0..1 → domain `−1..1` (`v*2−1`); arousal 0..1 → domain `0..1` (direkt).
- **Ek çıktı:** ses kalite sinyalleri (SNR, konuşma-oranı, süre) → füzyona (02 `QualitySignals`) besler.
  `modelConfidence` = çıktı varyansı / tutarlılık.
- **Girdi:** ham 16 kHz mono float (03'ün `AudioChunk`'ı ile aynı format).

## 04b — Metin modeli (sonra) — spike ile de-riske edildi (2026-07-25)
- **Model (başlangıç):** [`tabularisai/multilingual-emotion-classification`](https://huggingface.co/tabularisai/multilingual-emotion-classification)
  — XLM-R-base, Türkçe dahil 23 dil, **11 duygu**: anger, contempt, disgust, fear, frustration,
  gratitude, joy, love, neutral, sadness, surprise. Standart mimari → ONNX'e temiz çıkar. Kesin seçim → eval 09.
- **Neden ayrık + VA lookup (doğrudan VA-regresyon değil):** çok-dilli VA-regresyon araştırma
  aşamasında ve **Türkçe "en zorlu dil"** ([XLM-R VA çalışması](https://arxiv.org/pdf/2302.14021)); olgun
  Türkçe metin-VA modeli yok. Master §11'in kendi önerisiyle uyumlu.
- **Akış:** transkript → SentencePiece tokenize (.NET) → ONNX (`input_ids` + `attention_mask`) →
  `logits[1,11]` → softmax → 11-duygu dağılımı → **VA lookup** (olasılık-ağırlıklı ortalama) →
  `TextEmotion`. `modelConfidence` = dağılım **entropisi** (düşük entropi = yüksek güven).

### Tokenizer reçetesi (SPIKE İLE DOĞRULANDI — kritik risk kapandı)
`Microsoft.ML.Tokenizers` **2.0.0 (stable)** ile XLM-R tokenizasyonu **birebir** yapılabilir:
```csharp
var tok = SentencePieceTokenizer.Create(spmStream,
    addBeginningOfSentence: false, addEndOfSentence: false);
var raw = tok.EncodeToIds(text);                 // ham sentencepiece piece id'leri
var inputIds = new List<int>{ 0 };               // XLM-R <s>
foreach (var id in raw) inputIds.Add(id + 1);    // fairseq offset +1
inputIds.Add(2);                                 // XLM-R </s>
```
Bu reçete, 3 Türkçe metinde Python `transformers` `XLMRobertaTokenizer` çıktısıyla **birebir eşleşti**.
`sentencepiece.bpe.model` XLM-R-base'den alınır (~5 MB). `attention_mask` = hepsi 1 (tek örnek, padding yok).

### VA lookup tablosu (BAŞLANGIÇ — eval 09 kalibre eder, keyfi değil)
| Duygu | Valence | Arousal | | Duygu | Valence | Arousal |
|---|---|---|---|---|---|---|
| joy | +0.8 | 0.7 | | anger | −0.8 | 0.8 |
| love | +0.9 | 0.5 | | fear | −0.7 | 0.8 |
| gratitude | +0.7 | 0.4 | | sadness | −0.7 | 0.3 |
| surprise | +0.2 | 0.8 | | disgust | −0.6 | 0.5 |
| neutral | 0.0 | 0.5 | | frustration | −0.5 | 0.6 |
| | | | | contempt | −0.5 | 0.4 |

- **Dil:** Türkçe-first tune/valide; İngilizce `ITextEmotionModel`'in yeni implementasyonu (D5).
- **Önerilen mimari (04a'yı izler):** saf `XlmrTokenizerAdapter` + `TextVaLookup` + entropi-confidence
  (unit-testli) + `OnnxTextEmotionModel : ITextEmotionModel` (singleton motor, `IEmotionModelProvider`
  yeniden kullanılır) + `AddTextEmotion()` DI. ONNX girdisi `input_ids`/`attention_mask` (int64 tensör).

## ONNX export hattı
- PyTorch→ONNX + INT8 quantization Python scriptleri (repo'da, tekrar-üretilebilir). Prozodinin hazır
  Zenodo ONNX'i varsa export atlanabilir; metin için HF Optimum ile export.
- **build-time** Python/PyTorch gerekir (runtime DEĞİL); stajyerin venv'i mevcut.

## Kararlar / Uyarılar
- **ONNX-export edilebilirlik bir KABUL KRİTERİDİR** (stajyerin özel-mimari HuBERT tuzağından kaçış).
  Her iki başlangıç adayı standart mimari (Wav2Vec2ForSequenceClassification / XLMRobertaForSequenceClassification) → temiz export.
- Prozodi dil-agnostik; İngilizce için değişmez.
- Kalite > paketleme: bir model sidecar'da anlamlı derecede iyiyse orada kalır (kontrat hazır).
- **Kesin model isimleri + eşikler + VA lookup tablosu → Alt-Proje 09 (eval)** ile Türkçe veri
  üzerinde ölçülüp kilitlenir; bu spec başlangıç adaylarını sabitler, keyfi kalite iddiası vermez.

## Kabul Kriteri
- İki model de macOS'ta ONNX Runtime **CoreML EP** ile çalışır, VA çıktısı verir.
- Export edilen ONNX çıktısı PyTorch referansıyla **tolerans içinde eşleşir** (regression test).
- Sidecar fallback **kontratı** tanımlı ve en az bir modelle uçtan uca kanıtlanabilir (implementasyon ertelenmiş).
- **04a önce:** prozodi modeli tek başına `IProsodyEmotionModel` olarak çalışıp VA + kalite sinyali üretir.
