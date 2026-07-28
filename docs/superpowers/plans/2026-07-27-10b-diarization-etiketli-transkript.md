# Alt-Proje 10b Uygulama Planı — Diarization + Etiketli Transkript

> **Agentic worker'lar için:** GEREKLİ ALT-BECERİ: Bu planı task-task uygulamak için
> `superpowers:subagent-driven-development` (önerilen) veya `superpowers:executing-plans`
> kullanın. Adımlar takip için checkbox (`- [ ]`) sözdizimi kullanır.

**Hedef:** Bir kaydın (canlı oturum veya yüklenen dosya) transkriptini konuşmacıya göre
ayrıştırmak — raporda `Konuşmacı A: … / Konuşmacı B: …` görünsün. Duygu analizi bu turda
**konuşmacı başına ayrılmaz** (o 10c'dir); bu tur etiketli transkripti teslim eder.

**Architecture:** Diarization tek seferlik bir **arka plan geçişidir**, canlı hattın içinde
değil. Canlı akış (08e'de doğrulanmış `EmotionPipeline`) hiç değişmez; oturum bitince ses
saklanır, arka planda sherpa-onnx tüm kaydı diarize eder, saf bir hizalayıcı zaman damgalı
transkript segmentlerini konuşmacı bölgeleriyle eşler ve rapor kendini günceller.

**Tech Stack:** .NET 9 · sherpa-onnx 1.13.4 (`org.k2fsa.sherpa.onnx`) · SQLite
(Microsoft.Data.Sqlite 9.0.0) · Avalonia 11.3.1 · xUnit 2.9.2 + Xunit.SkippableFact

**Spec:** [`10-konusmaci-ayrimi.md`](../specs/subprojects/10-konusmaci-ayrimi.md)
**Ölçüm:** [`10a-diarization-spike-olcumu.md`](../specs/subprojects/10a-diarization-spike-olcumu.md)

---

## Global Kısıtlar

Her task'ın gereksinimleri örtük olarak bu bölümü içerir.

- **Test kapısı:** Her task `dotnet build && dotnet test` ile biter. Başlangıç: **296 geçer,
  8 atlanır, 0 başarısız, 0 uyarı.** Bu sayı düşmez, yeni uyarı eklenmez.
- **Ölçülmüş sabitler — tahmin değil** (10a raporundan, aynen kullanılacak):
  - `ClusteringThreshold = 0.8` · `MinSegmentSeconds = 4.0` · `NumClusters = 0` (bilinmiyor)
  - Segmentation: `sherpa-onnx-pyannote-segmentation-3-0/model.onnx` (**fp32**; int8 fayda vermiyor)
  - Embedding: `speaker-embedding.onnx` (3D-Speaker ERes2Net, 512 boyut)
  - Beklenen: RTF ~0,44 · ~390 MB RSS · ~%93 doğruluk (2 kişi, uzak alan)
- **Yerel ayar tuzağı:** sayı ayrıştıran her yerde `CultureInfo.InvariantCulture` kullanılır.
  10a'da `float.Parse("0.5")` Türkçe kültürde **5,00** üretip ölçümü sessizce bozmuştu.
- **Canlı hat korunur:** `EmotionPipeline`'ın kanal/drenaj/backpressure mantığına
  DOKUNULMAZ (08e'de gerçek donanımda doğrulandı).
- **Additive genişletme:** `AppConfig` ve SQLite şeması yalnızca varsayılanlı/ALTER ile
  büyür; eski veritabanları kırılmaz (08d deseni).
- **Özellik varsayılan KAPALI** (`DiarizationConfig.Enabled = false`). Kapalıyken davranış
  bugünküyle bit-bit aynı olmalıdır.
- **Gerçek-model testleri env-gated:** `[SkippableFact]` + `Skip.If(...)`, `DIAR_TEST=1`.
  Sahte-yeşil test yasak.
- **Ses embedding'i diske YAZILMAZ** (S4, kalıcı biyometri yok).
- **Son task canlı doğrulamadır** — kullanıcı gerçek bir kayıtta görmeden 10b bitmez (08e dersi).

---

## Dosya Yapısı

| Dosya | Sorumluluk | Task |
|---|---|---|
| `src/SentimentAnalysis.Core/Signals.cs` | `TranscriptSegment` + `Transcript.Segments` | 1 |
| `src/SentimentAnalysis.Infrastructure.Stt/TranscriptAssembler.cs` | Segmentleri koru (indirgeme değil) | 1 |
| `src/SentimentAnalysis.Core/Results.cs` | `ChunkResult.Segments` (mutlak zamanlı) | 2 |
| `src/SentimentAnalysis.Application/Pipeline/ChunkProcessor.cs` | Chunk-göreli → mutlak zaman kaydırma | 2 |
| `src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs` | `transcript_segments` + `speaker_segments` tabloları | 3 |
| `src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs` | Segment yazma/okuma + migration | 3 |
| `src/SentimentAnalysis.Desktop/LiveViewModel.cs` | Oturum sesini kaydet (engelleyici ön koşul) | 4 |
| `src/SentimentAnalysis.Core/Speaker.cs` | `SpeakerId`, `SpeakerSegment` | 5 |
| `src/SentimentAnalysis.Application/Abstractions/Interfaces.cs` | `IDiarizer` | 5 |
| `src/SentimentAnalysis.Application/Diarization/SpeakerAligner.cs` | Saf hizalama (kalitenin kazanıldığı yer) | 5 |
| `src/SentimentAnalysis.Infrastructure.Diarization/` | **Yeni proje** — sherpa-onnx sarmalayıcı | 6 |
| `src/SentimentAnalysis.Application/Diarization/DiarizationJob.cs` | Arka plan geçişi orkestrasyonu | 7 |
| `src/SentimentAnalysis.Desktop/ReportViewModel.cs` | Etiketli transkript + durum göstergesi | 8 |

---

## Task 1: Transkript zaman damgalarını koru

Konuşmacı etiketlemesi zaman damgalı metin ister. Bugün `TranscriptAssembler.Assemble`
Whisper'ın `SttSegment`'lerini tek bir string'e indirgeyip zamanları **atıyor** — hizalanacak
hiçbir şey kalmıyor. Bu task o bilgiyi korur; başka hiçbir davranışı değiştirmez.

**Files:**
- Değiştir: `src/SentimentAnalysis.Core/Signals.cs:16`
- Değiştir: `src/SentimentAnalysis.Infrastructure.Stt/TranscriptAssembler.cs`
- Test: `tests/SentimentAnalysis.Infrastructure.Stt.Tests/TranscriptAssemblerTests.cs`

**Interfaces:**
- Consumes: mevcut `SttSegment(string Text, TimeSpan Start, TimeSpan End, double Probability, string LanguageCode)`
- Produces: `TranscriptSegment(string Text, TimeSpan Start, TimeSpan End)` ve
  `Transcript.Segments` (`IReadOnlyList<TranscriptSegment>`, varsayılan boş liste)

- [ ] **Adım 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Stt.Tests/TranscriptAssemblerTests.cs` dosyasına ekleyin:

```csharp
    [Fact]
    public void Assemble_ZamanDamgalariniKorur_MetniDegistirmeden()
    {
        var segments = new[]
        {
            new SttSegment("Merhaba", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2), 0.9, "tr"),
            new SttSegment("nasılsın", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), 0.8, "tr"),
        };

        var t = TranscriptAssembler.Assemble(segments, forced: null);

        Assert.Equal("Merhaba nasılsın", t.Text);          // mevcut davranış korunur
        Assert.Equal(2, t.Segments.Count);
        Assert.Equal("Merhaba", t.Segments[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(0), t.Segments[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2), t.Segments[0].End);
        Assert.Equal("nasılsın", t.Segments[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(4), t.Segments[1].End);
    }

    [Fact]
    public void Assemble_BosSegmentListesi_BosSegmentDondurur()
    {
        var t = TranscriptAssembler.Assemble(Array.Empty<SttSegment>(), forced: null);
        Assert.Empty(t.Segments);
        Assert.Equal(string.Empty, t.Text);
    }

    [Fact]
    public void Assemble_BosMetinliSegmentiAtar_MetindeDeSegmenttteDe()
    {
        var segments = new[]
        {
            new SttSegment("  ", TimeSpan.Zero, TimeSpan.FromSeconds(1), 0.5, "tr"),
            new SttSegment("evet", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0.9, "tr"),
        };

        var t = TranscriptAssembler.Assemble(segments, forced: null);

        Assert.Equal("evet", t.Text);
        Assert.Single(t.Segments);                          // boş metinli segment taşınmaz
        Assert.Equal("evet", t.Segments[0].Text);
    }
```

- [ ] **Adım 2: Testin başarısız olduğunu doğrula**

```bash
dotnet test tests/SentimentAnalysis.Infrastructure.Stt.Tests --filter "TranscriptAssemblerTests"
```

Beklenen: derleme hatası — `Transcript` bir `Segments` üyesi içermiyor.

- [ ] **Adım 3: `TranscriptSegment` tipini ekle**

`src/SentimentAnalysis.Core/Signals.cs` içinde `Transcript` tanımını bulun ve ŞÖYLE değiştirin:

```csharp
/// <summary>Transkriptin zaman damgalı bir parçası. Konuşmacı ayrımı (10b) bu zamanları
/// konuşmacı bölgeleriyle hizalar; onsuz transkript kime ait olduğu bilinmeyen düz metindir.</summary>
public sealed record TranscriptSegment(string Text, TimeSpan Start, TimeSpan End);

/// <summary>STT çıktısı. <paramref name="Segments"/> additive'dir (varsayılan boş) — 10b
/// öncesi çağıranlar etkilenmez.</summary>
public sealed record Transcript(
    string Text,
    DetectedLanguage Language,
    double AvgLogProb,
    IReadOnlyList<TranscriptSegment>? Segments = null)
{
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = Segments ?? Array.Empty<TranscriptSegment>();
}
```

`Signals.cs` dosyasının başında `using System;` ve `using System.Collections.Generic;`
bulunduğundan emin olun.

- [ ] **Adım 4: `TranscriptAssembler`'ı segmentleri taşıyacak şekilde güncelle**

`TranscriptAssembler.Assemble` içinde, `text` hesaplanan bloğu şununla değiştirin:

```csharp
        var kept = segments
            .Select(s => new { Trimmed = s.Text.Trim(), s.Start, s.End })
            .Where(x => x.Trimmed.Length > 0)
            .ToList();

        var text = string.Join(" ", kept.Select(x => x.Trimmed));
        var transcriptSegments = kept
            .Select(x => new TranscriptSegment(x.Trimmed, x.Start, x.End))
            .ToList();
```

ve `return` satırını şununla değiştirin:

```csharp
        return new Transcript(text, language, avgLogProb, transcriptSegments);
```

Dosyanın başında `using System.Linq;` olduğundan emin olun.

- [ ] **Adım 5: Testlerin geçtiğini doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **299 geçer** (3 yeni test), 8 atlanır, 0 başarısız, 0 uyarı.

- [ ] **Adım 6: Commit**

```bash
git add src/SentimentAnalysis.Core/Signals.cs \
        src/SentimentAnalysis.Infrastructure.Stt/TranscriptAssembler.cs \
        tests/SentimentAnalysis.Infrastructure.Stt.Tests/TranscriptAssemblerTests.cs
git commit -m "feat(stt): transkript segmentlerinin zaman damgalarını koru (10b ön koşulu)"
```

---

## Task 2: Segmentleri mutlak zamana çevirip `ChunkResult`'a taşı

Whisper'ın segment zamanları **chunk'a görelidir** (her chunk'ta 0'dan başlar). Diarization ise
tüm kaydı gördüğü için **mutlak** zaman üretir. İkisi hizalanacağından biri diğerine
çevrilmelidir; kaydırmayı burada, kaynağa en yakın yerde yaparız. Bu, sessizce yanlış hizalama
üretebilecek bir tuzaktır — testle kilitlenir.

**Files:**
- Değiştir: `src/SentimentAnalysis.Core/Results.cs:30-34`
- Değiştir: `src/SentimentAnalysis.Application/Pipeline/ChunkProcessor.cs:38`
- Test: `tests/SentimentAnalysis.Application.Tests/Pipeline/ChunkProcessorTests.cs`

**Interfaces:**
- Consumes: Task 1'in `Transcript.Segments`'i (chunk-göreli zamanlar)
- Produces: `ChunkResult.Segments` (`IReadOnlyList<TranscriptSegment>`, **mutlak** zamanlar)

- [ ] **Adım 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Application.Tests/Pipeline/ChunkProcessorTests.cs` dosyasına ekleyin
(dosyadaki mevcut sahte (fake) sınıfları kullanın; STT sahtesinin segment döndürmesi gerekir):

```csharp
    [Fact]
    public async Task ProcessAsync_SegmentZamanlariniMutlakaCevirir()
    {
        // chunk 30. saniyede başlıyor; STT segmentleri chunk'a göreli (0 ve 2 sn)
        var chunk = new AudioChunk(new float[16000], 16000,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40));

        var transcript = new Transcript("Merhaba dünya", DetectedLanguage.Turkish, -0.2,
            new[]
            {
                new TranscriptSegment("Merhaba", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2)),
                new TranscriptSegment("dünya",   TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
            });

        var proc = new ChunkProcessor(
            new AlwaysSpeechVad(), new FixedStt(transcript), new FixedProsody(),
            new FixedTextEmotion(), new PassThroughFusion());

        var result = await proc.ProcessAsync(chunk, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Segments[0].Start);  // 30 + 0
        Assert.Equal(TimeSpan.FromSeconds(32), result.Segments[0].End);    // 30 + 2
        Assert.Equal(TimeSpan.FromSeconds(32), result.Segments[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(34), result.Segments[1].End);
        Assert.Equal("Merhaba dünya", result.Transcript);                  // metin değişmedi
    }
```

Dosyada gerekli sahteler yoksa ekleyin:

```csharp
    private sealed class AlwaysSpeechVad : IVadGate { public bool HasSpeech(AudioChunk c) => true; }

    private sealed class FixedStt(Transcript t) : ISpeechToText
    {
        public Task<Transcript> TranscribeAsync(AudioChunk c, CancellationToken ct) => Task.FromResult(t);
    }

    private sealed class FixedProsody : IProsodyEmotionModel
    {
        public Task<ProsodyEmotion> AnalyzeAsync(AudioChunk c, CancellationToken ct)
            => Task.FromResult(new ProsodyEmotion(ValenceArousal.Neutral, 0.5));
    }

    private sealed class FixedTextEmotion : ITextEmotionModel
    {
        public Task<TextEmotion> AnalyzeAsync(Transcript t, CancellationToken ct)
            => Task.FromResult(new TextEmotion(ValenceArousal.Neutral, 0.5));
    }

    private sealed class PassThroughFusion : IEmotionFusion
    {
        public FusedResult Fuse(TextEmotion t, ProsodyEmotion p, QualitySignals q)
            => new(ValenceArousal.Neutral, Emotion.Notr, Emotion.Notr, Confidence.Orta,
                   true, false, IncongruencePattern.Yok, 1.0,
                   ValenceArousal.Neutral, ValenceArousal.Neutral, 0, 0);
    }
```

- [ ] **Adım 2: Testin başarısız olduğunu doğrula**

```bash
dotnet test tests/SentimentAnalysis.Application.Tests --filter "ProcessAsync_SegmentZamanlariniMutlakaCevirir"
```

Beklenen: derleme hatası — `ChunkResult` bir `Segments` üyesi içermiyor.

- [ ] **Adım 3: `ChunkResult`'a alanı ekle**

`src/SentimentAnalysis.Core/Results.cs` içinde `ChunkResult` tanımını değiştirin:

```csharp
/// <summary>Bir chunk'ın tam sonucu (zaman çizelgesi öğesi).
/// <paramref name="Segments"/> MUTLAK zamanlıdır (chunk başlangıcı eklenmiş) — konuşmacı
/// hizalaması (10b) buna dayanır. Additive: varsayılan boş liste.</summary>
public sealed record ChunkResult(
    TimeSpan TimeStart,
    TimeSpan TimeEnd,
    FusedResult Fused,
    string Transcript,
    IReadOnlyList<TranscriptSegment>? Segments = null)
{
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = Segments ?? Array.Empty<TranscriptSegment>();
}
```

`Results.cs` başında `using System;` bulunduğundan emin olun.

- [ ] **Adım 4: `ChunkProcessor`'da kaydırmayı uygula**

`ChunkProcessor.ProcessAsync`'in son satırını değiştirin:

```csharp
        // Whisper segmentleri chunk'a GÖRELİ gelir; diarization mutlak zaman üretir.
        // Kaydırmayı burada, kaynağa en yakın yerde yaparız.
        var absolute = transcript.Segments
            .Select(s => new TranscriptSegment(s.Text, chunk.Start + s.Start, chunk.Start + s.End))
            .ToList();

        return new ChunkResult(chunk.Start, chunk.End, fused, transcript.Text, absolute);
```

Dosyanın başına `using System.Linq;` ekleyin.

- [ ] **Adım 5: Testlerin geçtiğini doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **300 geçer**, 8 atlanır, 0 başarısız, 0 uyarı.

- [ ] **Adım 6: Commit**

```bash
git add src/SentimentAnalysis.Core/Results.cs \
        src/SentimentAnalysis.Application/Pipeline/ChunkProcessor.cs \
        tests/SentimentAnalysis.Application.Tests/Pipeline/ChunkProcessorTests.cs
git commit -m "feat(pipeline): chunk sonuçları mutlak zamanlı transkript segmentleri taşısın"
```

---

## Task 3: Segment ve konuşmacı tablolarını sakla

**Files:**
- Değiştir: `src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs`
- Değiştir: `src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs`
- Test: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/SqliteSessionStoreTests.cs`

**Interfaces:**
- Consumes: `ChunkResult.Segments` (Task 2)
- Produces: `SqliteSessionStore.SaveSpeakerSegmentsAsync(Guid, IReadOnlyList<SpeakerSegment>, CancellationToken)`
  ve `GetTranscriptSegmentsAsync(Guid, CancellationToken)` → `IReadOnlyList<TranscriptSegment>`

> `SpeakerSegment` tipi Task 5'te tanımlanır. Bu task yalnızca **transkript segmentlerini**
> saklar; konuşmacı tablosu şemada oluşturulur ama yazma/okuma Task 7'de bağlanır. Bu bilinçli:
> şema değişikliği tek migration turunda toplanır.

- [ ] **Adım 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Storage.Tests/SqliteSessionStoreTests.cs` dosyasına:

```csharp
    [Fact]
    public async Task SaveChunkAsync_TranskriptSegmentleriniSaklar_VeGeriOkur()
    {
        using var tmp = new TempDb();
        var store = tmp.Store;
        var id = Guid.NewGuid();

        var chunk = new ChunkResult(
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40), Fused(), "Merhaba dünya",
            new[]
            {
                new TranscriptSegment("Merhaba", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(32)),
                new TranscriptSegment("dünya",   TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(34)),
            });

        await store.SaveChunkAsync(id, chunk, CancellationToken.None);

        var segs = await store.GetTranscriptSegmentsAsync(id, CancellationToken.None);

        Assert.Equal(2, segs.Count);
        Assert.Equal("Merhaba", segs[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(30), segs[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(34), segs[1].End);
    }

    [Fact]
    public async Task GetTranscriptSegmentsAsync_SegmentsizOturum_BosDondurur()
    {
        using var tmp = new TempDb();
        var id = Guid.NewGuid();
        await tmp.Store.SaveChunkAsync(id,
            new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(10), Fused(), "metin"),
            CancellationToken.None);

        Assert.Empty(await tmp.Store.GetTranscriptSegmentsAsync(id, CancellationToken.None));
    }
```

> Dosyada `TempDb` ve `Fused()` yardımcıları zaten varsa onları kullanın; yoksa dosyadaki
> mevcut testlerin veritabanı kurulum desenini birebir izleyerek ekleyin.

- [ ] **Adım 2: Testin başarısız olduğunu doğrula**

```bash
dotnet test tests/SentimentAnalysis.Infrastructure.Storage.Tests --filter "TranskriptSegmentleriniSaklar"
```

Beklenen: derleme hatası — `GetTranscriptSegmentsAsync` yok.

- [ ] **Adım 3: Şemayı genişlet**

`StorageSchema.CreateSql` içindeki `chunks` tablosundan SONRA, kapanış `"""` işaretinden ÖNCE
şunları ekleyin:

```sql
        CREATE TABLE IF NOT EXISTS transcript_segments (
            session_id TEXT NOT NULL,
            seq INTEGER NOT NULL,               -- oturum içi sıra
            start_ms INTEGER NOT NULL,          -- MUTLAK zaman
            end_ms INTEGER NOT NULL,
            text TEXT NOT NULL,
            PRIMARY KEY (session_id, seq)
        );
        CREATE TABLE IF NOT EXISTS speaker_segments (
            session_id TEXT NOT NULL,
            seq INTEGER NOT NULL,
            start_ms INTEGER NOT NULL,
            end_ms INTEGER NOT NULL,
            speaker_index INTEGER NOT NULL,     -- -1 = Bilinmeyen
            PRIMARY KEY (session_id, seq)
        );
```

Ayrıca sınıfın altına, 08d desenini izleyen bir not ekleyin:

```csharp
    /// <summary>10b ile eklenen tablolar. `CREATE TABLE IF NOT EXISTS` her açılışta çalıştığı
    /// için eski veritabanlarında da kendiliğinden oluşurlar — ALTER gerekmez (08d'deki sütun
    /// ekleme durumundan farkı budur).</summary>
    public static readonly string[] TablesAddedIn10b = ["transcript_segments", "speaker_segments"];
```

- [ ] **Adım 4: Yazma ve okumayı uygula**

`SqliteSessionStore.SaveChunkAsync` içinde, chunk `INSERT`'inden SONRA (metodun sonuna,
`await cmd.ExecuteNonQueryAsync(...)` satırından sonra) ekleyin:

```csharp
        // Transkript segmentleri (10b): konuşmacı hizalamasının hammaddesi.
        foreach (var seg in r.Segments)
        {
            await using var s = conn.CreateCommand();
            s.CommandText = """
                INSERT INTO transcript_segments (session_id, seq, start_ms, end_ms, text)
                VALUES ($sid,
                    (SELECT COALESCE(MAX(seq), -1) + 1 FROM transcript_segments WHERE session_id = $sid),
                    $st, $en, $tx)
                """;
            s.Parameters.AddWithValue("$sid", sessionId.ToString("N"));
            s.Parameters.AddWithValue("$st", (long)seg.Start.TotalMilliseconds);
            s.Parameters.AddWithValue("$en", (long)seg.End.TotalMilliseconds);
            s.Parameters.AddWithValue("$tx", seg.Text);
            await s.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
```

Sınıfa okuma metodunu ekleyin:

```csharp
    /// <summary>Oturumun zaman damgalı transkript segmentleri (sıralı). ISessionStore dışı ek yetenek.</summary>
    public async Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid sessionId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT start_ms, end_ms, text FROM transcript_segments WHERE session_id = $id ORDER BY seq";
        cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));

        var list = new List<TranscriptSegment>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new TranscriptSegment(
                reader.GetString(2),
                TimeSpan.FromMilliseconds(reader.GetInt64(0)),
                TimeSpan.FromMilliseconds(reader.GetInt64(1))));
        return list;
    }
```

- [ ] **Adım 5: Testlerin geçtiğini doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **302 geçer**, 8 atlanır, 0 başarısız, 0 uyarı.

- [ ] **Adım 6: Commit**

```bash
git add src/SentimentAnalysis.Infrastructure.Storage/ tests/SentimentAnalysis.Infrastructure.Storage.Tests/
git commit -m "feat(storage): transkript ve konuşmacı segment tabloları"
```

---

## Task 4: Canlı oturum sesini sakla (ENGELLEYİCİ ÖN KOŞUL)

`SqliteSessionStore.SaveAudioAsync` yazılmış ve testli, ama **üretimde hiçbir yerden
çağrılmıyor** — yalnızca depolama testleri kullanıyor. Sonuç: canlı oturumlarda
`sessions.audio_path` daima `NULL` ve arka plan diarization'ının işleyecek sesi yok.
Bu task o bağlantıyı kurar.

**Files:**
- Oluştur: `src/SentimentAnalysis.Infrastructure.Audio/RecordingCaptureSource.cs`
- Değiştir: `src/SentimentAnalysis.Desktop.Avalonia/PipelineAnalysisRunner.cs`
- Test: `tests/SentimentAnalysis.Infrastructure.Audio.Tests/RecordingCaptureSourceTests.cs`

**Interfaces:**
- Consumes: mevcut `IAudioCaptureSource`, `SqliteSessionStore.SaveAudioAsync(Guid, byte[], CancellationToken)`
- Produces: `RecordingCaptureSource(IAudioCaptureSource inner, int maxSamples) : IAudioCaptureSource`
  ile `byte[] GetPcm16()` ve `bool LimitReached`

> **Neden dekoratör?** `LiveViewModel` ham PCM'i hiç görmez — yalnızca `IProgress<ChunkResult>`
> alır. `PipelineAnalysisRunner`'ın ise birim test projesi yoktur. Yakalama kaynağını saran bir
> dekoratör her ikisini de çözer: `Infrastructure.Audio` içinde yaşar (test projesi VAR),
> `EmotionPipeline`'a hiç dokunmaz ve chunk'lar zaten oradan akar.

> **İPTAL SÖZLEŞMESİ (kritik):** `IAudioCaptureSource` üzerinde belgelenmiş sözleşme —
> implementasyonlar iptal anında kısa sürede dönmeli, **asla sonsuza dek bloklamamalıdır**;
> `EmotionPipeline` üreticiyi `finally` içinde zaman aşımsız bekler. Dekoratör iç kaynağı
> **saydam biçimde** geçirmeli, iptal davranışını değiştirmemelidir (`await foreach` ile pass-through,
> ek `try/catch` yok). Bu kural bozulursa hiçbir test yakalamaz, uygulama "Durdur"da asılı kalır.

- [ ] **Adım 1: Başarısız testleri yaz**

`tests/SentimentAnalysis.Infrastructure.Audio.Tests/RecordingCaptureSourceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Audio;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public class RecordingCaptureSourceTests
{
    private sealed class FakeSource(int count) : IAudioCaptureSource
    {
        public async IAsyncEnumerable<AudioChunk> CaptureAsync(
            CaptureOptions o, [EnumeratorCancellation] CancellationToken ct)
        {
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var samples = new float[1600];
                Array.Fill(samples, 0.5f);
                yield return new AudioChunk(samples, 16000,
                    TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i + 1));
                await Task.Yield();
            }
        }
    }

    private static readonly CaptureOptions Opts = new(TimeSpan.FromSeconds(1), TimeSpan.Zero);

    private static async Task<int> DrainAsync(IAudioCaptureSource src)
    {
        int n = 0;
        await foreach (var _ in src.CaptureAsync(Opts, CancellationToken.None)) n++;
        return n;
    }

    [Fact]
    public async Task Chunklari_DegistirmedenGecirir()
    {
        var src = new RecordingCaptureSource(new FakeSource(5), maxSamples: 1_000_000);
        Assert.Equal(5, await DrainAsync(src));
    }

    [Fact]
    public async Task Pcm16_BiriktirilenOrnekleriDondurur()
    {
        var src = new RecordingCaptureSource(new FakeSource(3), maxSamples: 1_000_000);
        await DrainAsync(src);

        var pcm = src.GetPcm16();
        Assert.Equal(3 * 1600 * 2, pcm.Length);      // örnek başına 2 bayt
        Assert.False(src.LimitReached);
    }

    [Fact]
    public async Task UstSinirAsilirsa_BiriktirmeDurur_VeIsaretlenir()
    {
        // 3 chunk × 1600 örnek = 4800; sınır 3200 → yalnızca ilk 2 chunk sığar
        var src = new RecordingCaptureSource(new FakeSource(3), maxSamples: 3200);
        int passed = await DrainAsync(src);

        Assert.Equal(3, passed);                      // chunk akışı KESİLMEZ
        Assert.True(src.LimitReached);
        Assert.Equal(3200 * 2, src.GetPcm16().Length);
    }

    [Fact]
    public async Task HicChunkYoksa_BosPcmDondurur()
    {
        var src = new RecordingCaptureSource(new FakeSource(0), maxSamples: 1000);
        await DrainAsync(src);
        Assert.Empty(src.GetPcm16());
    }

    [Fact]
    public async Task Iptal_IcKaynagınDavranisiniDegistirmez()
    {
        var src = new RecordingCaptureSource(new FakeSource(100), maxSamples: 1_000_000);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in src.CaptureAsync(Opts, cts.Token)) cts.Cancel();
        });
    }
}
```

- [ ] **Adım 2: Testlerin başarısız olduğunu doğrula**

```bash
dotnet test tests/SentimentAnalysis.Infrastructure.Audio.Tests --filter "RecordingCaptureSourceTests"
```

Beklenen: derleme hatası — `RecordingCaptureSource` yok.

- [ ] **Adım 3: Dekoratörü uygula**

`src/SentimentAnalysis.Infrastructure.Audio/RecordingCaptureSource.cs`:

```csharp
using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Bir <see cref="IAudioCaptureSource"/>'u sarıp akan chunk'ların PCM'ini biriktirir
/// (10b: arka plan diarization'ın girdisi). Chunk akışını DEĞİŞTİRMEZ ve iptal davranışını
/// olduğu gibi geçirir — <see cref="IAudioCaptureSource"/> üzerindeki iptal sözleşmesi gereği
/// burada ek bir try/catch veya bekleme OLMAMALIDIR.
/// <para>Biriktirme <paramref name="maxSamples"/> ile sınırlıdır: 16 kHz mono PCM16 ≈ 115 MB/saat,
/// sınırsız biriktirme M1 8GB'da uygulamayı şişirir. Sınır aşılırsa chunk akışı sürer ama
/// biriktirme durur ve <see cref="LimitReached"/> true olur — sessizce kırpma yerine görünür
/// bir durum (master §12.2).</para></summary>
public sealed class RecordingCaptureSource(IAudioCaptureSource inner, int maxSamples) : IAudioCaptureSource
{
    private readonly List<float> _pcm = new();

    /// <summary>Üst sınır aşıldığı için sesin bir kısmı biriktirilmedi mi?</summary>
    public bool LimitReached { get; private set; }

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in inner.CaptureAsync(options, ct).ConfigureAwait(false))
        {
            Accumulate(chunk);
            yield return chunk;
        }
    }

    private void Accumulate(AudioChunk chunk)
    {
        if (LimitReached) return;
        int room = maxSamples - _pcm.Count;
        if (room <= 0) { LimitReached = true; return; }
        if (chunk.Samples.Length > room)
        {
            _pcm.AddRange(chunk.Samples.Take(room));
            LimitReached = true;
            return;
        }
        _pcm.AddRange(chunk.Samples);
    }

    /// <summary>Biriktirilen sesi 16-bit little-endian PCM olarak döndürür.</summary>
    public byte[] GetPcm16()
    {
        var bytes = new byte[_pcm.Count * 2];
        for (int i = 0; i < _pcm.Count; i++)
        {
            short s = (short)Math.Clamp(_pcm[i] * 32768f, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }
}
```

- [ ] **Adım 4: Runner'da bağla**

`PipelineAnalysisRunner.RunAsync` içinde, seçili yakalama kaynağını dekoratöre sarın:

```csharp
        int maxSamples = _config.Diarization.MaxRecordedMinutes * 60 * 16000;
        var recording = new RecordingCaptureSource(source, maxSamples);
        var summary = await _pipeline.RunAsync(recording, options, sessionId, startedAt, language, progress, ct)
            .ConfigureAwait(false);

        // Özet ÜRETİLDİKTEN SONRA saklanır — rapor bu yüzden gecikmez (spec 10 S2).
        var pcm = recording.GetPcm16();
        if (pcm.Length > 0 && _config.Diarization.Enabled)
            await _store.SaveAudioAsync(sessionId, pcm, CancellationToken.None).ConfigureAwait(false);
```

`_store` olarak `SqliteSessionStore` (veya onu saran arayüz) DI'dan alınır. **Özellik
kapalıyken ses saklanmaz** — gizlilik varsayılanı (master §12.5): kullanıcı istemediği hâlde
sesini diske yazmayız.

- [ ] **Adım 5: Testlerin geçtiğini doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **307 geçer** (5 yeni test), 8 atlanır, 0 başarısız, 0 uyarı.

- [ ] **Adım 6: Commit**

```bash
git add src tests
git commit -m "feat(desktop,storage): canlı oturum sesi saklansın (diarization ön koşulu)"
```

---

## Task 5: `SpeakerId`, `SpeakerSegment` ve saf hizalayıcı

Bu task'ta ML yok — özelliğin kalitesi burada kazanılır. `SpeakerAligner` zaman damgalı
transkript segmentlerini konuşmacı bölgeleriyle eşler ve tüm kenar durumları burada karara
bağlanır.

**Files:**
- Oluştur: `src/SentimentAnalysis.Core/Speaker.cs`
- Değiştir: `src/SentimentAnalysis.Application/Abstractions/Interfaces.cs`
- Oluştur: `src/SentimentAnalysis.Application/Diarization/SpeakerAligner.cs`
- Test: `tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerAlignerTests.cs`

**Interfaces:**
- Consumes: `TranscriptSegment` (Task 1)
- Produces:
  - `SpeakerId(int Index)` — `Unknown = new(-1)`, `DefaultLabel`
  - `SpeakerSegment(SpeakerId Speaker, TimeSpan Start, TimeSpan End)`
  - `IDiarizer.DiarizeAsync(float[] mono16k, CancellationToken)` → `IReadOnlyList<SpeakerSegment>`
  - `SpeakerAligner.Align(IReadOnlyList<TranscriptSegment>, IReadOnlyList<SpeakerSegment>, double minOverlapRatio)`
    → `IReadOnlyList<(TranscriptSegment Segment, SpeakerId Speaker)>`

- [ ] **Adım 1: Başarısız testleri yaz**

`tests/SentimentAnalysis.Application.Tests/Diarization/SpeakerAlignerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Application.Tests.Diarization;

public class SpeakerAlignerTests
{
    private static TranscriptSegment T(double a, double b, string text = "x") =>
        new(text, TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    private static SpeakerSegment S(int spk, double a, double b) =>
        new(new SpeakerId(spk), TimeSpan.FromSeconds(a), TimeSpan.FromSeconds(b));

    [Fact]
    public void Align_TamOrtusme_DogruKonusmaciyaAtar()
    {
        var result = SpeakerAligner.Align(
            [T(0, 5), T(6, 10)],
            [S(0, 0, 5), S(1, 6, 10)],
            minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(0), result[0].Speaker);
        Assert.Equal(new SpeakerId(1), result[1].Speaker);
    }

    [Fact]
    public void Align_IkiKonusmaciyaYayilanSegment_EnCokOrtusenKazanir()
    {
        // segment 0-10; konuşmacı 0 ile 3 sn, konuşmacı 1 ile 7 sn örtüşüyor
        var result = SpeakerAligner.Align(
            [T(0, 10)],
            [S(0, 0, 3), S(1, 3, 10)],
            minOverlapRatio: 0.5);

        Assert.Equal(new SpeakerId(1), result[0].Speaker);
    }

    [Fact]
    public void Align_HicOrtusmeYok_BilinmeyenDondurur()
    {
        var result = SpeakerAligner.Align(
            [T(0, 5)],
            [S(0, 100, 110)],
            minOverlapRatio: 0.5);

        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
    }

    [Fact]
    public void Align_OrtusmeEsikAltinda_BilinmeyenDondurur()
    {
        // segment 0-10; yalnızca 2 sn örtüşme = %20 < %50 eşik
        var result = SpeakerAligner.Align(
            [T(0, 10)],
            [S(0, 8, 12)],
            minOverlapRatio: 0.5);

        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
    }

    [Fact]
    public void Align_KonusmaciBolgesiYok_HepsiBilinmeyen()
    {
        var result = SpeakerAligner.Align([T(0, 5), T(5, 10)], [], minOverlapRatio: 0.5);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(SpeakerId.Unknown, r.Speaker));
    }

    [Fact]
    public void Align_SifirUzunlukluSegment_BilinmeyenDondurur_BolmeHatasiYok()
    {
        var result = SpeakerAligner.Align([T(5, 5)], [S(0, 0, 10)], minOverlapRatio: 0.5);

        Assert.Equal(SpeakerId.Unknown, result[0].Speaker);
    }

    [Fact]
    public void Align_MetinVeSirayiKorur()
    {
        var result = SpeakerAligner.Align(
            [T(0, 5, "merhaba"), T(6, 10, "dünya")],
            [S(0, 0, 5), S(1, 6, 10)],
            minOverlapRatio: 0.5);

        Assert.Equal("merhaba", result[0].Segment.Text);
        Assert.Equal("dünya", result[1].Segment.Text);
    }

    [Fact]
    public void Align_UcKonusmaciyaYayilan_EnCokOrtusenKazanir()
    {
        var result = SpeakerAligner.Align(
            [T(0, 12)],
            [S(0, 0, 3), S(1, 3, 8), S(2, 8, 12)],
            minOverlapRatio: 0.3);

        Assert.Equal(new SpeakerId(1), result[0].Speaker);   // 5 sn > 4 sn > 3 sn
    }
}
```

- [ ] **Adım 2: Testlerin başarısız olduğunu doğrula**

```bash
dotnet test tests/SentimentAnalysis.Application.Tests --filter "SpeakerAlignerTests"
```

Beklenen: derleme hatası — tipler yok.

- [ ] **Adım 3: Core tiplerini oluştur**

`src/SentimentAnalysis.Core/Speaker.cs`:

```csharp
namespace SentimentAnalysis.Core;

/// <summary>Oturum-içi anonim konuşmacı kimliği. Toplantılar arası TAŞINMAZ (spec 10 S4:
/// kalıcı ses parmak izi saklanmaz). <see cref="Unknown"/> = atanamadı (örtüşme, eşik altı
/// veya konuşmacı bölgesi yok) — uydurma atama yerine dürüst belirsizlik (master §12.2).</summary>
public readonly record struct SpeakerId(int Index)
{
    public static readonly SpeakerId Unknown = new(-1);

    /// <summary>Kullanıcı isim vermediyse gösterilecek etiket. 26'dan sonra sayıya döner.</summary>
    public string DefaultLabel => Index < 0
        ? "Bilinmeyen"
        : Index < 26 ? $"Konuşmacı {(char)('A' + Index)}" : $"Konuşmacı {Index + 1}";
}

/// <summary>Diarization'ın ürettiği konuşmacı bölgesi (mutlak zaman).</summary>
public sealed record SpeakerSegment(SpeakerId Speaker, TimeSpan Start, TimeSpan End);
```

- [ ] **Adım 4: `IDiarizer` arayüzünü ekle**

`src/SentimentAnalysis.Application/Abstractions/Interfaces.cs` sonuna:

```csharp
/// <summary>Tüm kaydı görüp konuşmacı bölgeleri üretir (Alt-proje 10b; sherpa-onnx offline
/// diarization). Canlı hatta ÇAĞRILMAZ — arka plan geçişinde bir kez çalışır (spec 10 S2).</summary>
public interface IDiarizer
{
    Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, CancellationToken ct);
}
```

- [ ] **Adım 5: `SpeakerAligner`'ı uygula**

`src/SentimentAnalysis.Application/Diarization/SpeakerAligner.cs`:

```csharp
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Zaman damgalı transkript segmentlerini konuşmacı bölgeleriyle eşler. Saf ve
/// deterministik — diarization'ın belirsizliği motor sınırında kalır, "kim kimdir" kararı
/// burada test edilebilir matematiktir (spec 10).</summary>
public static class SpeakerAligner
{
    /// <param name="minOverlapRatio">Bir segmentin süresinin en az bu oranı bir konuşmacı
    /// bölgesiyle örtüşmelidir; altında kalırsa <see cref="SpeakerId.Unknown"/> döner. Örtüşen
    /// konuşma ve sınır artıkları bu kapıdan elenir (spec 10 S6).</param>
    public static IReadOnlyList<(TranscriptSegment Segment, SpeakerId Speaker)> Align(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<SpeakerSegment> speakers,
        double minOverlapRatio)
    {
        var result = new List<(TranscriptSegment, SpeakerId)>(transcript.Count);

        foreach (var seg in transcript)
        {
            double duration = (seg.End - seg.Start).TotalSeconds;
            if (duration <= 0) { result.Add((seg, SpeakerId.Unknown)); continue; }

            SpeakerId best = SpeakerId.Unknown;
            double bestOverlap = 0;

            foreach (var sp in speakers)
            {
                double overlap =
                    Math.Min(seg.End.TotalSeconds, sp.End.TotalSeconds) -
                    Math.Max(seg.Start.TotalSeconds, sp.Start.TotalSeconds);
                if (overlap > bestOverlap) { bestOverlap = overlap; best = sp.Speaker; }
            }

            result.Add(bestOverlap / duration >= minOverlapRatio ? (seg, best) : (seg, SpeakerId.Unknown));
        }

        return result;
    }
}
```

- [ ] **Adım 6: Testlerin geçtiğini doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **315 geçer** (8 yeni test), 8 atlanır, 0 başarısız, 0 uyarı.

- [ ] **Adım 7: Commit**

```bash
git add src/SentimentAnalysis.Core/Speaker.cs \
        src/SentimentAnalysis.Application/Abstractions/Interfaces.cs \
        src/SentimentAnalysis.Application/Diarization/ \
        tests/SentimentAnalysis.Application.Tests/Diarization/
git commit -m "feat(core,application): SpeakerId, SpeakerSegment ve saf SpeakerAligner"
```

---

## Task 6: Diarization dilimi (sherpa-onnx)

Native bağımlılık bu projede izole edilir; dışarı yalnızca `IDiarizer` sızar.

**Files:**
- Oluştur: `src/SentimentAnalysis.Infrastructure.Diarization/SentimentAnalysis.Infrastructure.Diarization.csproj`
- Oluştur: `src/SentimentAnalysis.Infrastructure.Diarization/SherpaDiarizer.cs`
- Oluştur: `src/SentimentAnalysis.Infrastructure.Diarization/IDiarizationModelProvider.cs`
- Oluştur: `src/SentimentAnalysis.Infrastructure.Diarization/FileDiarizationModelProvider.cs`
- Oluştur: `src/SentimentAnalysis.Infrastructure.Diarization/DiarizationServiceCollectionExtensions.cs`
- Oluştur: `tests/SentimentAnalysis.Infrastructure.Diarization.Tests/` (yeni test projesi)

**Interfaces:**
- Consumes: `IDiarizer` (Task 5)
- Produces: `SherpaDiarizer : IDiarizer, IDisposable`; DI: `services.AddDiarization()`

- [ ] **Adım 1: Projeleri oluştur ve çözüme ekle**

Mevcut Vad diliminin yapısını örnek alın (`FileVadModelProvider` / `VadServiceCollectionExtensions`):

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet new classlib -n SentimentAnalysis.Infrastructure.Diarization -o src/SentimentAnalysis.Infrastructure.Diarization
dotnet new xunit -n SentimentAnalysis.Infrastructure.Diarization.Tests -o tests/SentimentAnalysis.Infrastructure.Diarization.Tests
dotnet sln add src/SentimentAnalysis.Infrastructure.Diarization tests/SentimentAnalysis.Infrastructure.Diarization.Tests
dotnet add src/SentimentAnalysis.Infrastructure.Diarization package org.k2fsa.sherpa.onnx --version 1.13.4
dotnet add src/SentimentAnalysis.Infrastructure.Diarization reference src/SentimentAnalysis.Application
dotnet add tests/SentimentAnalysis.Infrastructure.Diarization.Tests reference src/SentimentAnalysis.Infrastructure.Diarization
dotnet add tests/SentimentAnalysis.Infrastructure.Diarization.Tests package Xunit.SkippableFact
```

Diğer Infrastructure projelerindeki `<TargetFramework>`, `<Nullable>`, `<TreatWarningsAsErrors>`
gibi ayarları birebir eşleyin (uyarı sayısı artmamalı).

- [ ] **Adım 2: Env-gated gerçek-model testini yaz**

`tests/SentimentAnalysis.Infrastructure.Diarization.Tests/SherpaDiarizerTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Diarization;
using Xunit;

public class SherpaDiarizerTests
{
    [SkippableFact]
    public async Task DiarizeAsync_GercekModel_IkiKonusmaciyiAyirir()
    {
        Skip.If(Environment.GetEnvironmentVariable("DIAR_TEST") != "1",
            "Gerçek diarization modeli gerekir; DIAR_TEST=1 ile çalıştırın.");
        string wav = Environment.GetEnvironmentVariable("DIAR_TEST_WAV") ?? string.Empty;
        Skip.If(!File.Exists(wav), "DIAR_TEST_WAV geçerli bir 16 kHz mono WAV yolu olmalı.");

        var (samples, rate) = TestWav.Read(wav);
        Skip.If(rate != 16000, "16 kHz bekleniyor.");

        using var diarizer = new SherpaDiarizer(TestConfig.Default(), new FileDiarizationModelProvider(TestConfig.Default()));
        var segments = await diarizer.DiarizeAsync(samples, CancellationToken.None);

        Assert.NotEmpty(segments);
        // 10a ölçümü: 2 konuşmacılı kayıtta eşik 0,8 ile tam 2 küme bulunur.
        var distinct = new HashSet<int>();
        foreach (var s in segments) distinct.Add(s.Speaker.Index);
        Assert.Equal(2, distinct.Count);
        // Bölgeler sıralı ve pozitif uzunlukta olmalı.
        Assert.All(segments, s => Assert.True(s.End > s.Start));
    }

    [Fact]
    public void DiarizeAsync_BosSes_BosListeDondurur()
    {
        using var diarizer = new SherpaDiarizer(TestConfig.Default(), new FileDiarizationModelProvider(TestConfig.Default()));
        var segments = diarizer.DiarizeAsync(Array.Empty<float>(), CancellationToken.None).Result;
        Assert.Empty(segments);   // model yüklemeden kısa devre — modelsiz ortamda da geçer
    }
}
```

> `TestWav.Read` ve `TestConfig.Default()` yardımcılarını bu test projesinde oluşturun;
> WAV okuyucu için 10a'da doğrulanmış 16-bit PCM mono okuyucu desenini kullanın
> (**`SherpaOnnx.WaveReader` NuGet paketinde YOKTUR**).

- [ ] **Adım 3: Testin başarısız olduğunu doğrula**

```bash
dotnet test tests/SentimentAnalysis.Infrastructure.Diarization.Tests
```

Beklenen: derleme hatası — `SherpaDiarizer` yok.

- [ ] **Adım 4: Model sağlayıcıyı uygula**

`IDiarizationModelProvider.cs` ve `FileDiarizationModelProvider.cs` — mevcut
`IVadModelProvider` / `FileVadModelProvider` desenini izleyin:

```csharp
namespace SentimentAnalysis.Infrastructure.Diarization;

public interface IDiarizationModelProvider
{
    /// <summary>pyannote segmentation ONNX yolu (fp32 — 10a: int8 hız kazandırmıyor, doğruluk düşürüyor).</summary>
    string SegmentationModelPath { get; }
    /// <summary>3D-Speaker ERes2Net embedding ONNX yolu (10a: CAM++ ölçümde elendi).</summary>
    string EmbeddingModelPath { get; }
}
```

`FileDiarizationModelProvider`, `AppConfig`'teki model dizininden şu adları çözer:
`sherpa-onnx-pyannote-segmentation-3-0/model.onnx` ve `speaker-embedding.onnx`.
Dosya yoksa `FileNotFoundException` fırlatın — sessizce boş sonuç dönmeyin (sahte-yeşil yasak).

- [ ] **Adım 5: `SherpaDiarizer`'ı uygula**

```csharp
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Diarization;

/// <summary>sherpa-onnx offline speaker diarization sarmalayıcısı. Native tipler bu sınıfın
/// içinde kalır (mimari kural). Model açılışta bir kez yüklenir, sıcak tutulur.
/// <para>Eşik değerleri 10a'da ÖLÇÜLDÜ: <c>Threshold = 0.8</c> (sherpa'nın tipik varsayılanı
/// 0,5, iki kişilik kayıtta ALTI konuşmacı buluyordu), <c>NumClusters = 0</c> (konuşmacı
/// sayısı bilinmiyor). Bkz. 10a-diarization-spike-olcumu.md</para></summary>
public sealed class SherpaDiarizer : IDiarizer, IDisposable
{
    private readonly AppConfig _config;
    private readonly IDiarizationModelProvider _models;
    private readonly object _sync = new();
    private SherpaOnnx.OfflineSpeakerDiarization? _sd;
    private bool _disposed;

    public SherpaDiarizer(AppConfig config, IDiarizationModelProvider models)
    {
        _config = config; _models = models;
    }

    public Task<IReadOnlyList<SpeakerSegment>> DiarizeAsync(float[] mono16k, CancellationToken ct)
    {
        // Boş/çok kısa ses: modeli hiç yükleme (modelsiz ortamda da çalışır).
        if (mono16k.Length == 0)
            return Task.FromResult<IReadOnlyList<SpeakerSegment>>(Array.Empty<SpeakerSegment>());

        ct.ThrowIfCancellationRequested();

        // sherpa API'si senkron; uzun süren iş (RTF ~0,44) çağıranı bloklamasın diye
        // havuz thread'ine alınır. Arka plan geçişinde çalıştığı için UI'yı etkilemez.
        return Task.Run<IReadOnlyList<SpeakerSegment>>(() =>
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _sd ??= Create();
                var raw = _sd.Process(mono16k);
                var list = new List<SpeakerSegment>(raw.Length);
                foreach (var s in raw)
                    list.Add(new SpeakerSegment(
                        new SpeakerId(s.Speaker),
                        TimeSpan.FromSeconds(s.Start),
                        TimeSpan.FromSeconds(s.End)));
                return list;
            }
        }, ct);
    }

    private SherpaOnnx.OfflineSpeakerDiarization Create()
    {
        var cfg = new SherpaOnnx.OfflineSpeakerDiarizationConfig();
        cfg.Segmentation.Pyannote.Model = _models.SegmentationModelPath;
        cfg.Embedding.Model = _models.EmbeddingModelPath;
        cfg.Clustering.NumClusters = 0;                                   // bilinmiyor
        cfg.Clustering.Threshold = (float)_config.Diarization.ClusteringThreshold;
        return new SherpaOnnx.OfflineSpeakerDiarization(cfg);
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; _sd = null; }
    }
}
```

- [ ] **Adım 6: `AppConfig`'e `DiarizationConfig` ekle**

`src/SentimentAnalysis.Application/Configuration/` içinde, mevcut `VadConfig` desenini izleyin:

```csharp
/// <summary>Konuşmacı ayrımı ayarları. Değerler 10a spike'ında ÖLÇÜLDÜ (tahmin değil).</summary>
public sealed record DiarizationConfig(
    bool Enabled = false,                 // opt-in: kapalıyken davranış bugünküyle aynı
    double ClusteringThreshold = 0.8,     // 10a: 0,5 iki kişilik kayıtta 6 konuşmacı buluyordu
    double MinOverlapRatio = 0.5,         // SpeakerAligner kapısı
    int MaxRecordedMinutes = 90);         // ses biriktirme üst sınırı (~10 GB değil, ~170 MB PCM)
