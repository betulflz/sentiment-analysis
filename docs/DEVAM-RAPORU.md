# Devam raporu — 14 Kasım 2026

Bu belge, "Canlı Görüşme KDS" projesinde kalite iterasyonlarının **nerede kaldığını** ve **nereden devam edileceğini** anlatır. Yeni oturum ya da başka bir yapay zeka, önce bu dosyayı, sonra `docs/DEVIR-TESLIM.md` dosyasını okumalı.

## Proje ve kurallar

- **Proje:** C#/.NET 9 + Avalonia masaüstü uygulaması. Görüşmeyi (canlı mikrofon ya da dosya) dinler, konuşmacıları ayırır, metne çevirir (Whisper), duygu analizi yapar (metin + ses), rapor ve öneri üretir.
- **Hedef cihaz:** MacBook Air M1, 8 GB. Bellek dar olduğu için modeller aynı anda yüklenmez (`ModelWorkQueue`, `OwnedModelSession`).
- **Dal:** `11-karar-destek`. Repo: github.com/betulflz/sentiment-analysis (public).
- **Kullanıcı Türkçe çalışır.** Yanıtlar sade Türkçe ve kısa olmalı. Kullanıcı hızlı ilerlemek istiyor.
- **Push yalnızca kullanıcının açık onayıyla yapılır.** Bir commit için verilen onay, sonraki commit'leri kapsamaz.
- **Çalışma yöntemi:** her değişiklik TDD ile yapılır: önce kırmızı test, sonra yeşil. Commit öncesi tüm testler çalıştırılır (`dotnet test`, 12 proje; bazı model testleri atlanır). Commit mesajları Türkçedir.
- **Modeller ve kayıtlar git dışında durur.**
  - Modeller: `~/Library/Application Support/SentimentAnalysis/models`.
  - Kayıtlar ve etiketler: `quality-local/`.
- **Uygulamayı derleyip açmak:** önce eski uygulamayı kapatın, sonra `./build-macos-app.sh` çalıştırın.

## Yol haritası (5 iterasyon)

Ayrıntılar `docs/research/2026-11-13-bes-iterasyon-strateji-taslagi.md` dosyasında.

1. **Ölçüm:** gerçek Türkçe kayıtlarda WER/DER başlangıç değerleri.
2. **Konuşmacı, metin ve zaman doğruluğu.**
3. **Daha güvenilir duygu.**
4. **Kanıta dayalı öneri.**
5. **Günlük kullanım (ürünleştirme).**

## Durum

| İterasyon | Durum |
|---|---|
| 1 — Ölçüm | Araçlar hazır (`tools/quality`, `tools/SentimentAnalysis.Benchmark`). **Kullanıcının kendi kayıtları ve etiketleri bekleniyor.** |
| 2 — Konuşmacı/metin | Pencereli konuşmacı ayrımı bitti ve push edildi. Örtüşen konuşma ve kelime hizalama kaldı. |
| 3 — Duygu | **Devam ediyor.** Veri gerektirmeyen işler bitti (sigmoid, ses güveni, güvenlik bulguları, "belirsiz", lisans notu). Kalan her şey etiketli kayıt bekliyor. |
| 4 — Öneri | Başlamadı. |
| 5 — Ürün | Başlamadı. |

### Son commit'ler

| Commit | Ne yaptı | Push |
|---|---|---|
| `25dd0af` | Pencereli konuşmacı ayrımı varsayılan oldu. Etiketli 5 kayıtta doğru konuşmacı süresi %71'den %85'e çıktı ve işlem çok hızlandı. | Evet |
| `155312a` | Metin duygu modeli artık sigmoid ile okunuyor (softmax yerine); güven, en olası duygunun olasılığı. | **Hayır** (onay bekliyor) |
| `20644b6` | Ses (prozodi) güveni şiddetten ayrıldı: sabit `ProsodyModelConfidence = 0,6`. | **Hayır** (onay bekliyor) |
| `7dd4af3` | Güvenlik 1a+1b: NaN/∞ logit reddi, sonlu olmayan güven → Düşük; τ=0,5 altı metin güveni 0. | **Hayır** |
| `bf1c824` | Güvenlik 1c+1d: pencere/adım doğrulaması (adım 0 sonsuz döngüsü), kümelemede iptal + `counts[]`. | **Hayır** |
| `17e90e4` | "Belirsiz": güven Düşük → duygu adı yerine Belirsiz; belirsiz pay raporda, duygu paylarına girmez. | **Hayır** |

