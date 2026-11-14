# Devir notu ve yapılacaklar listesi — 13 Kasım 2026

> Güncel durum ve sıradaki işler: `docs/DEVAM-RAPORU.md` içindeki “Devralma doğrulaması” (14 Kasım 2026). Aşağıdaki liste tarihsel durumu içerir; İt.4 olay kuralları ve İt.5 geçmiş/ayar işleri sonradan uygulanmıştır.

Bu belge, Codex ile yürütülen kalite çalışmasının (açık kaynak araştırması → 5 iterasyonluk strateji → İterasyon 1) durumunu ve devralan kişinin yapacaklarını özetler. Branch: `11-karar-destek`.

## Önce bunları oku (sırayla)

1. `docs/research/2026-11-13-kalite-ve-acik-kaynak-arastirmasi.md` — koddaki somut sorunlar + incelenen açık kaynak projeler (pyannote Community-1, Nemotron/NeMo-Speech.cpp, WhisperX, Qwen3-ASR, emotion2vec+, EmoBox, ...).
2. `docs/research/2026-11-13-bes-iterasyon-strateji-taslagi.md` — 5 iterasyonluk strateji, M1 Air (8 GB) bellek politikası, çıkış koşulları.
3. `docs/superpowers/plans/2026-11-13-iterasyon-1-olcum.md` — İterasyon 1 uygulama planı ve doğrulama sonucu.
4. `docs/superpowers/plans/2026-11-13-m1-kalite-pilotu.md` ve `...-model-kaynak-yonetimi.md`.
5. `docs/research/2026-11-13-fleurs-turkce-on-olcum.md` — gerçek model koşusu (FLEURS, 6 kısa kayıt).
6. `tools/quality/README.md` ve `tools/quality/ITERATION-1.md` — ölçüm araçlarının kullanımı.

## Şu anki durum

Doğrulanan (13 Kasım 2026): `dotnet build` 0 hata/0 uyarı; `dotnet test SentimentAnalysis.sln` tüm projelerde 0 başarısız (bazı model/ortam testleri atlanır); `python3 -m unittest discover -s tools/quality` 27 test geçti.

Bu çalışmayla eklenenler:

- **Ölçüm altyapısı:** `tools/quality/` (Python: WER/CER/DER, manifest, `prepare`/`run`/`score`/`report`, preflight) ve `tools/SentimentAnalysis.Benchmark` (.NET CLI: `transcribe`, `diarize`, `analyze`).
- **Görünür hata / eksik kapsam:** `AnalysisDiagnostics`, `PipelineJournal`; dosya modunda kayıpsız (bounded Wait), canlıda DropOldest; rapor ekranında kalite notu.
- **Konuşmacı atama düzeltmesi:** `SpeakerAligner` artık kişi başına toplam örtüşmeyi kullanıyor, beraberlikte belirsiz işaretliyor.
- **Model ömrü yönetimi:** her analiz için model oturumu oluşturulur, bitince/hatada/durdurmada kapatılır; Sherpa native nesnesi deterministik dispose edilir; çıkışta sıralı temizlik. (Bellek düşüşü ölçülmedi, yalnız sahiplik/kapanış testli.)

Henüz **kanıtlanmış doğruluk kazancı yok**. Ölçüm hazır, ama gerçek Türkçe görüşme verisiyle başlangıç raporu üretilmedi.

## Yapılacaklar

