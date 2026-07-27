namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>SQLite şeması. `sessions` (metadata + ses ref) + `chunks` (metrik/transkript zaman çizelgesi).
/// Ses `audio_path` null'lanabilir (purge sonrası); metrik satırları kalıcı.</summary>
public static class StorageSchema
{
    public const string CreateSql = """
        CREATE TABLE IF NOT EXISTS sessions (
            id TEXT PRIMARY KEY,
            started_at INTEGER NOT NULL,        -- unix ms
            language INTEGER NOT NULL,
            audio_path TEXT,
            audio_saved_at INTEGER              -- unix ms; null = ses yok
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
            language INTEGER NOT NULL DEFAULT 2,        -- DetectedLanguage; 2 = Other
            text_arousal REAL NOT NULL DEFAULT 0.5,
            audio_arousal REAL NOT NULL DEFAULT 0.5,
            text_confidence REAL NOT NULL DEFAULT 0,
            audio_confidence REAL NOT NULL DEFAULT 0,
            text_reliability REAL NOT NULL DEFAULT 0,
            audio_reliability REAL NOT NULL DEFAULT 0,
            PRIMARY KEY (session_id, seq)
        );
        """;

    /// <summary>
    /// <c>chunks</c> tablosuna sonradan eklenen sütunlar. Yeni veritabanlarında
    /// <see cref="CreateSql"/> bunları zaten kurar; <b>var olan</b> veritabanlarında
    /// <see cref="SqliteMigrations.EnsureColumns"/> tamamlar.
    /// </summary>
    public static IReadOnlyList<SqliteMigrations.ColumnSpec> ChunkColumnMigrations { get; } = new[]
    {
        // Chunk-başına algılanan dil (oturum dili bundan türetilir). Eski satırlarda
        // ölçülmediği için Other(2) — uydurma bir dil iddia edilmez.
        new SqliteMigrations.ColumnSpec("language", "INTEGER NOT NULL DEFAULT 2"),

        // Spec 08d Dilim 2: üçlü görünüm (Metin/Ses/Nihai) için iki kanalın tam çıktısı
        // ve onlara verilen füzyon ağırlıkları. Eski satırlarda bunlar ölçülmemişti;
        // varsayılanlar "bilinmiyor" anlamına gelir, okuma tarafı olduğu gibi döndürür.
        new SqliteMigrations.ColumnSpec("text_arousal", "REAL NOT NULL DEFAULT 0.5"),
        new SqliteMigrations.ColumnSpec("audio_arousal", "REAL NOT NULL DEFAULT 0.5"),
        new SqliteMigrations.ColumnSpec("text_confidence", "REAL NOT NULL DEFAULT 0"),
        new SqliteMigrations.ColumnSpec("audio_confidence", "REAL NOT NULL DEFAULT 0"),
        new SqliteMigrations.ColumnSpec("text_reliability", "REAL NOT NULL DEFAULT 0"),
        new SqliteMigrations.ColumnSpec("audio_reliability", "REAL NOT NULL DEFAULT 0"),
    };
}
