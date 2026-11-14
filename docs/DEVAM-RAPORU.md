# Devam raporu — 14 Kasım 2026

Bu belge, "Canlı Görüşme KDS" projesinde kalite iterasyonlarının **nerede kaldığını** ve **nereden devam edileceğini** anlatır. Yeni oturum ya da başka bir yapay zeka, önce bu dosyayı, sonra `docs/DEVIR-TESLIM.md` dosyasını okumalı.


## SON DURUM — 14 Kasım 2026 akşam (bu bölüm aşağıdakilerin hepsinden önce gelir)

Beş iterasyonun **veri gerektirmeyen bütün işleri bitti**. Kalan her şey insan etiketi ya da cihazda elle deneme ister.

| Commit | İş |
|---|---|
| `deaccb4` | İt.5: rapor ekranında metin ve konuşmacı düzeltme; ayrı tabloda kalıcı, açık olaylar düzeltilmiş metne göre yenilenir. |
| `0b9cabb`, `bd0747e` | İt.2: Whisper sessizlik halüsinasyonu ("Altyazı M.K.") süzgeci; yalnız segmentin tamamı kalıpsa, kalite sinyalinde kalır. |
| `b97c8c0` | İt.2: kişi sayısı verildiyse kısa konuşan gerçek kişi "hayalet" sayılmaz. İt.5: yüz yalnız karşı tarafın duygusuna katılır. |
| `365162f` | İt.4: aktarılan soru ("… mi … diye") yanlış "cevapsız soru" üretmez; Benchmark `--profile`. |

Ölçümler (geliştirme kayıtları, bağımsız test değil):
- Kelime zamanıyla konuşmacı ataması denendi, **eklenmedi** (%98→%99; DTW çöktü) — `docs/research/2026-11-14-kelime-zamani-deneyi.md`.
- 30 dk ses hattı: RTF 0,52, kapsam %100, yavaşlama yok, tepe bellek 3,6 GB — `docs/research/2026-11-14-uzun-gorusme-testi.md`.
- Öneri karşılaştırması: 3 sakin kayıtta TOPSIS 13/13 "Devam et", kurallar 0 olay → ayırt edici kayıt gerekiyor (aynı belge).

Testler: `dotnet test` 12 projede 1.135 başarılı / 18 atlanan / 0 başarısız; `tools/quality` Python testleri yeşil.
Push: yapılmadı (origin `67d1c6c`'de; aradaki tüm commit'ler onay bekliyor).

**İnsan gerektiren kalan işler:** (1) Şemsi/Muğla/pilot etiketlerinin insan denetimi + duygu etiketleri → İt.1 WER/DER
kesin ölçümü ve İt.3 kalibrasyonu; (2) sorunlu anlar içeren 2–3 kayıt + kör değerlendirme (İt.4); (3) cihazda canlı
mikrofonla 30 dk ve tekrarlı başlat/durdur, "Sesimi tanıt" akışı, rapor ekranındaki düzeltme alanı (İt.5).


## Son çalışma — 14 Kasım 2026: duraklama sonrası eksik metin

Kullanıcı yeni bir iki kişilik kayıt, referans metin ve yaklaşık konuşmacı zamanları verdi.
Bu nedenle önceki “kayıt bekleniyor” engeli kısmen kalktı. İnsan denetimli sessizlik sınırları
ve duygu etiketleri hâlâ yok; kesin DER ve duygu kalibrasyonu bekliyor.

- Son cevabın atlanması gerçek model testiyle önce kırmızı, sonra yeşil olarak doğrulandı.
- Ortak `WhisperSpeechToText` bileşeni beş adaylı beam search kullanıyor.
- Tam kayıtta 218 referans kelimeye göre WER %25,69 → %6,42; süre 24,00 → 28,46 sn.
  Bu bir geliştirme kaydıdır; bağımsız test veya genel doğruluk ölçümü değildir.
- Ayrıntılar ve tekrar çalıştırma: `docs/research/2026-11-14-whisper-duraklama-regresyonu.md`.
- Özel ses/metin ve ham ölçümler `quality-local/` altında, git dışında. Push yapılmadı.

## Devralma doğrulaması — 14 Kasım 2026 (Codex)

**Aşağıdaki eski durum tablosu ve push bilgileri yerine bu bölüm esas alınmalı.**
Kod ve yerel Git geçmişi incelendi; HEAD `8dc8a20`, dal `11-karar-destek`.
Yerel `origin/11-karar-destek` referansı `67d1c6c`; uzak sunucudan fetch yapılmadı.
Güvenlik düzeltmeleri, sigmoid/ses güveni ve “belirsiz” commit'leri bu referansta mevcut.

Rapor yazıldıktan sonra tamamlanan ve yerel origin referansına göre **push bekleyen** işler:

| Commit | İş |
|---|---|
| `248a70c` | İt.4: kaynak cümleli üç olay kuralı (cevapsız soru, belirsiz sonraki adım, uzun tek kişi). |
| `2886bd1` | İt.5: geçmişten saklanan raporu açma. |
| `e2734f9` | İt.5: ayarları kaydetme/açılışta uygulama, ses saklama tercihi. |
| `e57cafd` | ClearIf, hazırlık yazıları, dosya modu ipucu, XML belgesi. |
| `8dc8a20` | İt.2: uzun parçadaki kelimeleri konuşma sürelerine orantılı bölme. |

Son madde **gerçek kelime zaman hizalaması değildir**; eşit konuşma hızı varsayar.
Örtüşen konuşma ve gerçek kelime hizalaması tamamlanmış sayılmaz.
İt.4'ün TOPSIS karşılaştırması ve kör insan değerlendirmesi; İt.5'in kişi/metin düzeltmesi,
30 dakika ve tekrarlı başlat/durdur denemeleri hâlâ bekliyor.

Doğrulama: `dotnet test` 12 test projesinde **1.092 başarılı, 17 atlanan, 0 başarısız**;
`python3 -m unittest discover -s tools/quality` **37 başarılı**.
Bu sonuçlar gerçek model doğruluğunu, arayüzün elle kullanımını veya performans kazancını kanıtlamaz.
Önceki değişikliklerin kırmızı→yeşil aşamaları bu oturumda yeniden gözlenmedi.

**Devam noktası:** kullanıcının “etiketli kayıt gereken işte dur” talimatı gereği kayıt bekleniyor.
`quality-local/pilot.json` tek public kayıt içeriyor; `referenceStatus=pending` ve duygu etiketi yok.
İnsan denetimli başlangıç/bitiş, konuşmacı, metin ve duygu etiketleri gerekli.
WER/DER başlangıç ölçümü, macro-F1/UAR, güven–kapsam, füzyon kalibrasyonu ve model
karşılaştırmaları bu devralmada yapılmadı. Yeni kod commit'i veya push yapılmadı.

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
