# Pencereli konuşmacı ayrımı — ölçüm (İterasyon 2'nin ilk adımı, 2026-11-13)

**Soru:** Kullanıcı canlı denemede "ses ayırma hâlâ yetersiz" dedi. Sorun hangi bileşende ve hızlıca düzelir mi?

**Veri:** 5 iki kişilik kayıt, insan etiketli: 12a spike'ındaki 4 oturum (`scratchpad/enroll-spike/labels.txt`, yalnız içerikten kesin anlaşılan aralıklar, kenarlar ~0,3 sn içeri çekilmiş) + dizi sahnesi `pilot-01` (kaba ±1 sn etiket; her aralığın iki ucundan 0,5 sn atıldı). Az veri: sonuç yön gösterir, genellenebilir doğruluk iddiası değildir.

**Ölçü:** Etiketli süre içinde doğru kişiye atanan pay (tahmin kümeleri etiketlere bire-bir, en iyi eşleme; atanmamış süre yanlış sayılır). DER'in "karışıklık + kaçırma" kısmına karşılık gelir; yanlış alarm ölçülmez (etiketler kısmi). Araç: `scratchpad/diar-eval` (git dışı; ses bellekte okunur).

## Sonuçlar (kişi sayısı 2 verildi)

| Yöntem | 17eaac4b | 73d4b0ad | 4a6ad925 | 6339a9fa | pilot-01 | Ortalama | Süre (5 kayıt) |
|---|---|---|---|---|---|---|---|
| sherpa + pyannote, resnet34 (eski varsayılan) | 89 | 85 | 59 | 66 | 58 | **71** | ~2,5 dk |
| sherpa + pyannote, TitaNet-small | 88 | 85 | 59 | 66 | 58 | 71 | ~2,5 dk |
| sherpa + pyannote, CAM++ | 58 | 85 | 59 | 66 | 58 | 65 | ~2,5 dk |
| sherpa + pyannote, ERes2NetV2 | 88 | 62 | 59 | 48 | 58 | 63 | ~2,5 dk |
| Pencereli, tek başlangıçlı k-means | 83 | 97 | 89 | 48 | 88 | 81 | 13 sn |
| **Pencereli, çok başlangıçlı k-means (uygulamaya alındı)** | 83 | 97 | 89 | 69 | 88 | **85** | **11 sn** |

- Embedding modelini değiştirmek pyannote hattında hiçbir şeyi düzeltmiyor → darboğaz **segmentasyon** (karşılıklı konuşmayı 13–17 sn'lik tek parça kesiyor; 2026-11-09 bulgusu doğrulandı).
- Pencereli yöntem: 1,5 sn pencere / 0,5 sn adım, VAD'lı dilimler, TitaNet-small imzası, k-means (40 başlangıç, %10'dan küçük küme cezalı), 3 komşu çoğunluğu. Pencere 2,0 sn → %78; yumuşatma 5 → %85 (aynı).
- Tek başlangıç 6339a9fa'da aykırı tek pencereyi ayrı "kişi" seçiyordu (%48) → çok başlangıç + küçük küme cezası (%69).

## Kişi sayısı bilinmiyorsa (k otomatik)

k=2 küme merkezleri arası kosinüs: farklı iki kişi 0,33–0,51; aynı kişinin (yalnız onun etiketli aralıkları) iki yarısı 0,51–0,87. Eşik **0,55** (`SameSpeakerCosine`). Uygulama sınıfıyla bilinmeyen sayıda ortalama yine %85 (17eaac4b'de küçük üçüncü küme; GhostSpeakerFilter ayıklar). **3+ kişiyle doğrulanmadı.**

## Uygulama

`WindowedDiarizer` (Application/Diarization) varsayılan `IDiarizer` (`DiarizationConfig.UseWindowedDiarizer = true`; false → eski sherpa+pyannote). Dosya modundaki ön ayrım ve oturum sonrası konuşmacı geçişi bunu kullanır. Benchmark: `diarize --diarizer windowed|pyannote`. 90 dk ses ≈ 10 800 pencere × ~16 ms ≈ 3 dk (eski RTF 0,44 ≈ 40 dk) — tahmindir, uzun kayıtla ölçülmedi.

## Sıradaki

1. Kullanıcının kendi kayıtları + düzeltilmiş pilot-01 zamanlarıyla tam DER (yanlış alarm dahil) — İterasyon 1 harness'i (`tools/quality`, `--diarizer`).
2. Üst üste konuşma şu an tek kişiye atanıyor; kelime hizalama (WhisperX tarzı) İterasyon 2'nin kalanı.