### İterasyon 2: yapılan iş

`WindowedDiarizer` + `SpeakerClustering` (`src/SentimentAnalysis.Application/Diarization/`) şöyle çalışır:

1. Ses 1,5 sn pencerelere bölünür, pencereler 0,5 sn adımla kayar.
2. Her adım dilimine VAD (konuşma var mı) uygulanır.
3. Konuşma olan pencereler için TitaNet ses imzası çıkarılır.
4. İmzalar çok başlangıçlı k-means ile kümelenir (40 deneme, %10'dan küçük kümeye ceza).
5. Komşu 3 pencereyle çoğunluk oylaması yapılır.
6. Konuşmacı sayısı verilmemişse kendisi bulunur: küme merkezleri arasındaki kosinüs benzerliği 0,55'in altında kalan en büyük k seçilir.

- **Ayarlar:** `AppConfig.Diarization` içindeki `UseWindowedDiarizer`, `WindowSeconds`, `HopSeconds`, `SmoothWindows`, `SameSpeakerCosine`, `MaxAutoSpeakers`.
- **Ölçüm:** `docs/research/2026-11-13-pencereli-konusmaci-ayrimi.md`.
- **Kullanıcının canlı deneme geri bildirimi:** "ses ayırma hâlâ yetersiz". Yeni sürüm denendi mi, henüz bilinmiyor.
- **Bilinen sınır:** 0,55 eşiği 3 ve daha fazla konuşmacıyla doğrulanmadı.

### İterasyon 3: yapılan iş

1. **Metin modeli** (`TextVaLookup`):
   - tabularisai/multilingual-emotion-classification çok etiketli bir model (`problem_type = multi_label_classification`, bağımsız sigmoid, eşik τ = 0,5).
   - Yerel ONNX'in ham logitleri incelenerek doğrulandı: etkin olmayan duyguların logitleri -5 ile -8 arasında. Etiket sırası alfabetik ve doğru.
   - Doğrulama betiği: `scratchpad/text-logits/` (git dışında).
   - Değerlendirme kuralı: VA = sigmoid ağırlıklı ortalama; güven = en yüksek sigmoid olasılığı.
2. **Ses modeli** (`ProsodyOutputMapper`):
   - Güven önceden "nötr merkeze uzaklık" ile hesaplanıyordu. Bu aslında şiddet: nötr ses hep düşük güven alıyordu ve kazanç artınca güven de şişiyordu.
   - Artık güven ayarlanabilir sabit bir değer. 0,6 kalibre edilmemiş bir başlangıç değeri (msp-dim için yayınlanan CCC'ler: valence yaklaşık 0,64, arousal yaklaşık 0,75).

## Sıradaki işler (öncelik sırasıyla)

1. ~~**Otomatik güvenlik incelemesinin bulguları.**~~ **Bitti** (`7dd4af3`, `bf1c824`). 1b kararı: τ altı → güven 0; τ üstü olasılık aynen (yeniden ölçekleme seçilmedi, "güven = olasılık" korunsun diye). Hepsi orta önemde; her biri için önce test, sonra düzeltme yapılacak:
   - a) `TextVaLookup.Map`: sonlu olmayan (NaN/∞) logit gelirse `ArgumentException` fırlatılmalı. `EmotionFusion.ResolveConfidence` da sonlu olmayan güvende `Dusuk` döndürmeli.
   - b) `TextVaLookup`: hiçbir duygu τ = 0,5'i geçmiyorsa sonuç ne olmalı? Füzyonun düşük güven eşiği 0,35; bu yüzden en yüksek olasılığı 0,35–0,5 arasında kalan "eşik altı" bir sonuç "Orta" güven alabiliyor. Öneri: τ altını düşük güvene eşleyin. Seçenekler: güven = 0, ya da `(top − 0,5) / 0,5` ile yeniden ölçekleme. Uçtan uca test: tüm logitler negatifken füzyon sonucu `Confidence.Dusuk` olmalı.
   - c) `WindowedDiarizer`: `WindowSeconds` ≤ 0, `HopSeconds` ≤ 0 ya da `HopSeconds > WindowSeconds` ise kurucu `ArgumentOutOfRangeException` fırlatmalı. Şu an hop = 0 sonsuz döngüye girer.
   - d) `SpeakerClustering`: `CancellationToken` her denemede ve her k-means turunda kontrol edilmeli. Turlar içindeki `lab.Contains` / `lab.Count` LINQ taramaları, atama turunda doldurulan bir `counts[]` dizisiyle değiştirilmeli.