### İterasyon 1'i bitirmek (önce bu)
- [ ] Tam üretim model setinin ve gerçek görüşme kayıtlarının konumunu belirle (modeller ve ses repoya girmez, `.gitignore`'da).
- [ ] 12 kısa Türkçe kayıtlık pilotu hazırla (20–30'a genişlet); referans metin/konuşmacı etiketlerini insan denetiminden geçir; geliştirme/test ayrımını dondur (`tools/quality prepare`).
- [ ] M1 Air'de mevcut motorlarla başlangıç hata/süre/bellek raporunu üret (`run` + `report`).
- [ ] Toplam yerleşik bellek (uygulama + model süreçleri) ölçümü; hedef ~3 GiB normal / 4 GiB tepe (başlangıç hedefi, sert sınır değil).
- [ ] Model kapatma değişikliğinin gerçek RSS/swap etkisini ölç.

### İterasyon 2 — doğru konuşmacı, metin, zaman
- [~] (2026-11-14: pencereli ayrım varsayılan, %71→%85; bkz. DEVAM-RAPORU.md) sherpa vs pyannote Community-1 vs (uygunsa) Nemotron/NeMo-Speech.cpp; en iyi iki hat ayrıntılı değerlendirme. Hedef: DER'de ≥%20 göreli azalma.
- [x] `GhostSpeakerFilter` kuralını yeniden değerlendir. (2026-11-14: kişi sayısı verildiyse ve küme sayısı bunu aşmıyorsa pay kuralı uygulanmaz — `b97c8c0`.)
- [x] Kelime hizalama denendi, **eklenmedi** (2026-11-14): uzun parça zaten orantılı bölünüyor (`8dc8a20`); kelime zamanı 2 referanslı kayıtta %98→%99 / %100→%100, DTW whisper.cpp'yi çökertti. Bkz. `docs/research/2026-11-14-kelime-zamani-deneyi.md`.
- [x] Whisper sessizlik halüsinasyonu ("Altyazı M.K.") süzgeci (`0b9cabb`, `bd0747e`).
- [x] Duraklama sonrası atlanan cevap: beam search 5 (`72805b8`; Şemsi WER %25,7 → %6,4).
- [ ] Whisper turbo vs daha küçük model (FLEURS ön ölçümünde küçük modeller belirgin kötüydü: WER base %39, small %27, turbo %8,7).

### İterasyon 3 — duygu
- [x] Metin ONNX modelinin etiket sırası/logitlerini referansla doğrula; `TextVaLookup` softmax ↔ modelin sigmoid tanımı. (2026-11-14: yerel model çok etiketli/sigmoid; sıra doğru → sigmoid'e geçildi.)
- [x] Prozodi güvenini (`ProsodyOutputMapper`) nötr uzaklığından ayır. (2026-11-14: sabit `ProsodyModelConfidence`=0,6; şiddetten/kazançtan bağımsız.)
- [ ] Kalibre güven politikası (etiketli Türkçe kayıtlarla; `ProsodyModelConfidence` ve eşikler).
- [ ] emotion2vec+ base ile karşılaştırma; ses/metin/birlikte deneyleri; "etiket üretme" seçeneği.
- [x] "Belirsiz" seçeneği (2026-11-14): güven Düşük → duygu adı yerine "Belirsiz"; kapsam kaybı raporda (`17e90e4`).
- [x] Lisans notu (2026-11-14): bkz. aşağıdaki "Lisans" notu. **Ürün kararı kullanıcıda.**

### İterasyon 4 — kanıta dayalı öneri
- [x] 3 açık olay + kaynak cümleli öneri (`248a70c`); aktarılan soru yanlış alarmı düzeltildi (`365162f`).
- [ ] Kör insan değerlendirmesi — form üreteci denendi (`quality-local/kor-degerlendirme/`), ama eldeki 3 sakin kayıtta TOPSIS 13/13 "Devam et", kurallar 0 olay: ayırt edici değil. Sorunlu anlar içeren kayıt gerekiyor. Sentetik 5 senaryoda kurallar 4/4 doğru (bkz. `docs/research/2026-11-14-sentetik-olay-senaryolari.md`).

### İterasyon 5 — günlük kullanım
- [x] Geçmiş ekranından eski raporu açma (`2886bd1`).
- [x] Ayarların uygulanması, saklama tercihi (`e2734f9`), kişi/metin düzeltme (`deaccb4`).
- [x] 30 dk test (2026-11-14, ses hattı, dosya modu): 31 dk → 964 sn, RTF 0,52, kapsam %100, ilk/son parça aynı hız, RSS 2,3 GB, tepe 3,6 GB. Bkz. `docs/research/2026-11-14-uzun-gorusme-testi.md`.
- [ ] Canlı mikrofonla 30 dk + tekrarlı başlat/durdur (elle, cihazda).
- [x] Görüntü kanalı: rol bilgisi varken yüz yalnız karşı tarafın duygusuna katılır (`b97c8c0`); kişi kartında yüz yalnız elle eşlemeyle.

## Notlar / tuzaklar

- **Lisans (ses duygu modeli):** audEERING `wav2vec2-large-robust-12-ft-emotion-msp-dim` CC-BY-NC-SA-4.0 lisanslıdır: **ticari kullanım yok**, türetilen model aynı lisansla paylaşılmalı, atıf zorunlu. Ders/araştırma projesi olarak kullanım uygundur; ürünleşecekse (İt.5) ses kanalı ticari izinli bir modelle değiştirilmeli (ör. emotion2vec+ — lisansı ayrıca doğrulanmalı) ya da audEERING'den ticari lisans alınmalı. Karar kullanıcınındır.

- `quality-local/` (model, ses, ham koşular; ~1,4 GB) **git dışındadır** ve bu bilgisayarda kaldı. FLEURS pilotunu yeniden üretmek için `docs/research/2026-11-13-fleurs-turkce-on-olcum.md` sonundaki kimlikler yeterli.
- Codex oturum kayıtları yalnız Ilkay'ın bilgisayarında (`~/.codex/sessions/2026/11/13/`); bu belgeler onların özetidir, ek bağlam gerekmez.
- Kod değişiklikleri doğruluk artışı olarak sunulmamalı; her iterasyonun çıkışı aynı kayıtlarda önce/sonra ölçümdür.
- Strateji hedefleri (%20 DER azalması, %80 öneri ilgililiği vb.) ölçülmüş sonuç değil, proje hedefleridir.

## Birleştirme notu (12. aşama ile)

Bu çalışma, Betül'ün `origin/11-karar-destek` üzerindeki 12. aşama commit'lerinin (Sesimi tanıt, SpeakerRoleTracker, rol çizelgesi) **üstüne rebase edildi**. Çakışmalar `EmotionPipeline` (hem `roles` hem `mode` parametresi), `PipelineAnalysisRunner` ve `CompositionRoot`'ta çözüldü. Ses imzası (TitaNet) ve VAD modelleri, "kökte yerel model olmaz" kuralını bozmamak için `RoleModels.cs` içinde tembel yüklenen özel bir alt sağlayıcıda tutulur (`SpeakerRoleTracker` buradan beslenir). Bunlar iş-başı model ömrüne henüz bağlı değil: ilk tanıtmada yüklenir, uygulama kapanınca kapanır.
- [ ] Gerçek cihazda "Sesimi tanıt" → canlı analiz akışını elle dene (birleştirme testlerle doğrulandı, modelle denenmedi).
