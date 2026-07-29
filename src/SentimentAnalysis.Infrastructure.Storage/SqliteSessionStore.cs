using Microsoft.Data.Sqlite;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Gerçek <see cref="ISessionStore"/>: metrik/transkript SQLite'ta kalıcı, ham ses şifreli dosyada
/// (<see cref="EncryptedAudioStore"/>). Retention dolunca ses silinir, metrik korunur. Bağlantı-başına-işlem
/// (SQLite dosya kilidi ile serileşir). Metadata sınırı: StartedAt = ilk kayıt (enjekte saat), Language = Other
/// (ChunkResult taşımaz; toplantı metadata'sı 06 pipeline'da).</summary>
public sealed class SqliteSessionStore : ISessionStore, ISessionAudioSink, IDiarizationStore, ISpeakerDataReader
{
    /// <summary>Oturumun saklanmış (şifreli) sesini 16 kHz mono float PCM olarak okur.
    /// Ses yoksa <c>null</c> — hata değil, "bu oturumda ses saklanmamış" demektir (10b).
    /// <para>Yazma tarafıyla simetriktir: <c>RecordingCaptureSource.GetPcm16</c> 16-bit
    /// little-endian üretir, burada aynı biçim geri çözülür.</para></summary>
    public async Task<float[]?> GetSessionAudioAsync(Guid sessionId, CancellationToken ct)
    {
        string? path;
        await using (var conn = new SqliteConnection(_connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT audio_path FROM sessions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            path = result as string;
        }

        // audio_path null olabilir: hiç ses saklanmadı ya da retention temizliği sildi.
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        var pcm16 = _audio.Read(path);
        var samples = new float[pcm16.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(pcm16, i * 2) / 32768f;
        return samples;
    }

    /// <summary><see cref="ISessionAudioSink"/> uyarlaması (10b): canlı koşunun tek ihtiyacı
    /// olan "sesi yaz" yeteneğini, mevcut <see cref="SaveAudioAsync"/> (şifreleme + retention)
    /// üzerinden sunar. Ayrı bir yol değil, aynı yolun dar bir kapısıdır.</summary>
    public async Task SaveSessionAudioAsync(Guid sessionId, byte[] pcm16, CancellationToken ct)
        => await SaveAudioAsync(sessionId, pcm16, ct).ConfigureAwait(false);

    private readonly string _connectionString;
    private readonly int _retentionDays;
    private readonly EncryptedAudioStore _audio;
    private readonly Func<DateTimeOffset> _clock;

    public SqliteSessionStore(AppConfig config, EncryptedAudioStore audio, Func<DateTimeOffset>? clock = null)
    {
        _audio = audio;
        _retentionDays = config.Storage.AudioRetentionDays;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        var dir = config.Storage.StorageDirectory ?? FileKeyProvider.DefaultStorageDirectory();
        Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "sessions.db") }.ToString();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = StorageSchema.CreateSql;
        cmd.ExecuteNonQuery();

        Migrate(conn);
    }