2. ~~**İt.3 — "belirsiz" seçeneği.**~~ **Bitti** (`17e90e4`). Kural: `FusedResult.Uncertain` = `Confidence.Dusuk`. Ses güveni sabit 0,6 olduğu için pratikte "metin modeli hiçbir duyguda τ'yu geçmedi" ya da "girdi kalitesi düşük" demek. Gerçek kayıtta kapsam kaybının oranı **ölçülmedi**. Model emin değilse yanlış duygu adı yerine "belirsiz" gösterilir. Kapsam kaybı (kaç parça etiketsiz kaldı) raporda görünmeli. Etiketli veri gerekmez.
3. ~~**İt.3 — lisans notu.**~~ **Bitti** (`DEVIR-TESLIM.md` → Notlar). Ürün kararı kullanıcıda. audEERING ses modeli CC-BY-NC-SA-4.0 lisanslı, ticari kullanıma uygun değil. `docs/DEVIR-TESLIM.md` dosyasına not düşülmeli; ürün kararını kullanıcı verir.
4. **İt.3 — veri gerektiren işler:**
   - emotion2vec+ base ile karşılaştırma.
   - Yalnız ses / yalnız metin / ikisi birlikte deneyleri.
   - Güven–kapsam tablosu, macro-F1 ve UAR ölçümleri.
   - `ProsodyModelConfidence` ve füzyon eşiklerinin kalibrasyonu.
5. **İt.1 — gerçek kayıtlarla WER/DER.**
   - Kullanıcı kendi kayıtlarını ve etiketlerini verecek. Dizi sahnesinin etiketleri `quality-local/audio/pilot-01.txt` dosyasında; zamanları düzeltildikten sonra ölçülecek.
   - Araç: `tools/quality` ve Benchmark CLI (`--diarizer windowed|pyannote`).
6. **İt.2 kalanı:** örtüşen konuşma; WhisperX tarzı kelime hizalama (uzun parçayı tek kişiye atamamak için).
7. **İt.4:** 3–5 açık olay (cevapsız soru, belirsiz sonraki adım, tek kişinin uzun konuşması) + kaynak cümleli öneri; TOPSIS ile karşılaştırma.
8. **İt.5:** geçmiş raporu açma, ayarların gerçekten uygulanması, 30 dk test (RTF < 1).

## Ertelenen küçük bulgular (final incelemeden)

- Kod çözücünün bellek tepesi yaklaşık 860 MB.
- `MainViewModel` içindeki koşulsuz `Clear` çağrısı `ClearIf` olmalı.
- Hazırlık sırasında "Dinleniyor / Konuşma bekleniyor / Analiz tamamlanıyor" yazıları yanıltıcı; dosya modunda "ayrım KAPALI" ipucu yanlış.
- `DiarizationJob` XML belgesi yanlış metodun üstünde.
- Bazı test boşlukları.

## Yararlı yollar

- **Konuşmacı ayrımı ölçüm betiği:** `scratchpad/diar-eval/` (git dışında). Modlar: `MODE=app` gerçek `WindowedDiarizer` ile çalışır. Etiketler `scratchpad/enroll-spike/labels.txt` ve `quality-local/audio/pilot-01.txt` dosyalarında.
- **Kayıtlı oturum sesleri:** şifreli `SqliteSessionStore` içinde.
- **Temel belgeler:**
  - `docs/DEVIR-TESLIM.md` — yapılacaklar listesi (işaretli).
  - `docs/research/2026-11-13-bes-iterasyon-strateji-taslagi.md` — strateji.
  - `docs/research/2026-11-13-kalite-ve-acik-kaynak-arastirmasi.md` — kod sorunları ve açık kaynak seçenekler.
