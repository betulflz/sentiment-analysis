# Alt-Proje 10a Uygulama Planı — Diarization Spike (Ölçüm)

> **Agentic worker'lar için:** GEREKLİ ALT-BECERİ: Bu planı task-task uygulamak için
> `superpowers:subagent-driven-development` (önerilen) veya `superpowers:executing-plans`
> kullanın. Adımlar takip için checkbox (`- [ ]`) sözdizimi kullanır.

**Hedef:** Konuşmacı ayrımının bu projede gerçekten çalışıp çalışmadığını **kod yazmadan
önce gerçek donanımda ölçmek** — ve 10b'ye GEÇ/GEÇME kararını sayılara dayandırmak.

**Architecture:** Bu bir spike'tır: ürün kodu üretmez. `scratchpad/` altında geçici bir .NET
konsol projesi kurulur (repoya girmez, `.gitignore` kapsamındadır), üç ölçüm yapılır, sonuç
bir rapor dosyasına yazılır ve spike silinir. Repoda kalan tek kalıcı çıktı **ölçüm raporudur**.

**Tech Stack:** .NET 9 · `org.k2fsa.sherpa.onnx` 1.13.4 (NuGet) · `Microsoft.ML.OnnxRuntime` 1.28.0 ·
`Whisper.net` 1.9.1 · macOS arm64 (MacBook Air M1, 8 GB)