```

`AppConfig`'e additive alan olarak ekleyin: `DiarizationConfig Diarization = ...` (varsayılanlı).

- [ ] **Adım 7: Testlerin geçtiğini doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **316 geçer**, **9 atlanır** (yeni env-gated test), 0 başarısız, 0 uyarı.

- [ ] **Adım 8: Gerçek modelle bir kez doğrula**

```bash
DIAR_TEST=1 DIAR_TEST_WAV=/path/to/16k-mono.wav dotnet test tests/SentimentAnalysis.Infrastructure.Diarization.Tests
```

Beklenen: test GEÇER ve 2 konuşmacı bulur. Atlanırsa env değişkenlerini kontrol edin —
**atlanan test geçmiş sayılmaz.**

- [ ] **Adım 9: Commit**

```bash
git add src/SentimentAnalysis.Infrastructure.Diarization tests/SentimentAnalysis.Infrastructure.Diarization.Tests SentimentAnalysis.sln src/SentimentAnalysis.Application/Configuration
git commit -m "feat(diarization): sherpa-onnx tabanlı SherpaDiarizer dilimi"
```

---

## Task 7: Arka plan geçişi ve konuşmacı segmentlerinin saklanması

**Files:**
- Oluştur: `src/SentimentAnalysis.Application/Diarization/DiarizationJob.cs`
- Değiştir: `src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs` (konuşmacı yaz/oku)
- Test: `tests/SentimentAnalysis.Application.Tests/Diarization/DiarizationJobTests.cs`

**Interfaces:**
- Consumes: `IDiarizer` (Task 5/6), `SpeakerAligner` (Task 5), saklanmış ses (Task 4),
  `GetTranscriptSegmentsAsync` (Task 3)
- Produces: `DiarizationJob.RunAsync(Guid sessionId, CancellationToken)` → `DiarizationOutcome`
  (`Completed` / `NoAudio` / `Failed`); `SqliteSessionStore.SaveSpeakerSegmentsAsync(...)`

- [ ] **Adım 1: Başarısız testleri yaz**

`tests/SentimentAnalysis.Application.Tests/Diarization/DiarizationJobTests.cs`:

```csharp
    [Fact]
    public async Task RunAsync_SesVeTranskriptVarsa_KonusmaciSegmentleriniSaklar()
    {
        var store = new FakeDiarizationStore(
            audio: new float[16000 * 10],
            transcript: [ new TranscriptSegment("a", TimeSpan.Zero, TimeSpan.FromSeconds(5)),
                          new TranscriptSegment("b", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)) ]);
        var diarizer = new FakeDiarizer([
            new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(5)),
            new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))]);

        var job = new DiarizationJob(diarizer, store, AppConfig.Default);
        var outcome = await job.RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Completed, outcome);
        Assert.Equal(2, store.SavedSpeakerSegments.Count);
    }

    [Fact]
    public async Task RunAsync_SesYoksa_NoAudioDoner_DiarizerCagrilmaz()
    {
        var store = new FakeDiarizationStore(audio: null, transcript: []);
        var diarizer = new FakeDiarizer([]);

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.NoAudio, outcome);
        Assert.False(diarizer.WasCalled);
    }

    [Fact]
    public async Task RunAsync_DiarizerPatlarsa_FailedDoner_HataYukariAkmaz()
    {
        var store = new FakeDiarizationStore(audio: new float[16000], transcript: []);
        var diarizer = new ThrowingDiarizer();

        var outcome = await new DiarizationJob(diarizer, store, AppConfig.Default)
            .RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DiarizationOutcome.Failed, outcome);
        Assert.Empty(store.SavedSpeakerSegments);   // yarım sonuç yazılmaz
    }

    [Fact]
    public async Task RunAsync_Iptal_OperationCanceledFirlatir_YarimSonucYazmaz()
    {
        var store = new FakeDiarizationStore(audio: new float[16000], transcript: []);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiarizationJob(new FakeDiarizer([]), store, AppConfig.Default).RunAsync(Guid.NewGuid(), cts.Token));

        Assert.Empty(store.SavedSpeakerSegments);
    }
