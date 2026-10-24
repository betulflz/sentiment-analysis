using Microsoft.Data.Sqlite;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Gerçek <see cref="ISessionStore"/>: metrik/transkript SQLite'ta kalıcı, ham ses şifreli dosyada
/// (<see cref="EncryptedAudioStore"/>). Retention dolunca ses silinir, metrik korunur. Bağlantı-başına-işlem
/// (SQLite dosya kilidi ile serileşir). Metadata sınırı: StartedAt = ilk kayıt (enjekte saat), Language = Other
/// (ChunkResult taşımaz; toplantı metadata'sı 06 pipeline'da).</summary>
public sealed class SqliteSessionStore : ISessionStore, ISessionAudioSink, IDiarizationStore, ISpeakerDataReader, IDecisionStore
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

    private static readonly string[] DecisionTables =
        ["decision_sessions", "decision_weights", "decision_windows", "window_indicators", "window_faces"];

    /// <summary>Oturumun karar kaydını (11e) tek transaction'da yazar; önce eskisini siler — aynı oturum
    /// yeniden kaydedilince satırlar çoğalmaz. Pencere <c>seq</c> = liste sırası.</summary>
    public async Task SaveDecisionSessionAsync(Guid sessionId, DecisionSessionRecord record, CancellationToken ct)
    {
        var sid = sessionId.ToString("N");
        var session = record.Session;
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        async Task Exec(string sql, params (string Name, object? Value)[] args)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$sid", sid);
            foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Tablo adları sabit listeden gelir (kullanıcı girdisi değil).
        foreach (var table in DecisionTables)
            await Exec($"DELETE FROM {table} WHERE session_id = $sid").ConfigureAwait(false);

        await Exec("INSERT INTO decision_sessions (session_id, profile, followup_action, followup_reason) VALUES ($sid, $p, $f, $r)",
            ("$p", (int)session.Profile), ("$f", (int)record.FollowUp), ("$r", record.FollowUpReason)).ConfigureAwait(false);

        foreach (var (kind, weights) in new[] { (0, session.ProfileWeights), (1, session.FinalWeights) })
            for (int j = 0; j < weights.Count; j++)
                await Exec("INSERT INTO decision_weights (session_id, kind, criterion, weight) VALUES ($sid, $k, $c, $w)",
                    ("$k", kind), ("$c", j), ("$w", weights[j])).ConfigureAwait(false);

        for (int i = 0; i < session.Windows.Count; i++)
        {
            var w = session.Windows[i];
            var r = w.Recommendation;
            await Exec("""
                INSERT INTO decision_windows (session_id, seq, start_ms, end_ms, data_quality,
                    action, closeness, confidence, face, top1, top2, reason)
                VALUES ($sid, $seq, $st, $en, $q, $a, $cl, $cf, $face, $t1, $t2, $reason)
                """,
                ("$seq", i), ("$st", (long)w.Start.TotalMilliseconds), ("$en", (long)w.End.TotalMilliseconds),
                ("$q", w.Indicators.DataQuality), ("$a", (int)r.Action), ("$cl", r.Closeness),
                ("$cf", (int)r.Confidence), ("$face", r.Face),
                ("$t1", r.TopCriteria.Count > 0 ? (int)r.TopCriteria[0] : null),
                ("$t2", r.TopCriteria.Count > 1 ? (int)r.TopCriteria[1] : null),
                ("$reason", r.Reason)).ConfigureAwait(false);

            for (int j = 0; j < w.Indicators.Values.Count; j++)
                await Exec("INSERT INTO window_indicators (session_id, window_seq, criterion, value, source_face) VALUES ($sid, $seq, $c, $v, $sf)",
                    ("$seq", i), ("$c", j), ("$v", w.Indicators.Values[j]), ("$sf", w.Indicators.SourceFace[j])).ConfigureAwait(false);

            foreach (var f in w.Faces)
                await Exec("""
                    INSERT INTO window_faces (session_id, window_seq, face_index, visible_ratio, look_away_ratio,
                        valence_shift, negative_expression, smile_ratio, reliability)
                    VALUES ($sid, $seq, $fi, $vr, $la, $vs, $ne, $sm, $rl)
                    """,
                    ("$seq", i), ("$fi", f.FaceIndex), ("$vr", f.VisibleRatio), ("$la", f.LookAwayRatio),
                    ("$vs", f.ValenceShift), ("$ne", f.NegativeExpression), ("$sm", f.SmileRatio),
                    ("$rl", f.Reliability)).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Oturumun karar kaydı (11e); yoksa <c>null</c>. <c>Recommendation.Ranking</c> saklanmaz, boş döner.</summary>
    public async Task<DecisionSessionRecord?> GetDecisionSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var sid = sessionId.ToString("N");
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        async Task Read(string sql, Action<SqliteDataReader> row)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$sid", sid);
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) row(rd);
        }

        (MeetingProfile Profile, FollowUpAction FollowUp, string Reason)? head = null;
        await Read("SELECT profile, followup_action, followup_reason FROM decision_sessions WHERE session_id = $sid",
            rd => head = ((MeetingProfile)rd.GetInt32(0), (FollowUpAction)rd.GetInt32(1), rd.GetString(2))).ConfigureAwait(false);
        if (head is not { } h) return null;

        var weights = new[] { new List<double>(), new List<double>() };
        await Read("SELECT kind, weight FROM decision_weights WHERE session_id = $sid ORDER BY kind, criterion",
            rd => weights[rd.GetInt32(0)].Add(rd.GetDouble(1))).ConfigureAwait(false);

        var values = new Dictionary<int, (List<double> V, List<int?> F)>();
        await Read("SELECT window_seq, value, source_face FROM window_indicators WHERE session_id = $sid ORDER BY window_seq, criterion",
            rd =>
            {
                var seq = rd.GetInt32(0);
                if (!values.TryGetValue(seq, out var x)) values[seq] = x = (new List<double>(), new List<int?>());
                x.V.Add(rd.GetDouble(1));
                x.F.Add(rd.IsDBNull(2) ? null : rd.GetInt32(2));
            }).ConfigureAwait(false);

        var faces = new Dictionary<int, List<FaceWindowSummary>>();
        await Read("""
            SELECT window_seq, face_index, visible_ratio, look_away_ratio, valence_shift, negative_expression,
                smile_ratio, reliability
            FROM window_faces WHERE session_id = $sid ORDER BY window_seq, face_index
            """,
            rd =>
            {
                var seq = rd.GetInt32(0);
                if (!faces.TryGetValue(seq, out var list)) faces[seq] = list = new List<FaceWindowSummary>();
                list.Add(new FaceWindowSummary(rd.GetInt32(1), rd.GetDouble(2), rd.GetDouble(3), rd.GetDouble(4),
                    rd.GetDouble(5), rd.GetDouble(6), rd.GetDouble(7)));
            }).ConfigureAwait(false);

        var windows = new List<DecisionWindow>();
        await Read("""
            SELECT seq, start_ms, end_ms, data_quality, action, closeness, confidence, face, top1, top2, reason
            FROM decision_windows WHERE session_id = $sid ORDER BY seq
            """,
            rd =>
            {
                var seq = rd.GetInt32(0);
                var top = new List<Criterion>();
                if (!rd.IsDBNull(8)) top.Add((Criterion)rd.GetInt32(8));
                if (!rd.IsDBNull(9)) top.Add((Criterion)rd.GetInt32(9));
                var ind = values.TryGetValue(seq, out var x) ? x : (new List<double>(), new List<int?>());
                windows.Add(new DecisionWindow(
                    TimeSpan.FromMilliseconds(rd.GetInt64(1)), TimeSpan.FromMilliseconds(rd.GetInt64(2)),
                    new Indicators(ind.Item1, ind.Item2, rd.GetDouble(3)),
                    new Recommendation((LiveAction)rd.GetInt32(4), rd.GetDouble(5), Array.Empty<Ranked<LiveAction>>(),
                        top, rd.IsDBNull(7) ? null : rd.GetInt32(7), (Confidence)rd.GetInt32(6), rd.GetString(10)))
                {
                    Faces = faces.TryGetValue(seq, out var f) ? f : Array.Empty<FaceWindowSummary>(),
                });
            }).ConfigureAwait(false);

        return new DecisionSessionRecord(
            new DecisionSession(h.Profile, weights[0], weights[1], windows), h.FollowUp, h.Reason);
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