    /// <summary>08d öncesi bir veritabanına eksik sütunları ekler. `CREATE TABLE IF NOT EXISTS`
    /// mevcut tabloya sütun eklemez; bu adım olmadan eski kurulumlar sessizce bozulur.
    /// İdempotenttir: eksik olmayan sütun atlanır.</summary>
    private static void Migrate(SqliteConnection conn)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var q = conn.CreateCommand())
        {
            q.CommandText = "PRAGMA table_info(chunks)";
            using var rd = q.ExecuteReader();
            while (rd.Read()) existing.Add(rd.GetString(1));   // sütun 1 = ad
        }

        foreach (var (name, definition) in StorageSchema.ChunkColumnsAddedIn08d)
        {
            if (existing.Contains(name)) continue;
            using var alter = conn.CreateCommand();
            // Sütun adı/tanımı sabit literal listeden gelir (kullanıcı girdisi değil) —
            // SQLite ALTER TABLE sütun adını parametre olarak kabul etmez.
            alter.CommandText = $"ALTER TABLE chunks ADD COLUMN {name} {definition}";
            alter.ExecuteNonQuery();
        }
    }

    public async Task SaveChunkAsync(Guid sessionId, ChunkResult r, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Oturum satırını garanti et (ilk chunk).
        await using (var s = conn.CreateCommand())
        {
            s.CommandText = "INSERT OR IGNORE INTO sessions (id, started_at, language) VALUES ($id, $ts, $lang)";
            s.Parameters.AddWithValue("$id", sessionId.ToString("N"));
            s.Parameters.AddWithValue("$ts", _clock().ToUnixTimeMilliseconds());
            s.Parameters.AddWithValue("$lang", (int)DetectedLanguage.Other);
            await s.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chunks (session_id, seq, time_start_ms, time_end_ms, transcript,
                combined_valence, combined_arousal, primary_emotion, secondary_emotion, confidence,
                agreement, incongruent, pattern, congruence, text_valence, audio_valence,
                text_arousal, audio_arousal, text_confidence, audio_confidence)
            VALUES ($sid,
                (SELECT COALESCE(MAX(seq), -1) + 1 FROM chunks WHERE session_id = $sid),
                $ts, $te, $tr, $cv, $ca, $pe, $se, $cf, $ag, $inc, $pat, $cong, $tv, $av,
                $ta, $aa, $tc, $ac)
            """;
        var f = r.Fused;
        cmd.Parameters.AddWithValue("$sid", sessionId.ToString("N"));
        cmd.Parameters.AddWithValue("$ts", (long)r.TimeStart.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$te", (long)r.TimeEnd.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$tr", r.Transcript);
        cmd.Parameters.AddWithValue("$cv", f.Combined.Valence);
        cmd.Parameters.AddWithValue("$ca", f.Combined.Arousal);
        cmd.Parameters.AddWithValue("$pe", (int)f.PrimaryEmotion);
        cmd.Parameters.AddWithValue("$se", (int)f.SecondaryEmotion);
        cmd.Parameters.AddWithValue("$cf", (int)f.Confidence);
        cmd.Parameters.AddWithValue("$ag", f.Agreement ? 1 : 0);
        cmd.Parameters.AddWithValue("$inc", f.Incongruent ? 1 : 0);
        cmd.Parameters.AddWithValue("$pat", (int)f.Pattern);
        cmd.Parameters.AddWithValue("$cong", f.Congruence);
        cmd.Parameters.AddWithValue("$tv", f.TextVa.Valence);
        cmd.Parameters.AddWithValue("$av", f.ProsodyVa.Valence);
        cmd.Parameters.AddWithValue("$ta", f.TextVa.Arousal);
        cmd.Parameters.AddWithValue("$aa", f.ProsodyVa.Arousal);
        cmd.Parameters.AddWithValue("$tc", f.TextConfidence);
        cmd.Parameters.AddWithValue("$ac", f.ProsodyConfidence);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        // Zaman damgalı transkript segmentleri (10b): konuşmacı hizalamasının hammaddesi.
        // Chunk'ın kendi transkripti `chunks.transcript`'te kalır; bu tablo ona ek, ondan
        // türetilebilir olmayan zaman bilgisini taşır.
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
    }

    /// <summary>Oturumun zaman damgalı transkript segmentleri (yazılma sırasına göre).
    /// <para><b>Neden oturum düzeyinde?</b> Segmentler chunk'a geri dağıtılmaz: yakalama
    /// pencereleri örtüşebildiğinden (<c>CaptureOptions.Overlap</c>) bir segment iki chunk'ın
    /// sınırları içine birden düşebilir ve "hangi chunk'ın?" sorusunun doğru bir cevabı yoktur.
    /// Konuşmacı hizalaması zaten tüm oturumu tek zaman ekseninde işler; bu yüzden
    /// <c>GetSessionAsync</c> ile dönen <c>ChunkResult</c>'ların <c>Segments</c>'i BOŞTUR —
    /// veri kaybı değil, bilinçli bir sınır (bkz. testler).</para>
    /// ISessionStore dışı ek yetenek (10b).</summary>
    public async Task<IReadOnlyList<TranscriptSegment>> GetTranscriptSegmentsAsync(Guid sessionId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT start_ms, end_ms, text FROM transcript_segments WHERE session_id = $id ORDER BY seq";
        cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));

        var list = new List<TranscriptSegment>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new TranscriptSegment(
                rd.GetString(2),
                TimeSpan.FromMilliseconds(rd.GetInt64(0)),
                TimeSpan.FromMilliseconds(rd.GetInt64(1))));
        return list;
    }

    /// <summary>Arka plan diarization geçişinin sonucunu saklar. Aynı oturum için önce siler:
    /// geçiş yeniden çalıştırılabilir olmalıdır (kullanıcı raporu tekrar açtığında çift kayıt
    /// birikmesin). ISessionStore dışı ek yetenek (10b).</summary>
    public async Task SaveSpeakerSegmentsAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using (var del = conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM speaker_segments WHERE session_id = $id";
            del.Parameters.AddWithValue("$id", sessionId.ToString("N"));
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        for (int i = 0; i < segments.Count; i++)
        {
            await using var s = conn.CreateCommand();
            s.CommandText = """
                INSERT INTO speaker_segments (session_id, seq, start_ms, end_ms, speaker_index)
                VALUES ($sid, $seq, $st, $en, $spk)
                """;
            s.Parameters.AddWithValue("$sid", sessionId.ToString("N"));
            s.Parameters.AddWithValue("$seq", i);
            s.Parameters.AddWithValue("$st", (long)segments[i].Start.TotalMilliseconds);
            s.Parameters.AddWithValue("$en", (long)segments[i].End.TotalMilliseconds);
            s.Parameters.AddWithValue("$spk", segments[i].Speaker.Index);
            await s.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Saklanmış konuşmacı bölgeleri (sıralı). ISessionStore dışı ek yetenek (10b).</summary>
    public async Task<IReadOnlyList<SpeakerSegment>> GetSpeakerSegmentsAsync(Guid sessionId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT start_ms, end_ms, speaker_index FROM speaker_segments WHERE session_id = $id ORDER BY seq";
        cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));

        var list = new List<SpeakerSegment>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new SpeakerSegment(
                new SpeakerId(rd.GetInt32(2)),
                TimeSpan.FromMilliseconds(rd.GetInt64(0)),
                TimeSpan.FromMilliseconds(rd.GetInt64(1))));
        return list;
    }

    /// <summary>Konuşmacı-başına duygu özeti (10c) için chunk-düzeyi füzyon sonuçları.
    /// Oturum yoksa boş liste döner — <see cref="GetSessionAsync"/> gibi <c>null</c> değil,
    /// çağıran (aggregator) zaten boş girdiyle güvenle çalışır. ISessionStore dışı ek yetenek.</summary>
    public async Task<IReadOnlyList<ChunkResult>> GetChunkResultsAsync(Guid sessionId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await ReadChunksAsync(conn, sessionId, ct).ConfigureAwait(false);
    }

    /// <summary>Ham sesi şifreleyip saklar ve oturuma bağlar (retention'a tabi). ISessionStore dışı ek yetenek.</summary>
    public async Task<string> SaveAudioAsync(Guid sessionId, byte[] rawAudio, CancellationToken ct)
    {
        var path = _audio.Write(sessionId, rawAudio);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using (var s = conn.CreateCommand())
        {
            s.CommandText = "INSERT OR IGNORE INTO sessions (id, started_at, language) VALUES ($id, $ts, $lang)";
            s.Parameters.AddWithValue("$id", sessionId.ToString("N"));
            s.Parameters.AddWithValue("$ts", _clock().ToUnixTimeMilliseconds());
            s.Parameters.AddWithValue("$lang", (int)DetectedLanguage.Other);
            await s.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET audio_path = $p, audio_saved_at = $t WHERE id = $id";
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$t", _clock().ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return path;
    }

    public async Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        long startedAt; int lang;
        await using (var s = conn.CreateCommand())
        {
            s.CommandText = "SELECT started_at, language FROM sessions WHERE id = $id";
            s.Parameters.AddWithValue("$id", id.ToString("N"));
            await using var rd = await s.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
            startedAt = rd.GetInt64(0);
            lang = rd.GetInt32(1);
        }

        var chunks = await ReadChunksAsync(conn, id, ct).ConfigureAwait(false);
        return new MeetingSession(id, DateTimeOffset.FromUnixTimeMilliseconds(startedAt), (DetectedLanguage)lang, chunks);
    }

    public async Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var meta = new List<(Guid id, long startedAt, int lang)>();
        await using (var s = conn.CreateCommand())
        {
            s.CommandText = "SELECT id, started_at, language FROM sessions ORDER BY started_at";
            await using var rd = await s.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                meta.Add((Guid.ParseExact(rd.GetString(0), "N"), rd.GetInt64(1), rd.GetInt32(2)));
        }

        var result = new List<MeetingSession>(meta.Count);
        foreach (var (id, startedAt, lang) in meta)
        {
            var chunks = await ReadChunksAsync(conn, id, ct).ConfigureAwait(false);
            result.Add(new MeetingSession(id, DateTimeOffset.FromUnixTimeMilliseconds(startedAt), (DetectedLanguage)lang, chunks));
        }
        return result;
    }

    public async Task PurgeExpiredAudioAsync(CancellationToken ct)
    {
        long cutoff = _clock().AddDays(-_retentionDays).ToUnixTimeMilliseconds();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var expired = new List<(string id, string path)>();
        await using (var s = conn.CreateCommand())
        {
            s.CommandText = "SELECT id, audio_path FROM sessions WHERE audio_path IS NOT NULL AND audio_saved_at < $cut";
            s.Parameters.AddWithValue("$cut", cutoff);
            await using var rd = await s.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
                expired.Add((rd.GetString(0), rd.GetString(1)));
        }

        foreach (var (id, path) in expired)
        {
            _audio.Delete(path);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE sessions SET audio_path = NULL, audio_saved_at = NULL WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<ChunkResult>> ReadChunksAsync(SqliteConnection conn, Guid id, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT time_start_ms, time_end_ms, transcript, combined_valence, combined_arousal,
                primary_emotion, secondary_emotion, confidence, agreement, incongruent, pattern,
                congruence, text_valence, audio_valence,
                text_arousal, audio_arousal, text_confidence, audio_confidence
            FROM chunks WHERE session_id = $sid ORDER BY seq
            """;
        cmd.Parameters.AddWithValue("$sid", id.ToString("N"));

        var chunks = new List<ChunkResult>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            var fused = new FusedResult(
                new ValenceArousal(rd.GetDouble(3), rd.GetDouble(4)),
                (Emotion)rd.GetInt32(5), (Emotion)rd.GetInt32(6), (Confidence)rd.GetInt32(7),
                rd.GetInt32(8) != 0, rd.GetInt32(9) != 0, (IncongruencePattern)rd.GetInt32(10),
                rd.GetDouble(11),
                TextVa: new ValenceArousal(rd.GetDouble(12), rd.GetDouble(14)),
                ProsodyVa: new ValenceArousal(rd.GetDouble(13), rd.GetDouble(15)),
                TextConfidence: rd.GetDouble(16),
                ProsodyConfidence: rd.GetDouble(17));
            chunks.Add(new ChunkResult(
                TimeSpan.FromMilliseconds(rd.GetInt64(0)), TimeSpan.FromMilliseconds(rd.GetInt64(1)),
                fused, rd.GetString(2)));
        }
        return chunks;
    }
}