```

Sahteleri aynı dosyada tanımlayın (`FakeDiarizationStore` ses/transkript döndürür ve
kaydedilen konuşmacı segmentlerini biriktirir; `ThrowingDiarizer` `InvalidOperationException`
fırlatır; `FakeDiarizer` verilen listeyi döndürür ve `WasCalled` işaretler).

- [ ] **Adım 2: Testlerin başarısız olduğunu doğrula**

```bash
dotnet test tests/SentimentAnalysis.Application.Tests --filter "DiarizationJobTests"
```

Beklenen: derleme hatası — `DiarizationJob` yok.

- [ ] **Adım 3: `DiarizationJob`'ı uygula**

```csharp
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Arka plan konuşmacı geçişinin sonucu. Kullanıcıya dürüstçe bildirilir —
/// sessiz başarısızlık yok (master §12.2).</summary>
public enum DiarizationOutcome { Completed, NoAudio, Failed }

/// <summary>Oturum bittikten SONRA bir kez çalışan arka plan geçişi: saklanmış sesi diarize
/// eder, transkript segmentleriyle hizalar, konuşmacı bölgelerini saklar. Canlı hatta
/// çağrılmaz (spec 10 S2 — canlı anlık etiket 10a ölçümüyle elendi).</summary>
public sealed class DiarizationJob
{
    private readonly IDiarizer _diarizer;
    private readonly IDiarizationStore _store;
    private readonly AppConfig _config;

