# Whisper duraklama regresyonu — 14 Kasım 2026

## Sorun ve değişiklik

Yerel, iki konuşmacılı 116,63 saniyelik kayıtta 30 saniyelik örtüşmesiz transkripsiyon,
son soruyu yazıp yaklaşık 20 saniyelik cevabı tamamen atlıyordu. Aynı kayıt 90. saniyeden
ayrı verilince sorun tekrarlandı; 94. saniyeden verilince cevap çıktı. Ses dosyası eksik değildi.

`WhisperSpeechToText` çözümleme yöntemi varsayılan greedy yerine beş adaylı beam search oldu.
Bu hem Benchmark hem uygulamanın kullandığı ortak STT bileşenidir. Model, Türkçe dil seçimi,
ses örnekleme hızı ve bölümleme değiştirilmedi. Yalnız bu yöntem değişikliğiyle regresyon geçti;
native çözücünün iç nedeninin kesin olarak ayrıştırıldığı iddia edilmiyor.

[Whisper.net](https://github.com/sandrohanea/whisper.net) 1.9.1 yerel paket belgelerindeki
`WithBeamSearchSamplingStrategy` / `WithBeamSize` API'leri kullanıldı. Beş aday açıkça belirtildi.

## TDD kanıtı

`TranscribeAsync_RealModel_PreservesSpeechAfterPause` testi önce eklendi.
Eski kodda beklenen son cümle bulunamadığı için kırmızı oldu; çıktı yalnız sorunun sonuydu.
Beam search ile aynı ses üzerinde yeşil oldu. Test son bölümün zaman damgasını da denetler.
Özel kayıt repoya alınmaz; gerekli yerel dosyalar yoksa test açıkça atlanır.

Tekrar çalıştırma (repo kökünden; model yolu yerel kurulumunuza göre değiştirilebilir):

```sh
WHISPER_MODEL_PATH="$HOME/Library/Application Support/SentimentAnalysis/models/ggml-large-v3-turbo-q5_0.bin" \
WHISPER_PAUSE_WAV="$PWD/quality-local/semsi-01/son-bolum.wav" \
WHISPER_PAUSE_EXPECTED='İyi bir yazar' \
dotnet test tests/SentimentAnalysis.Infrastructure.Stt.Tests --filter PreservesSpeechAfterPause
```

`son-bolum.wav`, tam kaydın 90. saniyesinden sonuna kadardır (26,63 sn).
Kayıt yoksa aynı hata koşulunu sağlayan, kısa söz + duraklama + 20. saniyeden sonra da süren
konuşma içeren bir yerel WAV ve ona ait beklenen ifade gerekir.

## Tam kayıt karşılaştırması

Aynı model (`large-v3-turbo-q5_0`), iki iş parçacığı, 30 sn örtüşmesiz bölme,
VAD kapalı, .NET Debug Benchmark. Kullanıcının sağladığı 218 kelimelik metin referanstır;
okurken yapılan sapmalar ayrıca insan tarafından kontrol edilmedi.

| Koşul | Kelime hatası | WER | İşlem süresi | Bileşen RTF |
|---|---:|---:|---:|---:|
| Önce, greedy | 56 / 218 | %25,69 | 24,00 sn | 0,206 |
| Sonra, beam 5 | 14 / 218 | %6,42 | 28,46 sn | 0,244 |

Son cevap yeni **tam kayıt koşusunda** mevcut; elle kesit eklenmedi.
Önceki %4,13 sonucu elle seçilmiş ayrı son cevabı ekleyen tanısal sonuçtu;
üretim düzeltmesinin sonucu olarak kullanılmaz. Yeni koşuda kısa yanlış/ek sözcükler hâlâ var.
Tek koşuluk süreler; önbellek ve termal koşullar kontrol edilmedi. Canlı uygulama RTF'si değildir.

Ham tahmin, model hash'leri, süreler ve değişen STT kaynağının hash'i
`quality-local/whisper-beam-01/` içinde; önceki koşular `quality-local/semsi-01/` altında.
Bu kayıt yöntemi seçerken kullanıldığı için bağımsız test başarısı olarak sunulamaz.
Genellenebilir WER, bellek etkisi, 30 dakika canlı performansı, duygu doğruluğu ve kesin DER ölçülmedi.

## Ek kontroller ve sınırlar

- Muğla kaydı yeni yöntemle 39,74 sn (bileşen RTF 0,263) içinde işlendi; önceki koşu
  35,13 sn idi. İnsan denetimli metni olmadığı için WER karşılaştırılmadı.
- 30 sn sıfır PCM, VAD kullanılmayan ham STT koşusunda hem eski greedy hem yeni beam
  ile aynı uydurma altyazıyı üretti. Bu değişiklikle ortaya çıkan bir fark değildir;
  beam search sessizlikte metin uydurmayı çözmez. Uygulamada `ChunkProcessor` önce
  Silero VAD uygular; ham `transcribe` kıyaslamasında VAD bilinçli olarak kapalıdır.
- Bu sonuçlara dayanarak konuşmacı sınırları veya duygu eşikleri değiştirilmedi.

## Son doğrulama

`dotnet test`: 12 test projesinde 1.096 başarılı, 14 atlanan, 0 başarısız.
Whisper duraklama testi, mevcut Türkçe gerçek model testi ve iki Silero VAD gerçek model
testi ortam değişkenleriyle etkinleştirildi; bu dört test geçti.
`python3 -m unittest discover -s tools/quality`: 37 başarılı.