> **Uygulama sırasında bulunan API düzeltmesi (2026-07-27):** `SherpaOnnx.WaveReader` NuGet
> paketinde **YOKTUR** (sherpa-onnx'in dotnet örneklerinde kendi yazdıkları yardımcı sınıftır).
> Aşağıdaki kod bloklarında geçen `new SherpaOnnx.WaveReader(path)` yerine elle yazılmış bir
> 16-bit PCM mono WAV okuyucu kullanılır: `ReadWav(path)` → `(float[] Samples, int SampleRate)`.
> Ayrıca yerel fonksiyona `Whisper` adı verilemez — `Whisper.net` namespace'iyle çakışır
> (CS0119); `WhisperNet` kullanın. Ve macOS'ta `Process.PeakWorkingSet64` **her zaman 0 döner**;
> tepe bellek için `WorkingSet64` ölçüm noktalarının maksimumu elle takip edilmelidir.

**Spec:** [`10-konusmaci-ayrimi.md`](../specs/subprojects/10-konusmaci-ayrimi.md)

---

## Global Kısıtlar

Her task'ın gereksinimleri örtük olarak bu bölümü içerir.

- **Ürün koduna DOKUNULMAZ.** `src/` ve `tests/` altında tek satır değişmez. Bu spike'ın
  amacı öğrenmek, inşa etmek değil. Başlangıç durumu korunur: **296 geçer, 8 atlanır,
  0 başarısız, 0 uyarı.**
- **Spike `scratchpad/` altında yaşar** (`.gitignore:65` kapsamında) ve Task 4'te silinir.
- **Ölçülen sayılar rapora AYNEN yazılır.** Beklenen değer değil, ölçülen değer. Sayı kötüyse
  kötü yazılır — spike'ın işi haber vermektir, onaylamak değil (master §12.2).
- **Tek koşuya güvenilmez (08d dersi).** 08d spike'ında program kendi eşiğine bakıp sessiz odada
  "GERÇEK SES YAKALANDI" demişti; hata ancak sessiz koşu ile konuşmalı koşu karşılaştırılınca
  görüldü. Bu spike'ın her ölçümü bir **karşılaştırma** kurar: aynı konuşmacı vs farklı
  konuşmacı, sherpa'lı vs sherpa'sız. Mutlak eşiğe bakıp "iyi görünüyor" denmez.
- **Modeller `~/Library/Application Support/SentimentAnalysis/models/` altına iner** (mevcut
  5 modelin yanına), spike silinse de kalırlar — 10b onları kullanacak.
- **Kullanıcı kaydı olmadan Task 2 ve 3 çalıştırılamaz.** Kayıt yoksa DUR ve iste; sentetik
  ses veya tek kişilik kayıtla ölçüm yapılmaz (yanlış güven üretir).

---

## Kullanıcıdan istenen girdi (Task 2 öncesi zorunlu)

**Kayıt protokolü** — kullanıcı bunu kendi gerçek ortamında yapar:

- **Ortam:** Toplantı yaptığı oda, mikrofon normal toplantı mesafesinde (kulaklık/yaka mikrofonu
  DEĞİL). Canlı modun gerçek koşulu budur; laboratuvar kaydı bizi kandırır.
- **Kişi:** 2 veya 3 kişi. (2 yeterli; 3 daha bilgilendirici.)
- **Süre:** ~2 dakika.
- **Düzen:** Sırayla konuşulur, **her konuşma ~15 saniye**, tur iki kez döner. Örnek:
  `A(15sn) → B(15sn) → C(15sn) → A(15sn) → B(15sn) → C(15sn)`
- **İçerik:** Doğal Türkçe konuşma (ne söylendiği önemli değil).
- **Kayıt sırasında kim ne zaman konuştuğu bir kağıda yazılır** — bu bizim **referans
  doğrumuz** (ground truth); onsuz "doğru mu?" sorusunun cevabı yok.
- **Dosya:** 16 kHz mono WAV olarak `scratchpad/diar-spike/toplanti.wav`, referans zamanlar
  `scratchpad/diar-spike/referans.txt` (her satır: `başlangıç_sn bitiş_sn konuşmacı`).

**Neden bu kadar katı:** Diarization'ın "çalışıyor gibi görünmesi" kolaydır — herhangi bir
kayıtta bir şeyler bulur ve segment listesi üretir. O listenin doğru olup olmadığını yalnızca
referansla kıyaslayarak bilebiliriz.

---

## Dosya Yapısı

| Dosya | Sorumluluk | Task |
|---|---|---|
| `scratchpad/diar-spike/DiarSpike/DiarSpike.csproj` | Geçici konsol projesi (repoya girmez) | 1 |
| `scratchpad/diar-spike/DiarSpike/Program.cs` | Üç ölçümün tamamı; `args[0]` ile mod seçilir | 1, 2, 3 |
| `scratchpad/diar-spike/toplanti.wav` | Kullanıcının gerçek kaydı | kullanıcı |
| `scratchpad/diar-spike/referans.txt` | Referans doğru (kim ne zaman konuştu) | kullanıcı |
| `docs/superpowers/specs/subprojects/10a-diarization-spike-olcumu.md` | **Kalıcı ölçüm raporu** | 4 |

---

## Task 1: Native çakışma ölçümü

Bu ölçüm **ilk sırada** çünkü en yıkıcı riski test ediyor: sherpa-onnx kendi native
onnxruntime kütüphanelerini taşır; projede zaten `Microsoft.ML.OnnxRuntime` 1.28.0 (duygu +
Silero VAD) ve `Whisper.net.Runtime` 1.9.1 native'i var. Üçü aynı süreçte çakışırsa —
sembol çakışması, sürüm uyuşmazlığı, çökme — 10b'nin tüm motor kararı (S3) çöper ve
saf-ONNX veya sidecar yoluna dönmek gerekir. Bunu ölçmeden yazılan her satır risk altındadır.

**Files:**
- Oluştur: `scratchpad/diar-spike/DiarSpike/DiarSpike.csproj`
- Oluştur: `scratchpad/diar-spike/DiarSpike/Program.cs`

**Interfaces:**
- Consumes: —
- Produces: `dotnet run --project scratchpad/diar-spike/DiarSpike -- cakisma` komutu;
  konsola çakışma raporu basar.

- [ ] **Adım 1: Spike projesini kur**

```bash
mkdir -p scratchpad/diar-spike
cd scratchpad/diar-spike
dotnet new console -n DiarSpike
cd DiarSpike
dotnet add package org.k2fsa.sherpa.onnx
dotnet add package Microsoft.ML.OnnxRuntime --version 1.28.0
dotnet add package Whisper.net --version 1.9.1
dotnet add package Whisper.net.Runtime --version 1.9.1
```

- [ ] **Adım 2: Çözülen sherpa-onnx sürümünü kaydet**

```bash
cd /Users/betulfiliz/sentiment-analysis/scratchpad/diar-spike/DiarSpike
grep -n "sherpa" DiarSpike.csproj
```

Çıkan sürüm numarasını not edin — Task 4'te rapora yazılacak. (Plan yazıldığında NuGet'teki
güncel sürüm 1.13.4'tü; farklı çıkarsa gerçek çıkan yazılır.)

- [ ] **Adım 3: Çakışma ölçüm kodunu yaz**

`scratchpad/diar-spike/DiarSpike/Program.cs` — bu dosya üç modun hepsini barındıracak;
şimdilik `cakisma` modu yazılır:

```csharp
using System.Diagnostics;

string mode = args.Length > 0 ? args[0] : "cakisma";
string modelDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "Library", "Application Support", "SentimentAnalysis", "models");

switch (mode)
{
    case "cakisma": Cakisma(modelDir); break;
    default: Console.WriteLine($"Bilinmeyen mod: {mode}"); break;
}

// Üç native runtime'ı AYNI süreçte, arka arkaya yükleyip çalıştırır.
// Amaç: yükleme sırası fark eder mi, sembol/sürüm çakışması var mı, çöküyor mu?
static void Cakisma(string modelDir)
{
    Console.WriteLine("=== Native çakışma ölçümü ===");
    Console.WriteLine($".NET: {Environment.Version}, OS: {Environment.OSVersion}, " +
                      $"Arch: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");

    long rss0 = Process.GetCurrentProcess().WorkingSet64;
    Console.WriteLine($"Başlangıç RSS: {rss0 / 1024 / 1024} MB");

    // 1) Microsoft.ML.OnnxRuntime — mevcut Silero VAD modeliyle
    try
    {
        string vad = Path.Combine(modelDir, "silero_vad.onnx");
        using var s = new Microsoft.ML.OnnxRuntime.InferenceSession(vad);
        Console.WriteLine($"[1] Microsoft.ML.OnnxRuntime YÜKLENDİ — silero girdi sayısı: {s.InputMetadata.Count}");
    }
    catch (Exception ex) { Console.WriteLine($"[1] Microsoft.ML.OnnxRuntime HATA: {ex.GetType().Name}: {ex.Message}"); }
    Console.WriteLine($"    RSS: {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB");

    // 2) sherpa-onnx — kendi native'ini AYNI sürece yükler (asıl risk burada)
    try
    {
        var cfg = new SherpaOnnx.SpeakerEmbeddingExtractorConfig();
        cfg.Model = Path.Combine(modelDir, "speaker-embedding.onnx");
        var ext = new SherpaOnnx.SpeakerEmbeddingExtractor(cfg);
        Console.WriteLine($"[2] sherpa-onnx YÜKLENDİ — embedding boyutu: {ext.Dim}");
    }
    catch (Exception ex) { Console.WriteLine($"[2] sherpa-onnx HATA: {ex.GetType().Name}: {ex.Message}"); }
    Console.WriteLine($"    RSS: {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB");

    // 3) Whisper.net — üçüncü native
    try
    {
        string ggml = Path.Combine(modelDir, "ggml-large-v3-turbo-q5_0.bin");
        using var factory = Whisper.net.WhisperFactory.FromPath(ggml);
        Console.WriteLine("[3] Whisper.net YÜKLENDİ");
    }
    catch (Exception ex) { Console.WriteLine($"[3] Whisper.net HATA: {ex.GetType().Name}: {ex.Message}"); }
    Console.WriteLine($"    RSS: {Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024} MB");

    Console.WriteLine("=== Üçü de yüklendiyse ve süreç buraya kadar geldiyse: çakışma YOK ===");
}
```

- [ ] **Adım 4: Embedding modelini indir**

`[2]` adımının çalışabilmesi için model gerekir. 3D-Speaker ERes2Net (16 kHz, ~38 MB):

```bash
curl -L -o "$HOME/Library/Application Support/SentimentAnalysis/models/speaker-embedding.onnx" \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx"
ls -la "$HOME/Library/Application Support/SentimentAnalysis/models/speaker-embedding.onnx"
```

> Not: URL'deki `speaker-recongition-models` yazımı sherpa-onnx deposundaki gerçek etiket adıdır
> (üstteki yazım hatası onların); düzeltmeyin, 404 alırsınız. Üç URL de bu plan yazılırken
> HTTP 200 döndüğü doğrulanmıştır.

- [ ] **Adım 5: Ölçümü çalıştır**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet run --project scratchpad/diar-spike/DiarSpike -- cakisma
```

Beklenen: `[1]`, `[2]`, `[3]` satırlarının üçü de "YÜKLENDİ" desin ve süreç çökmeden bitsin.

**Sonuç ne olursa olsun konsol çıktısının TAMAMI kaydedilir** (Task 4 raporuna girecek).
Herhangi biri HATA verirse veya süreç çökerse **DUR** — bu, S3 kararını (sherpa-onnx motoru)
geçersiz kılan bir bulgudur; kullanıcıya bildirilip escape-hatch (saf-ONNX veya Python
sidecar, master D3/D4) konuşulmalıdır. Task 2'ye geçilmez.

- [ ] **Adım 6: Yükleme sırasını ters çevirip tekrarla**

Native yükleme çakışmaları sıraya duyarlı olabilir (önce yüklenen kütüphane sembolleri
kazanır). `Cakisma` içindeki üç bloğun sırasını `[2] → [1] → [3]` olacak şekilde değiştirin
ve tekrar çalıştırın:

```bash
dotnet run --project scratchpad/diar-spike/DiarSpike -- cakisma
```

İki sıranın da çıktısı kaydedilir. **Sıra fark ediyorsa** bu 10b için kritik bir kısıttır
(DI'da yükleme sırası sabitlenmeli) ve rapora ayrıca yazılır.

---

## Task 2: Gerçek kayıtta offline diarization doğruluğu

Bu, kullanıcının asıl sorusunun ölçümü: *farklı kişilerin konuştuğunu gerçekten ayırt
edebiliyor muyuz?* — hem de en zor koşulda (odadaki tek mikrofon, uzak alan).

**Files:**
- Değiştir: `scratchpad/diar-spike/DiarSpike/Program.cs` (`diarize` modu eklenir)

**Interfaces:**
- Consumes: Task 1'in kurduğu proje ve doğrulanmış sherpa-onnx yüklenebilirliği
- Produces: `dotnet run --project scratchpad/diar-spike/DiarSpike -- diarize <wav>` komutu;
  bulunan konuşmacı sayısını ve segment listesini basar

- [ ] **Adım 1: Kullanıcı kaydını ve referansı doğrula**

```bash
ls -la scratchpad/diar-spike/toplanti.wav scratchpad/diar-spike/referans.txt
```

**Dosyalar yoksa DUR** ve kullanıcıdan yukarıdaki "Kayıt protokolü"ne göre kayıt iste.
Sentetik ses, tek kişilik kayıt veya kulaklıkla alınmış kayıt kullanılmaz — canlı modun
gerçek koşulu odadaki mikrofondur ve kolay koşulda ölçmek yanlış güven üretir.

- [ ] **Adım 2: Segmentation modelini indir**

```bash
cd "$HOME/Library/Application Support/SentimentAnalysis/models"
curl -L -o seg.tar.bz2 \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2"
tar xf seg.tar.bz2 && rm seg.tar.bz2
find . -name "*.onnx" -path "*segmentation*"
```

Çıkan `.onnx` yolunu not edin (tipik: `sherpa-onnx-pyannote-segmentation-3-0/model.onnx`).

- [ ] **Adım 3: Diarization ölçüm kodunu yaz**

`Program.cs` içindeki `switch`'e ekleyin:

```csharp
    case "diarize": Diarize(modelDir, args[1]); break;
```

ve aşağıdaki metodu dosyanın sonuna ekleyin:

```csharp
// Tüm kayıt üzerinde offline diarization çalıştırır ve segmentleri basar.
// Clustering.NumClusters = 0 → konuşmacı sayısı BİLİNMİYOR, Threshold ile otomatik bulunur.
// Bu bilinçli: gerçek toplantıda kaç kişi olduğunu önceden bilmeyiz.
static void Diarize(string modelDir, string wavPath)
{
    Console.WriteLine("=== Offline diarization ölçümü ===");
    var cfg = new SherpaOnnx.OfflineSpeakerDiarizationConfig();
    cfg.Segmentation.Pyannote.Model =
        Path.Combine(modelDir, "sherpa-onnx-pyannote-segmentation-3-0", "model.onnx");
    cfg.Embedding.Model = Path.Combine(modelDir, "speaker-embedding.onnx");
    cfg.Clustering.NumClusters = 0;     // bilinmiyor
    cfg.Clustering.Threshold = 0.5f;    // başlangıç; Adım 5'te taranacak

    var sd = new SherpaOnnx.OfflineSpeakerDiarization(cfg);
    var reader = new SherpaOnnx.WaveReader(wavPath);
    Console.WriteLine($"WAV: {reader.SampleRate} Hz, {reader.Samples.Length} örnek, " +
                      $"{reader.Samples.Length / (double)reader.SampleRate:F1} sn");
    if (sd.SampleRate != reader.SampleRate)
    {
        Console.WriteLine($"HATA: model {sd.SampleRate} Hz bekliyor, dosya {reader.SampleRate} Hz.");
        return;
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var segments = sd.Process(reader.Samples);
    sw.Stop();

    double audioSec = reader.Samples.Length / (double)reader.SampleRate;
    Console.WriteLine($"Süre: {sw.Elapsed.TotalSeconds:F2} sn  (RTF = {sw.Elapsed.TotalSeconds / audioSec:F3})");
    Console.WriteLine($"Tepe RSS: {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024} MB");

    var speakers = new HashSet<int>();
    Console.WriteLine("--- segmentler ---");
    foreach (var s in segments)
    {
        speakers.Add(s.Speaker);
        Console.WriteLine($"{s.Start,7:F2} - {s.End,7:F2}  konuşmacı_{s.Speaker}");
    }
    Console.WriteLine($"--- BULUNAN KONUŞMACI SAYISI: {speakers.Count} ---");
}
```

- [ ] **Adım 4: Ölçümü çalıştır**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet run --project scratchpad/diar-spike/DiarSpike -- diarize scratchpad/diar-spike/toplanti.wav
```

Çıktının tamamını kaydedin.

- [ ] **Adım 5: Eşik taraması yap (tek sayıya güvenme)**

`Clustering.Threshold` sonucu doğrudan belirler; tek değere bakıp "iyi/kötü" demek 08d
hatasını tekrarlamaktır. `Diarize` içindeki `cfg.Clustering.Threshold` satırını sırayla
`0.3f`, `0.4f`, `0.5f`, `0.6f`, `0.7f` yapıp her seferinde çalıştırın ve **bulunan konuşmacı
sayısını** kaydedin:

| Threshold | Bulunan konuşmacı | Referanstaki gerçek |
|---|---|---|
| 0.3 | ? | (kullanıcının kaydındaki kişi sayısı) |
| 0.4 | ? | " |
| 0.5 | ? | " |
| 0.6 | ? | " |
| 0.7 | ? | " |

- [ ] **Adım 6: Referansla karşılaştır ve doğruluğu hesapla**

En iyi eşiğin segment listesini `scratchpad/diar-spike/referans.txt` ile elle karşılaştırın
ve şu üç soruyu **sayıyla** cevaplayın:

1. **Konuşmacı sayısı doğru mu?** (bulunan vs gerçek)
2. **Konuşmacı değişim anları ne kadar isabetli?** Referanstaki her değişim anına en yakın
   tespit edilen sınırın kaç saniye saptığını yazın; ortalama sapmayı hesaplayın.
3. **Segmentlerin ne kadarı doğru konuşmacıya atanmış?** Referans turlarını sırayla eşleyip
   (A→konuşmacı_0 gibi) doğru atanan toplam saniyeyi toplam konuşma saniyesine bölün.

Bu üçü rapora yazılır. **"Gözle iyi görünüyor" kabul edilmez** — 08e dersi tam olarak buydu.

---

## Task 3: Online kümeleme eşiğinin kalibrasyonu

Canlı geçiş (S2) sherpa'nın offline diarization'ını kullanamaz; kendi `OnlineSpeakerClusterer`
sınıfımız embedding'ler arası kosinüs benzerliğine ve bir **eşiğe** bakacak. Spec'teki
`SimilarityThreshold = 0.55` değeri açıkça "kalibre edilmemiş bir başlangıç tahmini" olarak
işaretlendi. Bu task o sayıyı ölçümle değiştirir.

Ölçümün mantığı bir **karşılaştırmadır** (mutlak eşik değil): aynı kişinin iki farklı
konuşmasının benzerliği ile farklı kişilerin konuşmalarının benzerliği ayrı ayrı ölçülür.
İki dağılım arasında net bir boşluk varsa eşik oraya konur; boşluk yoksa canlı geçiş bu
ortamda güvenilmezdir ve bu rapora öyle yazılır.

**Files:**
- Değiştir: `scratchpad/diar-spike/DiarSpike/Program.cs` (`benzerlik` modu eklenir)

**Interfaces:**
- Consumes: Task 1'in doğruladığı `SpeakerEmbeddingExtractor`; Task 2'nin kullandığı kayıt
- Produces: `dotnet run --project scratchpad/diar-spike/DiarSpike -- benzerlik <wav> <referans>`;
  aynı-konuşmacı ve farklı-konuşmacı benzerlik dağılımlarını basar

- [ ] **Adım 1: Benzerlik ölçüm kodunu yaz**

`switch`'e ekleyin:

```csharp
    case "benzerlik": Benzerlik(modelDir, args[1], args[2]); break;
```

ve metodu ekleyin:

```csharp
// referans.txt: her satır "baslangic_sn bitis_sn konusmaci"
// Her referans bölgesinden bir embedding çıkarır, sonra TÜM çiftlerin kosinüs benzerliğini
// aynı-konuşmacı / farklı-konuşmacı diye ikiye ayırır. Aradaki boşluk eşiği belirler.
static void Benzerlik(string modelDir, string wavPath, string refPath)
{
    Console.WriteLine("=== Konuşmacı benzerlik kalibrasyonu ===");
    var cfg = new SherpaOnnx.SpeakerEmbeddingExtractorConfig();
    cfg.Model = Path.Combine(modelDir, "speaker-embedding.onnx");
    var ext = new SherpaOnnx.SpeakerEmbeddingExtractor(cfg);
    var reader = new SherpaOnnx.WaveReader(wavPath);
    Console.WriteLine($"Embedding boyutu: {ext.Dim}, WAV: {reader.SampleRate} Hz");

    var regions = new List<(double start, double end, string spk)>();
    foreach (var line in File.ReadAllLines(refPath))
    {
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) continue;
        regions.Add((double.Parse(p[0]), double.Parse(p[1]), p[2]));
    }
    Console.WriteLine($"Referans bölge sayısı: {regions.Count}");

    var embs = new List<(float[] vec, string spk)>();
    foreach (var (start, end, spk) in regions)
    {
        int a = (int)(start * reader.SampleRate);
        int b = Math.Min((int)(end * reader.SampleRate), reader.Samples.Length);
        if (b - a < reader.SampleRate / 2) { Console.WriteLine($"Atlandı (<0.5sn): {spk} {start}-{end}"); continue; }
        var slice = reader.Samples[a..b];
        var stream = ext.CreateStream();
        stream.AcceptWaveform(reader.SampleRate, slice);
        stream.InputFinished();
        embs.Add((ext.Compute(stream), spk));
    }

    var same = new List<double>(); var diff = new List<double>();
    for (int i = 0; i < embs.Count; i++)
        for (int j = i + 1; j < embs.Count; j++)
        {
            double c = Cosine(embs[i].vec, embs[j].vec);
            (embs[i].spk == embs[j].spk ? same : diff).Add(c);
        }

    Console.WriteLine($"AYNI konuşmacı  ({same.Count} çift): min={same.Min():F3} ort={same.Average():F3} max={same.Max():F3}");
    Console.WriteLine($"FARKLI konuşmacı({diff.Count} çift): min={diff.Min():F3} ort={diff.Average():F3} max={diff.Max():F3}");
    Console.WriteLine($"BOŞLUK (aynı-min − farklı-max) = {same.Min() - diff.Max():F3}");
    Console.WriteLine(same.Min() > diff.Max()
        ? $"AYRILABİLİR — önerilen eşik ≈ {(same.Min() + diff.Max()) / 2:F3}"
        : "ÖRTÜŞÜYOR — bu ortamda canlı geçiş güvenilmez, rapora böyle yazılmalı");
}

static double Cosine(float[] a, float[] b)
{
    double dot = 0, na = 0, nb = 0;
    for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
    return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
}
```

- [ ] **Adım 2: Ölçümü çalıştır**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet run --project scratchpad/diar-spike/DiarSpike -- benzerlik \
  scratchpad/diar-spike/toplanti.wav scratchpad/diar-spike/referans.txt
```

Çıktının tamamını kaydedin. **"ÖRTÜŞÜYOR" çıkarsa bu bir başarısızlık değil, değerli bir
bulgudur** — canlı anlık etiketlemenin (S2'nin ilk yarısı) bu donanımda çalışmayacağı anlamına
gelir ve 10b tasarımı "yalnızca sonda etiketle"ye çekilmelidir. Kullanıcıya sorulur.

- [ ] **Adım 3: Kısa bölge dayanıklılığını ölç**

Canlıda embedding'ler tam 15 saniyelik temiz bölgelerden değil, VAD'ın verdiği kısa
parçalardan çıkacak. Referans bölgelerini kırpıp ölçümü tekrarlayın: `Benzerlik` içindeki
dilim satırını sırayla şu şekilde sınırlayıp üç koşu yapın —

```csharp
        // 1. koşu: b = Math.Min(a + reader.SampleRate * 1, b);   // 1 sn
        // 2. koşu: b = Math.Min(a + reader.SampleRate * 2, b);   // 2 sn
        // 3. koşu: b = Math.Min(a + reader.SampleRate * 4, b);   // 4 sn
```

(satırı `var slice = ...` öncesine ekleyin). Her koşunun BOŞLUK değerini kaydedin:

| Bölge uzunluğu | Aynı (min) | Farklı (maks) | Boşluk | Ayrılabilir mi |
|---|---|---|---|---|
| 1 sn | ? | ? | ? | ? |
| 2 sn | ? | ? | ? | ? |
| 4 sn | ? | ? | ? | ? |
| 15 sn (tam) | ? | ? | ? | ? |

Bu tablo doğrudan 10b'nin `MinSegmentSeconds` değerini belirler: hangi uzunlukta ayrım
güvenilir hale geliyorsa o.

- [ ] **Adım 4: İkinci embedding modeliyle karşılaştır**

Spec'in açık uçlarından biri "hangi embedding modeli" — bu keyfi değil ölçümle seçilir
(master §12.4). Task 1–3'te kullanılan 3D-Speaker ERes2Net modeli Çince veriyle eğitilmiştir;
WeSpeaker CAM++ ise VoxCeleb (çok uluslu İngilizce) ile. Türkçe konuşmada hangisinin daha iyi
ayırdığı **bilinmiyor** — ölçün:

```bash
curl -L -o "$HOME/Library/Application Support/SentimentAnalysis/models/speaker-embedding-campp.onnx" \
  "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_CAM++.onnx"
```

`Benzerlik` metodundaki model satırını geçici olarak yeni modele çevirip (bölge uzunluğu:
tam, kırpma yok) tekrar çalıştırın:

```csharp
    cfg.Model = Path.Combine(modelDir, "speaker-embedding-campp.onnx");
```

```bash
dotnet run --project scratchpad/diar-spike/DiarSpike -- benzerlik \
  scratchpad/diar-spike/toplanti.wav scratchpad/diar-spike/referans.txt
```

| Embedding modeli | Boyut | Aynı (min) | Farklı (maks) | Boşluk |
|---|---|---|---|---|
| 3D-Speaker ERes2Net | ? | ? | ? | ? |
| WeSpeaker CAM++ | ? | ? | ? | ? |

**Boşluğu büyük olan kazanır** ve 10b'nin varsayılan modeli olur. Fark ihmal edilebilirse
(< 0.05) daha küçük olan tercih edilir ve gerekçesi rapora yazılır. Kaybeden modelin dosyası
silinir:

```bash
rm "$HOME/Library/Application Support/SentimentAnalysis/models/speaker-embedding-<kaybeden>.onnx"
```

---

## Task 4: Ölçüm raporu ve GEÇ/GEÇME kararı

**Files:**
- Oluştur: `docs/superpowers/specs/subprojects/10a-diarization-spike-olcumu.md`
- Sil: `scratchpad/diar-spike/`

**Interfaces:**
- Consumes: Task 1, 2, 3'ün kaydedilmiş konsol çıktıları ve tabloları
- Produces: Kalıcı ölçüm raporu; 10b planının dayanacağı sayılar

- [ ] **Adım 1: Raporu yaz**

`docs/superpowers/specs/subprojects/10a-diarization-spike-olcumu.md` oluşturun. **Ölçülen
gerçek değerler** yazılır — beklenen değil. Şu iskeleti doldurun:

```markdown
# 10a Diarization Spike Ölçümü

> Tarih: <gerçek tarih> · Donanım: <gerçek> · .NET <gerçek> · sherpa-onnx <çözülen sürüm>

## Sonuç

**GEÇTİ / KALDI** — <tek cümlelik hüküm>

## 1. Native çakışma

| Yükleme sırası | Microsoft.ML.OnnxRuntime | sherpa-onnx | Whisper.net | Süreç |
|---|---|---|---|---|
| 1→2→3 | ? | ? | ? | ? |
| 2→1→3 | ? | ? | ? | ? |

RSS ilerlemesi: <ölçülen>

## 2. Offline diarization doğruluğu

Kayıt: <süre>, <kişi sayısı> kişi, <ortam tarifi>

| Threshold | Bulunan konuşmacı |
|---|---|
| ... | ... |

En iyi eşik: <değer>
- Konuşmacı sayısı doğru mu: <evet/hayır>
- Sınır isabeti (ortalama sapma): <sn>
- Doğru atanan konuşma oranı: <%>
- Hız: RTF <değer> · Tepe RSS: <MB>

## 3. Online kümeleme kalibrasyonu

| Bölge uzunluğu | Aynı (min) | Farklı (maks) | Boşluk | Ayrılabilir mi |
|---|---|---|---|---|
| ... | ... | ... | ... | ... |

| Embedding modeli | Boyut | Aynı (min) | Farklı (maks) | Boşluk |
|---|---|---|---|---|
| 3D-Speaker ERes2Net | ? | ? | ? | ? |
| WeSpeaker CAM++ | ? | ? | ? | ? |

Seçilen embedding modeli: <hangisi, neden>
Önerilen `SimilarityThreshold`: <değer>
Önerilen `MinSegmentSeconds`: <değer>

## 10b için sonuçlar

- <spec'te değişmesi gereken kararlar>

## Yakalanan tuzaklar

- <ölçüm sırasında yanıltıcı görünen ne varsa>
```

- [ ] **Adım 2: Spec'i ölçümlerle güncelle**

`docs/superpowers/specs/subprojects/10-konusmaci-ayrimi.md` içinde:
- "Fizibilite doğrulaması" bölümündeki "**henüz donanımda ölçülmedi**" ibaresini kaldırıp
  rapora bağlantı verin.
- `DiarizationConfig`'teki `SimilarityThreshold` ve `MinSegmentSeconds` varsayılanlarını
  ölçülen değerlerle değiştirin ve "KALİBRE EDİLMEMİŞ" notunu ölçüm referansıyla değiştirin.
- "Açık uçlar"daki kalibrasyon ve embedding-modeli kalemlerini kapatın (ölçüldüyse).
- Ölçüm S3'ü (sherpa-onnx motoru) veya S2'yi (canlı anlık etiket) geçersiz kıldıysa, ilgili
  karar satırını master D9'daki gibi **üstü çizili + revizyon notu** biçiminde güncelleyin.

- [ ] **Adım 3: Spike artıklarını temizle**

```bash
cd /Users/betulfiliz/sentiment-analysis
rm -rf scratchpad/diar-spike
git status --short
```

Beklenen: yalnızca yeni rapor ve güncellenmiş spec görünsün; `src/` ve `tests/` altında
değişiklik OLMAMALI (Global Kısıt).

- [ ] **Adım 4: Test takımının bozulmadığını doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **296 geçer, 8 atlanır, 0 başarısız, 0 uyarı** — spike ürün koduna dokunmadığı için
sayı değişmemeli.

- [ ] **Adım 5: Kullanıcıya sonucu sun ve DUR**

Rapordaki üç ölçümü ve GEÇ/GEÇME hükmünü kullanıcıya özetleyin. **Commit yalnızca kullanıcı
isteyince yapılır** (proje kuralı). 10b planına, kullanıcı ölçümleri görüp onaylamadan
geçilmez — ölçümler S2/S3 kararlarını değiştirmiş olabilir.

---

## Bu spike'ın cevaplamadığı sorular (bilinçli kapsam dışı)

- **Whisper + diarization aynı chunk'ta birlikte ne kadar sürer** (bütçe) — 10b'nin ilk
  task'ında ölçülür; burada tek tek bileşen hızları yeterli.
- **Örtüşen konuşmada davranış** — kayıt protokolü sırayla konuşmayı şart koşuyor. Örtüşme
  S6 gereği `Bilinmeyen` sayılacağı için spike'ın karar verdiği bir şey değil.
- **İngilizce/çok dilli davranış** — prozodi gibi diarization da dil-agnostiktir; D5 gereği
  şimdilik Türkçe'de doğrulanır.
