# Sesli Duygu Analizi — Master Tasarım Dokümanı

> **Durum:** Onaylanmış tasarım (brainstorm çıktısı) · **Tarih:** 2026-07-24
> **Amaç:** Bu doküman, projenin bütününü başka bir geliştirici/agent'ın sıfırdan
> anlayıp inşa edebileceği şekilde anlatır. Kararların yalnızca *ne* olduğunu değil
> *neden* öyle olduğunu da içerir (human-first). Uygulama planı ayrı çıkacaktır;
> bu doküman **kod yazma** değil, **ortak anlayış** dokümanıdır.

---

## 1. Vizyon ve Kapsam

Ses kayıtlarından ve **canlı konuşmalardan** (özellikle Teams toplantıları) duygu
analizi yapan, **tamamen lokal** çalışan bir masaüstü agent'ı. Sistem iki bağımsız
modelin çıktısını ilkeli bir füzyon algoritmasıyla birleştirip nihai bir duygu
sonucu ve zengin metrikler üretir.

**İki model, iki bakış açısı:**

1. **Metin modeli (ne söylendi):** Konuşma → metin (STT) → metnin duygusu.
2. **Prozodi modeli (nasıl söylendi):** Ham ses dalgasının tonu, temposu, enerjisi
   → duygu. Kelimelere değil, sesin *müzikalitesine* bakar (kullanıcının Spotify
   benzetmesi tam olarak budur).

**Neden iki model?** Çünkü insan sözcükleri kontrol edebilir ama ses tonu gerçeği
sızdırır. Ağlamaklı bir sesle "çok mutluyum" veya gülerek "bugün çok üzgünüm"
denebilir. Tek modelli sistem bunu asla yakalayamaz; bizim sistemimizin **varlık
sebebi** bu uyumsuzluğu tespit edip anlamlandırmaktır.

### Bu proje neyi HEDEFLER
- Lokal, gizlilik-öncelikli çalışma (kişisel ses verisi makineden çıkmaz).
- Canlı Teams toplantısını dinleyip near-real-time duygu zaman çizelgesi üretmek.
- Ses dosyası yükleyip analiz etmek.
- Türkçe-öncelikli, İngilizce'ye mimari olarak hazır.
- Çok-metrikli, boyutsal (valence-arousal) çekirdekli zengin çıktı.
- Clean, katmanlı, test edilebilir .NET kodu ve ölçülebilir model kararları.

### Bu proje neyi HEDEFLEMEZ (YAGNI)
- Bulut/API tabanlı LLM kullanımı (gizlilik ihlali).
- Gerçek-zamanlı (kelime kelime) streaming — near-real-time chunk yeterli.
- V1'de konuşmacı-başına duygu (diarization) — mimari hazır, uygulama sonraya.
- V1'de İngilizce tam doğrulama — mimari hazır, tune/valide sonraya.
- Web arayüzü — bu bir masaüstü agent'tır.

---

## 2. Kararlar Kütüğü (rationale ile)

Bu tablo brainstorm'da verilen her büyük kararı ve *nedenini* özetler. Bir tasarım
kararını değiştirmek isteyen herkes önce buraya bakmalı.

