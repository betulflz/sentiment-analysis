# Alt-Proje 02 — Füzyon Motoru

> Bağımlılık: 01 · İnşa sırası: **2** · Katman: `Application` (saf C#)

## Hedef
İki modelin çıktısını **eksen-başına güvenilirlik-ağırlıklı** birleştiren, uyumsuzluğu
tespit edip anlamlandıran, ayrık duyguya haritalayan deterministik motor. Model yok,
sadece matematik → **%100 test edilebilir.**

## Sözleşme
```csharp
public interface IEmotionFusion {
    FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality);
}
```

## Kapsam / Algoritma (master §5)
- Eksen-başına ağırlık: `arousal ← ses baskın`, `valence ← metin baskın`.
- Ağırlıklar kalite sinyalleriyle dinamik modüle edilir (gürültü, süre, logprob).
- **Uyumsuzluk tespiti:** `|valence_text − valence_audio|` eşiği → `incongruent`
  bayrağı + `IncongruencePattern` (Maskeleme / Şaka / Olası-ironi).
- Uyumsuz vakada **valence prozodiye yaslanır**, iki sinyal de raporlanır (D11).
- İki model zıt kadran + ikisi de düşük güven → `belirsiz`.
- `congruence` skoru (0–1) üretilir.
- Toplantı-düzeyi agregasyon: baskın duygu, %süre, valence trendi, oynaklık,
  tepe anlar, ortalama congruence.

## Kararlar
- Başlangıç ağırlıkları ve eşikleri `AppConfig`'ten; **kalibrasyon alt-proje 09'da**
  eval seti üzerinde ölçülür (keyfi sabit değil).

## Kabul Kriteri
- Master §5.1 tablosundaki üç örüntü (maskeleme, şaka, ironi) birim testlerle doğrulanır.
- Belirsiz-dönme koşulu testli.
- Saf fonksiyon: aynı girdi → aynı çıktı; hiçbir I/O yok.