    public DiarizationJob(IDiarizer diarizer, IDiarizationStore store, AppConfig config)
    {
        _diarizer = diarizer; _store = store; _config = config;
    }

    public async Task<DiarizationOutcome> RunAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var audio = await _store.GetSessionAudioAsync(sessionId, ct).ConfigureAwait(false);
        if (audio is null || audio.Length == 0) return DiarizationOutcome.NoAudio;

        IReadOnlyList<SpeakerSegment> speakers;
        try
        {
            speakers = await _diarizer.DiarizeAsync(audio, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;   // iptal bir hata değil, koşunun durumudur
        }
        catch
        {
            // Model yok / native hata: kullanıcıya "başarısız" denir, yarım sonuç yazılmaz.
            return DiarizationOutcome.Failed;
        }

        await _store.SaveSpeakerSegmentsAsync(sessionId, speakers, ct).ConfigureAwait(false);
        return DiarizationOutcome.Completed;
    }
}
```

`IDiarizationStore` arayüzünü `Abstractions/Interfaces.cs`'e ekleyin:

```csharp
/// <summary>Arka plan diarization geçişinin ihtiyaç duyduğu depolama işlemleri (Alt-proje 10b).</summary>
public interface IDiarizationStore
{
    Task<float[]?> GetSessionAudioAsync(Guid sessionId, CancellationToken ct);
    Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid sessionId, CancellationToken ct);
    Task SaveSpeakerSegmentsAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, CancellationToken ct);
}
```

`SqliteSessionStore`'u bu arayüzü uygulayacak şekilde genişletin: `GetSessionAudioAsync`
`sessions.audio_path`'ten şifreli sesi okur, PCM16 → `float[]` çevirir (yoksa `null` döner);
`SaveSpeakerSegmentsAsync` `speaker_segments` tablosuna yazar (aynı oturum için önce siler,
tekrar çalıştırmada çift kayıt olmasın).

- [ ] **Adım 4: Testlerin geçtiğini doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **320 geçer**, 9 atlanır, 0 başarısız, 0 uyarı.

- [ ] **Adım 5: Commit**

```bash
git add src tests
git commit -m "feat(application,storage): arka plan diarization geçişi ve konuşmacı segmentleri"
```

---

## Task 8: Raporda etiketli transkript ve durum göstergesi

**Files:**
- Değiştir: `src/SentimentAnalysis.Desktop/ReportViewModel.cs`
- Değiştir: `src/SentimentAnalysis.Desktop.Avalonia/Views/` (rapor görünümü)
- Test: `tests/SentimentAnalysis.Desktop.Tests/ReportViewModelTests.cs`

**Interfaces:**
- Consumes: `DiarizationJob` + `DiarizationOutcome` (Task 7), `SpeakerAligner` (Task 5)
- Produces: `ReportViewModel.SpeakerLines` (`IReadOnlyList<SpeakerLineVm>`),
  `ReportViewModel.DiarizationStatus` (kullanıcıya görünen metin)

- [ ] **Adım 1: Başarısız testleri yaz**

```csharp
    [Fact]
    public void SpeakerLines_ArdisikAyniKonusmaci_TekSatirdaBirlesir()
    {
        var vm = NewReportViewModel();
        vm.ApplySpeakers(
            transcript: [ new TranscriptSegment("Merhaba", TimeSpan.Zero, TimeSpan.FromSeconds(2)),
                          new TranscriptSegment("nasılsın", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
                          new TranscriptSegment("iyiyim", TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)) ],
            speakers: [ new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(4)),
                        new SpeakerSegment(new SpeakerId(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)) ]);

        Assert.Equal(2, vm.SpeakerLines.Count);
        Assert.Equal("Konuşmacı A", vm.SpeakerLines[0].Label);
        Assert.Equal("Merhaba nasılsın", vm.SpeakerLines[0].Text);
        Assert.Equal("Konuşmacı B", vm.SpeakerLines[1].Label);
        Assert.Equal("iyiyim", vm.SpeakerLines[1].Text);
    }

    [Fact]
    public void DiarizationStatus_Basarisiz_KullaniciyaDurustcuBildirir()
    {
        var vm = NewReportViewModel();
        vm.SetDiarizationOutcome(DiarizationOutcome.Failed);

        Assert.Contains("başarısız", vm.DiarizationStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.SpeakerLines);   // uydurma etiket gösterme
    }

    [Fact]
    public void DiarizationStatus_SesYok_NedeniniSoyler()
    {
        var vm = NewReportViewModel();
        vm.SetDiarizationOutcome(DiarizationOutcome.NoAudio);

        Assert.False(string.IsNullOrWhiteSpace(vm.DiarizationStatus));
        Assert.Empty(vm.SpeakerLines);
    }

    [Fact]
    public void SpeakerLines_BilinmeyenKonusmaci_BilinmeyenEtiketiyleGosterilir()
    {
        var vm = NewReportViewModel();
        vm.ApplySpeakers(
            transcript: [ new TranscriptSegment("mırıltı", TimeSpan.Zero, TimeSpan.FromSeconds(2)) ],
            speakers: []);   // hiç konuşmacı bölgesi yok

        Assert.Single(vm.SpeakerLines);
        Assert.Equal("Bilinmeyen", vm.SpeakerLines[0].Label);
    }