| # | Karar | Seçim | Neden |
|---|---|---|---|
| D1 | Platform/form-factor | Cross-platform masaüstü (Avalonia + .NET), macOS-first pratikte; ileride Windows service | Ekip macOS ağırlıklı; tek kod tabanı; Teams sesi lokal yakalanır |
| D2 | Zamansal mod | Chunked near-real-time (10–30 sn pencere) + toplantı-sonu özet | Lokal donanımda gerçekçi; streaming STT karmaşasından kaçınır; yine "canlı" hissi |
| D3 | Model barındırma | Hibrit: **Whisper.net** (STT) + **ONNX Runtime** (duygu), Python sidecar açık escape-hatch | Tek runtime, temiz paketleme, macOS Metal/CoreML; egzotik model gerekirse sidecar |
| D4 | Başlangıç duruşu | ONNX-first; bir model export edilemezse sadece o sidecar'a düşer | Kalite > paketleme kolaylığı; ama mimari değişmez |
| D5 | Dil stratejisi | Türkçe-first tune/valide + çok-dilli-hazır mimari | İngilizce ileride gelecek; STT/SER zaten dil-agnostik, borç minimal |
| D6 | STT modeli | **Whisper large-v3-turbo** (GGUF q5, varsayılan); large-v3 "derin geçiş" için opsiyonel | Yüksek doğruluk/hız dengesi; stajyerin `base`'i yetersizdi |
| D7 | Duygu temsili | **Boyutsal çekirdek (valence-arousal)** + ayrık haritalama | SER'in etiket kısıtını aşar; Spotify-tarzı; iki modeli tamamlayıcı yapar |
| D8 | Ayrık duygu çarkı | **8 duygu circumplex**: Coşkulu, Mutlu, Sakin, Sıkılmış, Üzgün, Gergin, Kızgın, Nötr | VA uzayını dengeli kaplar; toplantı için anlamlı; ne az ne fazla |
| D9 | Konuşmacı ayrımı | ~~V1 toplam-akış (oda), mimari konuşmacı-hazır~~ → **REVİZE 2026-07-27:** konuşmacı ayrımı kapsama alındı (alt-proje 10) | Gerekçe değişti: sherpa-onnx C#'tan diarization sunuyor, "çoğu Python" artık geçerli değil. **Dürüst not:** eski satırdaki "mimari konuşmacı-hazır (`SpeakerId` alanı)" iddiası kodda hiç karşılığı olmayan bir varsayımdı — `SpeakerId` yazılmamıştı. Bkz. spec 10 |
| D10 | Füzyon | Eksen-başına güvenilirlik-ağırlıklı: arousal←ses, valence←metin; kaliteyle dinamik | Arousal sesten güvenilir, valence metinden güvenilir; naif ortalamadan sağlam |
| D11 | Uyumsuzluk | Prozodi-baskın + iki sinyali de göster + congruence skoru | Affect leakage: ses gerçeği söyler; ama dürüstçe her iki sinyali sun |
| D12 | macOS capture | **ScreenCaptureKit** + küçük native Swift yardımcı süreç | Sıfır-kurulum, Teams sürecine kapsanabilir, profesyonel |
| D13 | Depolama | Katmanlı: metrik+transkript kalıcı; ses Opus+şifreli, 7 gün sonra oto-sil | Yeniden-analize izin ver, sonra gizlilik için temizle; disk dostu |
| D14 | UI | Avalonia (tepsi + minimal pencere) | Cross-platform, tek kod tabanı |
| D15 | Kalite kanıtı | Ayrı `eval` projesi (Türkçe veri, doğruluk + kapsam) | Keyfi değil ölçülü model/füzyon kararları (stajyer yaklaşımını koru) |

---

## 3. Duygu Temsili: Valence-Arousal Çekirdeği

Sistemin **iç para birimi** ayrık etiketler değil, iki sürekli eksendir:

- **Valence** (−1 … +1): olumsuz ↔ olumlu. "Kötü mü iyi mi hissediliyor?"
- **Arousal** (0 … 1): sakin ↔ uyarılmış/enerjik. "Ne kadar yoğun?"

Ayrık 8 duygu, bu 2B uzaydan **en sonda** haritalanır (circumplex modeli):

```
                    Arousal ↑ (yüksek enerji)
                         |
     Gergin/Stresli      |      Coşkulu
        (V− A+)          |       (V+ A+)
                         |
   Kızgın (V−− A+)       |
 ── ─────────────────────●───────────────────── →  Valence
   Valence −             |             Valence +
                         |
       Üzgün (V− A↓)     |      Mutlu (V+ orta-A)
                         |
      Sıkılmış (V− A−)   |    Sakin/Memnun (V+ A−)
                         |
                    Arousal ↓ (düşük enerji)

                (merkez ≈ Nötr)
```

**Neden bu tasarım kritik?** İki eksen, iki modeli **rakip değil tamamlayıcı** yapar:

| Eksen | Güvenilir kaynak | Neden |
|---|---|---|
| **Arousal** | **Ses (prozodi)** | Bağırma, tempo, ses tizliği; dilden bağımsız, güvenilir |
| **Valence** | **Metin** | Kelimelerin anlamı; sesten valence zayıf ölçülür |

Bu, füzyonun temelidir (Bölüm 5).

---

## 4. İki Model

### 4.1 Metin modeli (ne söylendi)
- **Girdi:** STT'den gelen transkript + algılanan dil.
- **Çıktı:** `valence_text`, `arousal_text`, ayrık duygu dağılımı (ham),
  `modelConfidence` (entropi/varyans), transkript kalite sinyali.
- **Model tipi:** Çok-dilli metin duygu/sentiment modeli (ONNX-export edilebilir
  standart mimari — ör. XLM-R tabanlı). Ayrık çıktı VA'ya lookup ile haritalanır,
  ya da doğrudan VA-regresyon veren bir model tercih edilir.
