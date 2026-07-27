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
            PRIMARY KEY (session_id, seq)
        );
        """;
}
