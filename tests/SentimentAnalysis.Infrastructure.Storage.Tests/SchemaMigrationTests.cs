using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class SchemaMigrationTests : IDisposable
{
    private readonly string _dir;

    public SchemaMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sa-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>08d ÖNCESİ şema — dört yeni sütun yok.</summary>
    private const string LegacyCreateSql = """
        CREATE TABLE IF NOT EXISTS sessions (
            id TEXT PRIMARY KEY,
            started_at INTEGER NOT NULL,
            language INTEGER NOT NULL,
            audio_path TEXT,
            audio_saved_at INTEGER
        );
        CREATE TABLE IF NOT EXISTS chunks (
            session_id TEXT NOT NULL,
            seq INTEGER NOT NULL,
            time_start_ms INTEGER NOT NULL,
            time_end_ms INTEGER NOT NULL,
            transcript TEXT NOT NULL,
            combined_valence REAL NOT NULL,
            combined_arousal REAL NOT NULL,
            primary_emotion INTEGER NOT NULL,
            secondary_emotion INTEGER NOT NULL,
            confidence INTEGER NOT NULL,
            agreement INTEGER NOT NULL,
            incongruent INTEGER NOT NULL,
            pattern INTEGER NOT NULL,
            congruence REAL NOT NULL,
            text_valence REAL NOT NULL,
            audio_valence REAL NOT NULL,
            PRIMARY KEY (session_id, seq)
        );
        """;

    private AppConfig Config() => AppConfig.Default with
    {
        Storage = AppConfig.Default.Storage with { StorageDirectory = _dir },
    };

    private SqliteSessionStore NewStore()
    {
        var cfg = Config();
        var audio = new EncryptedAudioStore(cfg, new FileKeyProvider(cfg));
        return new SqliteSessionStore(cfg, audio);
    }

    private void SeedLegacyDatabase(Guid sessionId)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_dir, "sessions.db") }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = LegacyCreateSql;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO sessions (id, started_at, language) VALUES ($id, 0, 0);
                INSERT INTO chunks (session_id, seq, time_start_ms, time_end_ms, transcript,
                    combined_valence, combined_arousal, primary_emotion, secondary_emotion,
                    confidence, agreement, incongruent, pattern, congruence, text_valence, audio_valence)
                VALUES ($id, 0, 0, 1000, 'eski kayıt', 0.4, 0.6, 1, 7, 1, 1, 0, 0, 0.9, 0.5, 0.3);
                """;
            cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));
            cmd.ExecuteNonQuery();
        }
    }

    private List<string> ChunkColumns()
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_dir, "sessions.db") }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(chunks)";
        using var rd = cmd.ExecuteReader();
        var names = new List<string>();
        while (rd.Read()) names.Add(rd.GetString(1));
        return names;
    }

    [Fact]
    public void Ctor_OnLegacyDatabase_AddsMissingColumns()
    {
        SeedLegacyDatabase(Guid.NewGuid());

        _ = NewStore();   // yapıcı geçişi çalıştırır

        var columns = ChunkColumns();
        Assert.Contains("text_arousal", columns);
        Assert.Contains("audio_arousal", columns);
        Assert.Contains("text_confidence", columns);
        Assert.Contains("audio_confidence", columns);
    }

    [Fact]
    public async Task Ctor_OnLegacyDatabase_OldRowsRemainReadable()
    {
        var id = Guid.NewGuid();
        SeedLegacyDatabase(id);

        var store = NewStore();
        var session = await store.GetSessionAsync(id, CancellationToken.None);

        Assert.NotNull(session);
        var chunk = Assert.Single(session!.Chunks);
        Assert.Equal("eski kayıt", chunk.Transcript);
        Assert.Equal(0.5, chunk.Fused.TextVa.Valence, precision: 6);   // eski sütun korunur
        Assert.Equal(0.0, chunk.Fused.TextVa.Arousal, precision: 6);   // yeni sütun varsayılanı: ölçülmedi
    }

    [Fact]
    public void Ctor_RunTwice_IsIdempotent()
    {
        SeedLegacyDatabase(Guid.NewGuid());

        _ = NewStore();
        _ = NewStore();   // ikinci kez: "duplicate column name" hatası VERMEMELİ

        Assert.Contains("text_arousal", ChunkColumns());
    }

    [Fact]
    public async Task NewDatabase_RoundTripsAllFourNewFields()
    {
        var store = NewStore();
        var id = Guid.NewGuid();
        var fused = new FusedResult(
            new ValenceArousal(0.1, 0.7), Emotion.Mutlu, Emotion.Sakin, Confidence.Orta,
            true, false, IncongruencePattern.Yok, 0.85,
            TextVa: new ValenceArousal(0.62, 0.41),
            ProsodyVa: new ValenceArousal(-0.33, 0.88),
            TextConfidence: 0.77,
            ProsodyConfidence: 0.44);

        await store.SaveChunkAsync(id, new ChunkResult(TimeSpan.Zero, TimeSpan.FromSeconds(1), fused, "merhaba"),
            CancellationToken.None);
        var session = await store.GetSessionAsync(id, CancellationToken.None);

        var read = Assert.Single(session!.Chunks).Fused;
        Assert.Equal(0.62, read.TextVa.Valence, precision: 6);
        Assert.Equal(0.41, read.TextVa.Arousal, precision: 6);
        Assert.Equal(-0.33, read.ProsodyVa.Valence, precision: 6);
        Assert.Equal(0.88, read.ProsodyVa.Arousal, precision: 6);
        Assert.Equal(0.77, read.TextConfidence, precision: 6);
        Assert.Equal(0.44, read.ProsodyConfidence, precision: 6);
    }
}
