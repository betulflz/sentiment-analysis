using Microsoft.Data.Sqlite;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>
/// Şema evrimi için küçük, idempotent yardımcılar.
/// <para><c>CREATE TABLE IF NOT EXISTS</c> <b>var olan</b> bir tabloya sütun eklemez: kullanıcının
/// diskindeki eski <c>sessions.db</c> yeni sürümle açıldığında sütun eksik kalır ve okuma
/// <c>SqliteException: no such column</c> ile patlar. Bu yüzden şema kurulumundan sonra eksik
/// sütunlar burada tamamlanır.</para>
/// </summary>
public static class SqliteMigrations
{
    /// <summary>Eklenecek sütun: adı ve <c>ALTER TABLE</c> tanımı (NOT NULL ise DEFAULT şart —
    /// SQLite dolu bir tabloya varsayılansız NOT NULL sütun eklemeye izin vermez).</summary>
    public readonly record struct ColumnSpec(string Name, string Definition);

    /// <summary>
    /// <paramref name="table"/> üzerinde eksik olan sütunları ekler. Var olanlara dokunmaz,
    /// ikinci çalıştırmada hiçbir şey yapmaz (idempotent).
    /// </summary>
    public static void EnsureColumns(SqliteConnection conn, string table, IReadOnlyList<ColumnSpec> columns)
    {
        var existing = GetColumnNames(conn, table);
        if (existing.Count == 0) return;   // tablo yok → CreateSql zaten güncel şemayla kuracak

        foreach (var col in columns)
        {
            if (existing.Contains(col.Name)) continue;

            using var cmd = conn.CreateCommand();
            // Tablo/sütun adları parametrelenemez (SQLite DDL kısıtı); bu adlar kullanıcı girdisi
            // değil, kaynak kodda sabit — enjeksiyon yüzeyi yok.
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {col.Name} {col.Definition}";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Tablodaki sütun adları (büyük/küçük harfe duyarsız). Tablo yoksa boş küme.</summary>
    public static HashSet<string> GetColumnNames(SqliteConnection conn, string table)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var rd = cmd.ExecuteReader();
        while (rd.Read()) names.Add(rd.GetString(1));   // 1 = name
        return names;
    }
}
