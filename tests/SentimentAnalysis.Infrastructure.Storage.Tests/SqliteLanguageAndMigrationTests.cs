using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

/// <summary>
/// Dilin uçtan uca kalıcı olması ve eski şemalı veritabanlarının geçişi.
/// Önceki sürümde <c>sessions.language</c> her zaman <see cref="DetectedLanguage.Other"/>
/// yazılıyordu — chunk'lar dili taşımadığı için.
/// </summary>
public class SqliteLanguageAndMigrationTests
{
    private static (SqliteSessionStore store, string dir) NewStore(string? dir = null)
    {
        dir ??= Path.Combine(Path.GetTempPath(), "sa-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };
        var audio = new EncryptedAudioStore(cfg, new FileKeyProvider(cfg));
        return (new SqliteSessionStore(cfg, audio), dir);
    }

    private static ChunkResult Chunk(double start, double end, string text, DetectedLanguage lang) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end),
            new FusedResult(new ValenceArousal(0.3, 0.6), Emotion.Mutlu, Emotion.Sakin,
                Confidence.Yuksek, true, false, IncongruencePattern.Yok, 0.9, 0.3, 0.2),
            text, lang);

    [Fact]
    public async Task ChunkLanguage_RoundTrips()
    {
        var (store, _) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Chunk(0, 5, "merhaba", DetectedLanguage.Turkish), CancellationToken.None);
        await store.SaveChunkAsync(id, Chunk(5, 10, "hello", DetectedLanguage.English), CancellationToken.None);

        var session = await store.GetSessionAsync(id, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal(DetectedLanguage.Turkish, session!.Chunks[0].Language);
        Assert.Equal(DetectedLanguage.English, session.Chunks[1].Language);
    }

    [Fact]
    public async Task SessionLanguage_IsDerivedFromChunks_NotHardcodedOther()
    {
        var (store, _) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Chunk(0, 30, "uzun türkçe", DetectedLanguage.Turkish), CancellationToken.None);
        await store.SaveChunkAsync(id, Chunk(30, 33, "short", DetectedLanguage.English), CancellationToken.None);

        var session = await store.GetSessionAsync(id, CancellationToken.None);

        Assert.Equal(DetectedLanguage.Turkish, session!.Language);
    }

    [Fact]
    public async Task ListSessions_AlsoDerivesLanguage()
    {
        var (store, _) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Chunk(0, 5, "hello", DetectedLanguage.English), CancellationToken.None);

        var list = await store.ListSessionsAsync(CancellationToken.None);

        Assert.Single(list);
        Assert.Equal(DetectedLanguage.English, list[0].Language);
    }

    [Fact]
    public async Task OldDatabaseWithoutLanguageColumn_IsMigratedAndStillReadable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "sessions.db");

        // 1) Eski şemayı (language sütunu YOK) elle kur ve bir satır yaz.
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        await using (var conn = new SqliteConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE sessions (
                    id TEXT PRIMARY KEY, started_at INTEGER NOT NULL, language INTEGER NOT NULL,
                    audio_path TEXT, audio_saved_at INTEGER);
                CREATE TABLE chunks (
                    session_id TEXT NOT NULL, seq INTEGER NOT NULL,
                    time_start_ms INTEGER NOT NULL, time_end_ms INTEGER NOT NULL, transcript TEXT NOT NULL,
                    combined_valence REAL NOT NULL, combined_arousal REAL NOT NULL,
                    primary_emotion INTEGER NOT NULL, secondary_emotion INTEGER NOT NULL,
                    confidence INTEGER NOT NULL, agreement INTEGER NOT NULL, incongruent INTEGER NOT NULL,
                    pattern INTEGER NOT NULL, congruence REAL NOT NULL,
                    text_valence REAL NOT NULL, audio_valence REAL NOT NULL,
                    PRIMARY KEY (session_id, seq));
                INSERT INTO sessions (id, started_at, language) VALUES ('0123456789abcdef0123456789abcdef', 0, 0);
                INSERT INTO chunks VALUES ('0123456789abcdef0123456789abcdef', 0, 0, 1000, 'eski satir', 0.1, 0.5, 1, 2, 1, 1, 0, 0, 0.8, 0.1, 0.2);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        // 2) Yeni sürümle aç → geçiş sütunu eklemeli, eski satır okunabilir kalmalı.
        var (store, _) = NewStore(dir);

        await using (var conn = new SqliteConnection(cs))
        {
            await conn.OpenAsync();
            var cols = SqliteMigrations.GetColumnNames(conn, "chunks");
            Assert.Contains("language", cols);
        }

        var sessions = await store.ListSessionsAsync(CancellationToken.None);
        Assert.Single(sessions);
        Assert.Equal("eski satir", sessions[0].Chunks[0].Transcript);
        // Eski satırda dil ölçülmemişti → uydurma değil, Other.
        Assert.Equal(DetectedLanguage.Other, sessions[0].Chunks[0].Language);
    }

    [Fact]
    public void EnsureColumns_IsIdempotent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-idem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "x.db") }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE t (a INTEGER NOT NULL)";
            cmd.ExecuteNonQuery();
        }

        var spec = new[] { new SqliteMigrations.ColumnSpec("b", "INTEGER NOT NULL DEFAULT 7") };
        SqliteMigrations.EnsureColumns(conn, "t", spec);
        SqliteMigrations.EnsureColumns(conn, "t", spec);   // ikinci kez → hata vermemeli

        Assert.Contains("b", SqliteMigrations.GetColumnNames(conn, "t"));
    }

    [Fact]
    public void EnsureColumns_MissingTable_IsNoOp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-notable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "x.db") }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();

        // Tablo yoksa ALTER denenmemeli (CreateSql zaten güncel şemayla kuracak).
        SqliteMigrations.EnsureColumns(conn, "yok_boyle_tablo",
            new[] { new SqliteMigrations.ColumnSpec("b", "INTEGER NOT NULL DEFAULT 0") });
    }
}