- **Dil:** Şimdi Türkçe tune/valide; İngilizce `ITextEmotionModel`'in yeni bir
  implementasyonu olarak gelir.

### 4.2 Prozodi modeli (nasıl söylendi)
- **Girdi:** Ham 16 kHz mono ses dalgası (chunk).
- **Çıktı:** `valence_audio`, `arousal_audio` (**doğrudan VAD-regresyon**),
  `modelConfidence`, ses kalite sinyalleri (SNR/enerji, konuşma-oranı, süre).
- **Model tipi:** wav2vec2/HuBERT tabanlı **boyutsal (dimensional) SER** — ayrık
  etiket değil, valence-arousal-(dominance) regresyonu veren standart mimari
  (ONNX uyumlu). Bu, stajyerin özel-mimari HuBERT tuzağından **bilinçli kaçıştır**.
- **Not:** Prozodi dil-agnostiktir; İngilizce için değişmez.

> **Uyarı (stajyer README'sinden ders):** Standart olmayan bir sınıfla kaydedilmiş
> SER checkpoint'leri `pipeline()`/naif yükleyicilerle sessizce bozulur (sınıflandırma
> başlığı atılır). **Model seçiminde ONNX-export edilebilirlik bir kabul kriteridir.**

---

## 5. Füzyon Algoritması (saf C#, %100 test edilebilir)

Füzyon bir model değil, **matematiktir** — hiçbir runtime bağımlılığı yok, tam test
edilebilir. Naif "iki dağılımı ağırlıkla topla" yerine **eksen-başına güvenilirlik-
ağırlıklı** yaklaşım:

```
arousal_final = w_a_audio · arousal_audio + w_a_text · arousal_text    (w_a_audio yüksek)
valence_final = w_v_text  · valence_text  + w_v_audio · valence_audio   (w_v_text  yüksek)
```

Ağırlıklar **sabit değil**, girdi kalitesiyle dinamik modüle edilir:
- Ses gürültülü / çok kısa / çoğu sessiz → ses ağırlığı düşer.
- Transkript boş / çok kısa / düşük Whisper avg-logprob → metin ağırlığı düşer.

### 5.1 Uyumsuzluk (incongruence) işleme — sistemin kalbi

Her chunk'ta `valence_text` ile `valence_audio` **ıraksaması** ölçülür.

| Durum | Davranış |
|---|---|
| **Uyumlu** (agree) | Güvenle birleştir; valence metin-ağırlıklı; `confidence: yüksek` |
| **Uyumsuz** (diverge) | `incongruent = true` + örüntü sınıfı; **valence prozodiye yaslanır** (affect leakage); ama **iki sinyal de** raporlanır |
| **İki model zıt kadran + ikisi de düşük güven** | Nihai sonuç `belirsiz` (uydurmaktan dürüsttür) |

**Uyumsuzluk örüntüleri:**

| Girdi örneği | Metin val. | Ses val. | Ses arousal | Yorum | Nihai |
|---|---|---|---|---|---|
| Ağlamaklı "çok mutluyum" | + | − | orta-yüksek | **Maskeleme** | Olumsuz (prozodi), bayraklı |
| Gülerek "çok üzgünüm" | − | + | yüksek | **Şaka/hafiflik** | Olumsuz değil (prozodi), bayraklı |
| Düz tonla "harika, bayıldım" | + | ~0 nötr | düşük | **Olası ironi/sarkazm** | Şüpheli, bayraklı |

**Yeni üst-metrik:** `congruence` (0–1) = söylenen ile hissedilenin örtüşmesi.
Toplantı analizinde altın değerinde ("herkes 'olur' dedi ama ton isteksizdi").

### 5.2 Ayrık haritalama
`valence_final, arousal_final` → 8-duygu çarkında en yakın bölge → `primaryEmotion`;
ikinci en yakın → `secondaryEmotion` (nüans için).

---

## 6. Çıktı Metrikleri

### Chunk düzeyi
- `valence`, `arousal` (birleşik), `primaryEmotion`, `secondaryEmotion`
- `confidence` (yüksek/orta/düşük), `agreement`, `incongruent` (+ örüntü), `congruence`
- `timeRange`, kaynak sinyaller (metin ve ses ham VA + güven)