```

- [ ] **Adım 2: Testin başarısız olduğunu doğrula**

```bash
dotnet test tests/SentimentAnalysis.Desktop.Tests --filter "SpeakerLines"
```

Beklenen: derleme hatası.

- [ ] **Adım 3: `ReportViewModel`'i genişlet**

Ardışık aynı konuşmacıya ait segmentleri tek satırda birleştiren saf bir yardımcı ekleyin
(`SpeakerLineBuilder`, Application katmanında, unit-testli):

```csharp
public sealed record SpeakerLine(SpeakerId Speaker, string Text, TimeSpan Start, TimeSpan End);

public static class SpeakerLineBuilder
{
    /// <summary>Hizalanmış segmentleri okunabilir konuşma satırlarına indirger: ardışık
    /// aynı konuşmacı tek satırda birleşir ("Konuşmacı A: merhaba nasılsın").</summary>
    public static IReadOnlyList<SpeakerLine> Build(
        IReadOnlyList<(TranscriptSegment Segment, SpeakerId Speaker)> aligned)
    {
        var lines = new List<SpeakerLine>();
        foreach (var (seg, spk) in aligned)
        {
            if (lines.Count > 0 && lines[^1].Speaker == spk)
            {
                var last = lines[^1];
                lines[^1] = last with { Text = $"{last.Text} {seg.Text}", End = seg.End };
            }
            else lines.Add(new SpeakerLine(spk, seg.Text, seg.Start, seg.End));
        }
        return lines;
    }
}
```

`ReportViewModel.ApplySpeakers` bu ikisini zincirler: `SpeakerAligner.Align(...)` →
`SpeakerLineBuilder.Build(...)` → `SpeakerLines`.

`DiarizationStatus` metinleri Türkçe ve dürüst olmalı:
- `Completed` → "Konuşmacılar hazır."
- `NoAudio` → "Konuşmacı ayrımı yapılamadı: bu oturumun sesi saklanmamış."
- `Failed` → "Konuşmacı ayrımı başarısız oldu; transkript konuşmacısız gösteriliyor."
- Çalışırken → "Konuşmacılar hesaplanıyor…"

- [ ] **Adım 4: Görünümü güncelle**

Rapor görünümünde transkript bölümünü, `SpeakerLines` boş değilse konuşmacı etiketli
satırlarla, boşsa bugünkü düz transkriptle gösterin. `DiarizationStatus` her zaman görünür
olmalıdır (08e dersi: kullanıcı ne gördüğünü bilmeli).

UI güncellemeleri **UI thread'inde** yapılmalıdır — arka plan işi bittiğinde
`Dispatcher.UIThread.Post(...)` kullanın (08e düzeltme turunda aynı hata yaşandı).

- [ ] **Adım 5: Testlerin geçtiğini doğrula**

```bash
dotnet build && dotnet test
```

Beklenen: **324 geçer**, 9 atlanır, 0 başarısız, 0 uyarı.

- [ ] **Adım 6: Commit**

```bash
git add src tests
git commit -m "feat(ui): raporda konuşmacı etiketli transkript ve diarization durumu"
```

---

## Task 9: Gerçek kullanımda canlı doğrulama (BU TASK OLMADAN 10b BİTMEZ)

> **08e dersi:** 292 test yeşilken ürün kırıktı; "Durdur"a basınca kaydedilen ses çöpe
> gidiyordu ve hiçbir test "kullanıcı bu düğmeye basınca ne görüyor" sorusunu sormuyordu.
> Yeşil test takımı, çalışan ürün demek değildir.

**Files:** yok (doğrulama task'ı) · Değiştir: gerekirse bulunan hatalar

- [ ] **Adım 1: Uygulamayı paketle ve çalıştır**

```bash
./build-macos-app.sh
open "dist/Sesli Duygu Analizi.app"
```

- [ ] **Adım 2: Ayarlardan konuşmacı ayrımını AÇ**

`DiarizationConfig.Enabled = true` (Ayarlar ekranı veya yapılandırma dosyası).

- [ ] **Adım 3: Dosya analiziyle doğrula**

10a'da kullanılan 2 kişilik kaydı yükleyin. Beklenen: raporda `Konuşmacı A:` / `Konuşmacı B:`
satırları görünür ve **konuşma sıraları gerçekten doğru kişiye ait**. Metni okuyarak
doğrulayın — segment sayısının doğru olması yetmez.

- [ ] **Adım 4: Canlı oturumla doğrula**

En az iki kişiyle ~2 dakikalık canlı kayıt alın, "Durdur"a basın. Beklenen sıra:
1. Rapor **hemen** gelir (bugünkü gibi, konuşmacısız) — donma YOK.
2. "Konuşmacılar hesaplanıyor…" görünür.
3. Bitince transkript konuşmacı etiketli hale gelir ve durum "Konuşmacılar hazır." olur.

- [ ] **Adım 5: Başarısızlık yolunu doğrula**

Model dosyalarından birini geçici olarak yeniden adlandırıp canlı oturumu tekrarlayın.
Beklenen: uygulama ÇÖKMEZ, rapor yine gelir, durum "Konuşmacı ayrımı başarısız oldu…" der.
Sonra dosya adını geri alın.

- [ ] **Adım 6: Kullanıcı onayı**

Kullanıcı 3, 4 ve 5. adımları kendi gözüyle görüp onaylamadan bu task kapatılmaz.
Bulunan her sorun düzeltilir ve adımlar tekrarlanır.

- [ ] **Adım 7: Spec'i kapat ve commit et**

`docs/superpowers/specs/subprojects/10-konusmaci-ayrimi.md` içinde 10b satırını tamamlandı
olarak işaretleyin; canlı doğrulamada öğrenilenleri (özellikle 3+ konuşmacı ve örtüşme
davranışı) "Açık uçlar"a işleyin.

```bash
git add docs src tests
git commit -m "docs: 10b tamamlandı — canlı doğrulama notları"
```

---

## Kapsam dışı (10c'ye bırakılan)

- **Konuşmacı başına duygu** — bu turda duygu analizi bugünkü gibi chunk düzeyinde ve
  toplam-akış kalır. `SpeakerSummary`, konuşmacı başına füzyon ve konuşmacı bazlı rapor
  kartları 10c'dir.
- **Konuşmacıya isim verme** (inline düzenleme) — 10c ile birlikte gelir; 10b anonim
  `Konuşmacı A/B` etiketleriyle teslim edilir.
- **Zaman çizelgesinde konuşmacı renkleri** — 10c.
