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
            text_arousal REAL NOT NULL DEFAULT 0,
            audio_arousal REAL NOT NULL DEFAULT 0,
            text_confidence REAL NOT NULL DEFAULT 0,
            audio_confidence REAL NOT NULL DEFAULT 0,
            PRIMARY KEY (session_id, seq)
        );
        CREATE TABLE IF NOT EXISTS transcript_segments (
            session_id TEXT NOT NULL,
            seq INTEGER NOT NULL,               -- oturum içi sıra (chunk'lar arası sürer)
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
        """;

    /// <summary>08d ile eklenen `chunks` sütunları. Mevcut veritabanlarına
    /// <c>ALTER TABLE</c> ile eklenir (bkz. <c>SqliteSessionStore</c> geçişi).
    /// Varsayılan 0 = "bu satır 08d öncesinden, değer ölçülmedi" — uydurma değer üretilmez.</summary>
    public static readonly (string Name, string Definition)[] ChunkColumnsAddedIn08d =
    [
        ("text_arousal",     "REAL NOT NULL DEFAULT 0"),
        ("audio_arousal",    "REAL NOT NULL DEFAULT 0"),
        ("text_confidence",  "REAL NOT NULL DEFAULT 0"),
        ("audio_confidence", "REAL NOT NULL DEFAULT 0"),
    ];

    /// <summary>10b ile eklenen tablolar (konuşmacı ayrımı). Yeni SÜTUN değil yeni TABLO
    /// oldukları için <c>CREATE TABLE IF NOT EXISTS</c> eski veritabanlarında da onları
    /// kendiliğinden oluşturur — 08d'deki ALTER geçişine gerek yoktur.</summary>
    public static readonly string[] TablesAddedIn10b = ["transcript_segments", "speaker_segments"];
}
