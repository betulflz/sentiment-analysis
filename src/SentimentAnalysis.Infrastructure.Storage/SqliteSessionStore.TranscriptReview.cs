using SentimentAnalysis.Core;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace SentimentAnalysis.Infrastructure.Storage;

public sealed partial class SqliteSessionStore
{
    public async Task<IReadOnlyList<ReviewedTranscriptLine>?> GetTranscriptReviewAsync(Guid sessionId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT content FROM transcript_reviews WHERE session_id = $id";
        cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));
        var content = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return content is string json
            ? JsonSerializer.Deserialize<ReviewedTranscriptLine[]>(json)
                ?? throw new JsonException("Düzeltme kaydı boş.")
            : null;
    }

    public async Task SaveTranscriptReviewAsync(Guid sessionId, IReadOnlyList<ReviewedTranscriptLine> lines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(l => l is null || l.Start < TimeSpan.Zero || l.End <= l.Start
            || l.Text is null || l.Speaker is null))
            throw new ArgumentException("Düzeltme satırları ve zamanları geçerli olmalı.", nameof(lines));
        var content = JsonSerializer.Serialize(lines);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        // Tek SQL ifadesi: eski düzeltme hata/iptalde yarım kayıtla değiştirilmez.
        cmd.CommandText = """
            INSERT INTO transcript_reviews (session_id, content)
            SELECT id, $content FROM sessions WHERE id = $id
            ON CONFLICT(session_id) DO UPDATE SET content = excluded.content
            """;
        cmd.Parameters.AddWithValue("$id", sessionId.ToString("N"));
        cmd.Parameters.AddWithValue("$content", content);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new KeyNotFoundException("Oturum bulunamadı.");
    }
}
