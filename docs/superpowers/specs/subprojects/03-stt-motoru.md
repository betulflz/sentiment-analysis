# Alt-Proje 03 — STT Motoru

> Bağımlılık: 01 · İnşa sırası: **3** · Katman: `Infrastructure/Stt`

## Hedef
Whisper large-v3-turbo ile lokal, donanım-hızlandırmalı konuşma→metin.

## Sözleşme
```csharp
public interface ISpeechToText {
    Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct);
}
```
`Transcript`: metin, `DetectedLanguage`, `AvgLogProb` (kalite sinyali), segment zamanları.

## Kapsam
- **Whisper.net** (whisper.cpp sarmalayıcı), **GGUF/GGML q5 large-v3-turbo** varsayılan.
- Donanım: macOS **Metal**, Windows CUDA/DirectML; runtime seçimi config'ten.
- Model açılışta bir kez yüklenir, sıcak tutulur (singleton).
- `AvgLogProb` düşükse (kalitesiz transkript) → füzyona sinyal.
- Opsiyonel "derin geçiş" için large-v3 q5 ikinci profil (toplantı-sonu, canlıyla
  eşzamanlı DEĞİL, D6).

## Kararlar
- 16 kHz mono float PCM standart girdi formatı.
- Dil algılama Whisper'a bırakılır (çok-dilli-hazır).
- **medium aday kümesinden çıkarıldı** — turbo onu RAM/hız/doğrulukta domine ediyor (aşağı bakınız).

## Donanım Fizibilite Ölçümü (2026-07-25, spike)

Proje sahibi "turbo bu makinede çalışmıyor" dediği için ölç-seç yaklaşımıyla (master §12.4)
gerçek hedef donanımda ölçüm yapıldı: **MacBook Air M1, 8 GB RAM, whisper.cpp 1.9.1 + Metal.**
Örnek: 24.83 sn sentetik Türkçe konuşma (16 kHz mono).

| Model | Dosya | Tepe RAM | İşlem süresi | RTF | Transkript (temiz ses) |
|---|---|---|---|---|---|
| **turbo q5** | 547 MB | **779 MB** | 4.21 sn | **0.17 (~6× hızlı)** | kusursuz |
| medium q5 | 514 MB | 1.08 GB | 4.88 sn | 0.20 (~5×) | kusursuz |
| large-v3 q5 | 1.0 GB | 1.91 GB | 8.05 sn | 0.32 (~3×) | kusursuz |

**Bulgular:**
- **"Turbo çalışmıyor" teşhisi:** Sorun stajyerin **PyTorch/transformers** yoluydu (birkaç GB'lık
  stack → 8 GB'da OOM). **whisper.cpp yolunda turbo yalnızca ~779 MB tepe RAM** kullanır ve
  gerçek-zamandan ~6× hızlıdır. **Master §D6'nın turbo kararı doğrudur;** değişen model değil,
  motor yoludur (zaten D3 = Whisper.net/whisper.cpp).
- **Derin geçiş (large-v3 q5)** 8 GB'da fizibildir (1.91 GB tepe, RTF 0.32) ama **canlıyla
  eşzamanlı çalıştırılmamalı** — yalnızca toplantı-sonu boşta.
- **Cold-start:** ilk çalıştırmada Metal kütüphanesi derlemesi ~9.8 sn (tek seferlik, cache'lenir)
  + model yükleme ~0.4 sn. Singleton yaşam döngüsüyle amortize olur.
- **Sınır:** Örnek sentetik/temiz olduğu için üç model de kusursuz transkript verdi; bu ölçüm
  **doğruluğu ayırt etmez** (turbo vs large-v3 farkı gürültülü/aksanlı/örtüşen gerçek seste
  belirir). Doğruluk kıyası ve quant seçimi (q5 vs q4) → **Alt-proje 09 (eval)**.

## Kabul Kriteri
- Türkçe örnek seste `base`'e göre belirgin daha iyi transkript (eval 09 ile ölçülür).
- macOS'ta Metal ile gerçek-zamanlıdan hızlı (chunk süresi < chunk uzunluğu) — **turbo q5 için
  ölçülü RTF 0.17, karşılanıyor.**
- Model yükleme bir kez; istek başına yükleme yok.
- Tepe RAM 8 GB donanımda güvenli sınırlarda — **turbo q5 için ~779 MB, karşılanıyor.**
