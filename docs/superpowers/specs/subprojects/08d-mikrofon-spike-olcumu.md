# 08d Mikrofon Spike Ölçümü

> Tarih: 2026-07-27 · Donanım: MacBook Air M1, 8 GB, macOS 26.3.1 · .NET 9.0.200 · osx-arm64

## Sonuç

**GEÇTİ** — macOS AudioToolbox / AudioQueue P/Invoke yolu çalışıyor; mikrofon örnekleri .NET'e
istenen formatta ulaşıyor ve gerçek konuşma sessizlikten kesin biçimde ayırt ediliyor.

## Ölçülen değerler

Üç koşu yapıldı. İlk ikisinde kimse konuşmuyordu; üçüncüsünde kullanıcı sürekli konuştu.

| Ölçüm | Koşu 1 (sessiz) | Koşu 2 (sessiz) | Koşu 3 (**konuşurken**) |
|---|---|---|---|
| `AudioQueueNewInput` dönüşü | 0 (OK) | 0 (OK) | 0 (OK) |
| `AudioQueueStart` dönüşü | 0 (OK) | 0 (OK) | 0 (OK) |
| Callback sayısı (5 sn) | 23 | 22 | 23 |
| Toplam bayt | 161.116 | 160.416 | 160.976 |
| Yakalanan süre | 5,03 sn | 5,01 sn | 5,03 sn |
| **Tepe genlik** | 0,0168 | 0,0098 | **0,0805** |

| Soru | Cevap |
|---|---|
| Native callback .NET'e marshal ediliyor mu | **Evet** — 5 saniyede ~22-23 callback |
| İstenen 16 kHz mono PCM16 doğrudan kabul edildi mi | **Evet** |
| Cihazın doğal örnekleme hızı | 48.000 Hz (MacBook Air Mikrofonu) |
| Yeniden örnekleme gerekiyor mu | **HAYIR** — AudioQueue dönüşümü kendisi yapıyor |
| İzin istemi çıktı mı | Hayır — sorumlu uygulamanın (VS Code) izni zaten açıktı |
| `.app` bundle gerekiyor mu | **Evet** (gerekçe aşağıda) |

**Format doğrulaması:** 5,03 sn × 16.000 örnek/sn × 2 bayt = 160.960 bayt beklenir; ölçülen
160.976. Bayt muhasebesi birebir tutuyor — AudioQueue gerçekten 16 kHz PCM16 üretiyor,
48 kHz'i yanlışlıkla 16 kHz diye etiketlemiyor.

**Konuşma doğrulaması:** Konuşurken tepe genlik sessiz koşuların **5-8 katı** (0,0805 vs
0,0098-0,0168). Sessiz oda tabanı ≈ −35 dBFS, konuşma ≈ −22 dBFS. Fark ortam gürültüsüyle
açıklanamaz.

## Süreçte yakalanan iki tuzak

**1. Spike'ın kendi hükmü yanıltıcıydı.** Program `tepe genlik < 0,001` ise "sessiz" diyor.
Sessiz odanın tabanı 0,0098-0,0168 olduğu için, kimse konuşmadığı hâlde program
"GERÇEK SES YAKALANDI" bastı. Tek koşuya bakılsaydı yanlış sonuca varılırdı. Doğru yöntem
**sessiz ve konuşmalı koşuyu karşılaştırmak** oldu.

Bu, alt-proje 05'teki Silero hatasının aynı kalıbı: spike "makul" bir sayı üretip doğruymuş
gibi göründü. Dilim 3'ün gerçek-mikrofon testi bu yüzden mutlak eşik değil, **sessizliğe göre
oran** kullanmalıdır.

**2. İlk iki koşu geçersizdi ve sebebi kod değildi.** Programın "konuşun" uyarısı komutu
çalıştıran agent'a gidiyordu, kullanıcının ekranına değil — kullanıcı konuşma penceresinin
ne zaman açıldığını göremedi. Ölçüm düzeneğinin kendisi hatalıydı. Üçüncü koşuda kullanıcı
sürekli konuşarak pencereyi garantiledi.

## Ölçülen ONNX/AudioQueue kontratı

```
AudioStreamBasicDescription (giriş):
  mSampleRate       = 16000
  mFormatID         = 'lpcm' (0x6C70636D)
  mFormatFlags      = 0xC  (SignedInteger | IsPacked)
  mBytesPerPacket   = 2
  mFramesPerPacket  = 1
  mBytesPerFrame    = 2
  mChannelsPerFrame = 1
  mBitsPerChannel   = 16

AudioQueueNewInput(ref format, callback, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out queue)
  → 0; runLoop/mode NULL geçilebilir (AudioQueue kendi iç thread'ini kullanır)

Buffer: 3 adet × 8192 bayt; callback'te AudioQueueEnqueueBuffer ile geri verilir
Callback delegate GCHandle.Alloc ile pinlenmeli — aksi halde GC toplar ve native taraf çöker
```

`AudioQueueBuffer` struct'ı `LayoutKind.Sequential` ile birebir eşleşiyor: `mAudioDataByteSize`
alanı doğru ofsetten okunuyor (bayt toplamı tutuyor), yani ek `Pack` ayarı gerekmiyor.

## Dilim 3 planına etkisi

1. **Yeniden örnekleme kodu YAZILMAYACAK.** `MicrophoneCaptureSource` doğrudan 16 kHz mono
   PCM16 isteyip alabilir. `AudioPreprocessor`'ın downmix/resample yolu mikrofon için gereksiz
   (dosya yolu için kalmaya devam eder).
2. **`.app` bundle üretimi Dilim 3'ün parçası.** Bu ölçümde izin, sorumlu uygulama olan
   VS Code üzerinden geldi. Son kullanıcı uygulamayı terminalden değil çift tıklayarak
   çalıştıracağı için, mikrofon izninin uygulamanın kendisine verilmesi gerekir:
   `Info.plist` içinde `NSMicrophoneUsageDescription` bulunan bir `.app` paketi şart.
   Bu olmadan macOS ya istem göstermez ya da sessizce sessizlik döndürür.
3. **İzin reddi sessizdir — dürüstçe ele alınmalı.** İzin yoksa `AudioQueueNewInput` hata
   DÖNMEZ; akış gelir ama örnekler sessizdir. `MicrophoneCaptureSource` bunu tespit edip
   (ör. ilk saniyelerde tepe genlik sessizlik tabanının üzerine çıkmıyorsa) kullanıcıya
   "mikrofon izni verilmemiş görünüyor" diyen anlaşılır bir uyarı göstermeli — sessizce
   boş analiz üretmemeli.
4. **Gerçek-mikrofon testi env-gated olacak** (`MIC_TEST=1`) ve mutlak eşik yerine
   sessizlik-tabanına-göre-oran kullanacak (yukarıdaki tuzak 1).

## Yeniden üretme

Spike programı `scratchpad/mic-spike/` altındaydı (git'e dahil değil, ölçüm sonrası silindi).
Yukarıdaki kontrat ve değerler Dilim 3'ün uygulama planına girdi olarak yeterlidir.