### Toplantı düzeyi (bitişte agregasyon)
- Baskın duygu, her duyguda geçirilen % süre
- **Valence trendi** (ısındı/soğudu), **duygusal oynaklık** (volatilite)
- **Tepe anlar** (en yüksek arousal / en düşük valence chunk'ları = "kritik anlar")
- Ortalama VA + doğal-dilde Türkçe özet cümlesi
- Ortalama congruence (toplantıda ne kadar "samimi/örtüşük" konuşuldu)

---

## 7. Canlı Ses Yakalama

`IAudioCaptureSource` soyutlaması, platform-başına implementasyon:

| Platform | Yol | Kurulum |
|---|---|---|
| **Windows** | WASAPI loopback (NAudio) | Ekstra yok |
| **macOS** | **ScreenCaptureKit** + küçük native Swift yardımcı süreç → PCM'i .NET'e akıtır | Ekstra yok; sadece ekran-kaydı izni |

**Avantaj:** ScreenCaptureKit/process-tap ile **yalnızca Teams sürecinin** sesi
yakalanabilir → arka plandaki müzik vb. karışmaz (doğruluk kazanımı).

**Rıza/etik ilkesi (zorunlu):** Kayıt/dinleme aktifken **görünür gösterge** ve
toplantı öncesi onay akışı. Kayıt yasal olarak rıza gerektirir; ürün bunu
görünür kılar.

---

## 8. İşleme Hattı ve Optimizasyon

Backpressure'lı, **bounded** producer/consumer hattı (`System.Threading.Channels`):

```
[Capture] → ring buffer → [Chunker 10–30sn] → [VAD kapısı] → ┬→ [STT turbo] → [Metin duygu ONNX]
                                                             └→ [Prozodi VAD-reg. ONNX]
                                                                        ↓
                                                          [Füzyon (saf C#)] → [Zaman çizelgesi + kalıcılık]
```

**Kilit optimizasyonlar:**
- **VAD kapısı (Silero VAD, ~KB ONNX):** sessiz chunk'ları STT'ye hiç sokma —
  en yüksek getirili tek optimizasyon (hem CPU hem kalite).
- **Model yaşam döngüsü:** açılışta bir kez yükle, sıcak tut (singleton).
- **Quantization:** Whisper GGUF q5; duygu modelleri INT8 ONNX.
- **Donanım:** macOS → whisper.cpp **Metal** + ONNX **CoreML EP**; Windows →
  CUDA/DirectML.
- **Bellek disiplini:** PCM tamponları `ArrayPool<float>`; GC baskısı düşer.
- **Degradasyon:** hat geri kalırsa turbo'da kal + chunk birleştir; "derin
  large-v3 geçişi" toplantı bittikten sonra boşta yapılır.

---

## 9. .NET Çözüm Mimarisi

Clean, katmanlı; bağımlılıklar hep **içe** doğru; `Core` hiçbir platform/kütüphaneye
bağlı değil.

```
SentimentAnalysis.sln
├─ Core            (domain: Emotion, ValenceArousal, ChunkResult, MeetingSession; saf C#, sıfır bağımlılık)
├─ Application     (arayüzler: ISpeechToText, ITextEmotionModel, IProsodyEmotionModel,
│                   IEmotionFusion, IAudioCaptureSource, ISessionStore, IVadGate; orkestrasyon/pipeline)
├─ Infrastructure
│   ├─ Stt         (WhisperNetEngine)
│   ├─ Emotion     (Onnx* implementasyonları; PySidecar* escape-hatch)
│   ├─ Audio       (WindowsLoopbackCapture, MacScreenCaptureKitCapture + native yardımcı)
│   ├─ Vad         (SileroVadGate)
│   └─ Storage     (SqliteSessionStore, Opus + şifreleme)
├─ Desktop.Avalonia (tepsi UI, canlı gösterge, rıza akışı, toplantı geçmişi/rapor)
└─ Tests           (Core/Application %100 birim; Infrastructure entegrasyon; eval harness)
```

**Temel arayüzler (sözleşmeler):**

```csharp
public interface ISpeechToText {
    Task<Transcript> TranscribeAsync(AudioChunk chunk, CancellationToken ct);
}
public interface ITextEmotionModel {
    Task<TextEmotion> AnalyzeAsync(Transcript transcript, CancellationToken ct);
}
public interface IProsodyEmotionModel {
    Task<ProsodyEmotion> AnalyzeAsync(AudioChunk chunk, CancellationToken ct);
}
public interface IEmotionFusion {                       // saf, senkron, deterministik
    FusedResult Fuse(TextEmotion text, ProsodyEmotion prosody, QualitySignals quality);
}
public interface IAudioCaptureSource {
    IAsyncEnumerable<AudioFrame> CaptureAsync(CaptureOptions options, CancellationToken ct);
}
public interface IVadGate { bool HasSpeech(AudioChunk chunk); }
public interface ISessionStore { /* kaydet, sorgula, saklama/temizlik */ }
```

---

## 10. Alt-Proje Ayrışımı ve İnşa Sırası

Her alt-proje kendi spec taşlağına sahiptir (`subprojects/`).

| # | Alt-proje | Bağımlı olduğu |
|---|---|---|
| 1 | Temel + Domain | — |
| 2 | Füzyon motoru | 1 |
| 3 | STT motoru | 1 |
| 4 | Duygu modelleri | 1, (girdi için) 3 |
| 5 | Ses yakalama + VAD | 1 |
| 6 | Pipeline orkestrasyonu | 1–5 |
| 7 | Depolama + gizlilik | 1 |
| 8 | Avalonia UI | 1, 6, 7 |
| 9 | Değerlendirme (eval) | 2, 3, 4 (paralel) |
| 10 | Konuşmacı ayrımı (diarization) — 10a spike / 10b etiketli transkript / 10c konuşmacı-başına duygu | 1–8 (D9 revizyonu; bkz. `subprojects/10-konusmaci-ayrimi.md`) |

**Önerilen ilk dikey dilim:** 1 → 2 → 3 → 4 (saf-C# çekirdek + füzyon önce; paketleme
acısı yok, test yoğun). Ses yakalama (5) ve UI (8) sonraki dalga.

---

## 11. Riskler ve Açık Sorular

| Risk / soru | Etki | Azaltma |
|---|---|---|
| ~~Türkçe boyutsal (VA) SER modeli bulunamazsa~~ **(ELE ALINDI 2026-07-25)** | Prozodi kalitesi | Araştırıldı: Türkçe'ye özel VA-SER yok → dil-agnostik `audeering/wav2vec2-...-msp-dim` (ONNX-hazır) kullanılır; prozodi dil-agnostik (D7). Bkz. spec 04a |
| ~~Metin VA-regresyon modeli Türkçe zayıfsa~~ **(ELE ALINDI 2026-07-25)** | Valence kalitesi | Karar: doğrudan VA-regresyon değil, çok-dilli **ayrık duygu + VA lookup** (Türkçe VA-regresyon olgun değil). Bkz. spec 04b; eval 09 ile ölçülür |
| ScreenCaptureKit native interop karmaşası | macOS capture | İlk BlackHole ile doğrula (aynı arayüz), sonra native'e geç |
| Teams istemcisi (native vs tarayıcı) ses yolu | Yakalama kapsamı | Process kapsamı test edilmeli; gerekirse tüm-sistem fallback |
| Chunk sınırında kesilen kelime/duygu | Doğruluk | Örtüşmeli (overlapping) pencere; cümle sınırına hizalama |
| ~~large-v3-turbo 8GB'da çalışmaz~~ **(KAPANDI 2026-07-25)** | STT fizibilite | Ölçüldü: whisper.cpp/Metal ile turbo q5 M1 8GB'da ~779 MB tepe RAM, RTF 0.17. "Çalışmıyor" deneyimi PyTorch yoluydu. Bkz. spec 03 fizibilite ölçümü |
| large-v3-turbo Türkçe doğruluğu yetersiz kalırsa | STT kalite | eval ile ölç; Türkçe fine-tuned aday veya large-v3 (q5 deep-pass 8GB'da fizibil: 1.91 GB tepe) |
| Şifreleme anahtarı yönetimi (lokal) | Gizlilik | OS keychain/DPAPI ile anahtar; düz metin anahtar yok |
| **(2026-07-27)** Diarization tek/uzak-alan mikrofonda zayıf kalabilir; sherpa-onnx native'i mevcut ONNX Runtime + Whisper.net ile çakışabilir | Alt-proje 10 fizibilitesi | Kod öncesi 10a spike'ı gerçek ortamda ÖLÇER; çakışma varsa saf-ONNX/sidecar escape-hatch (D3/D4). Bkz. spec 10 |

---

## 12. İlkeler (bu proje boyunca geçerli)

1. **Kalite > paketleme kolaylığı** — bir model gerçekten sidecar'da çok daha iyiyse,
   sidecar'da kalır (mimari buna izin verir).
2. **Dürüstlük > kesinlik gösterisi** — belirsizse `belirsiz` de.
3. **Uyumsuzluk gürültü değil, birinci sınıf sinyaldir.**
4. **Model kararları ölçülür, keyfi verilmez** (eval projesi).
5. **Gizlilik varsayılan** — veri lokal, ses geçici+şifreli, kayıt görünür.
6. **Soyutlama arkasında değiştirilebilirlik** — her model/aygıt bir arayüz ardında.
