# Alt-Proje 07 — Depolama + Gizlilik

> Bağımlılık: 01 (+06 `ISessionStore` sözleşmesi) · İnşa sırası: **7** · Katman: `Infrastructure/Storage`
> **Durum:** Brainstorm ile somutlaştırıldı (2026-07-25). Bu tur **SQLite metrik store + retention +
> AES-GCM ses şifreleme**; **Opus sıkıştırma + OS keychain anahtar** → 07b'ye ertelenir (arayüz hazır).
> Yeni kütüphane belirsizliği yok (Microsoft.Data.Sqlite + `AesGcm` BCL) → spike gerekmez.

## Hedef
Katmanlı, gizlilik-öncelikli kalıcılık: metrikler kalıcı (SQLite), ses geçici + şifreli (D13).

## Sözleşme (Alt-Proje 01/06'da YERLEŞİK — değişmez)
```csharp
public interface ISessionStore {
    Task SaveChunkAsync(Guid sessionId, ChunkResult r, CancellationToken ct);
    Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct);
    Task PurgeExpiredAudioAsync(CancellationToken ct);   // saklama politikası
}
```

## Kapsam (bu tur)
- **`SqliteSessionStore : ISessionStore`** (Microsoft.Data.Sqlite, cross-platform, osx-arm64 native bundled):
  oturum + chunk satırları (FusedResult metrikleri + transkript + zaman-damgaları) kalıcı. Şema açılışta kurulur.
  `MeetingSession` metadata (StartedAt/Language) `sessions` tablosunda; `SaveChunkAsync` oturum satırını
  ilk chunk'ta oluşturur: **StartedAt = enjekte saat (ilk kayıt anı), Language = `DetectedLanguage.Other`**
  (ChunkResult metadata taşımaz — `InMemorySessionStore` ile birebir aynı davranış; toplantı-düzeyi gerçek
  metadata'yı 06 pipeline kendi `MeetingSession`'ında zaten yönetir). Metadata sınırı dokümante, tutarlı.
- **Retention/purge:** `PurgeExpiredAudioAsync` — retention (varsayılan `StorageConfig.AudioRetentionDays`=7,
  enjekte saat) dolmuş oturumların **ses dosyalarını siler**, DB metrik satırlarını **korur** (ses ref'i null'lanır).
- **Ses şifreleme:** ham ses bytes → **AES-256-GCM** (`AesGcm`, BCL) → şifreli dosya (nonce+tag+ciphertext).
  Anahtarsız okunamaz. `EncryptedAudioStore` dosyayı yazar/okur/siler; DB'den sessionId ile referanslı.
- **Anahtar:** `IKeyProvider` arkasında; bu tur `FileKeyProvider` (app-data'da 32-byte anahtar dosyası,
  yoksa üretir, izin 0600). **OS keychain/DPAPI → 07b** aynı arayüz ardında.
- **`StorageConfig` (mevcut) additive genişletme:** `StorageDirectory` (db + ses + anahtar konumu; null →
  uygulama-veri varsayılanı). Mevcut `AudioRetentionDays`/`KeepAudio`/`OpusBitrate` değişmez.
- **`AddSqliteStorage()` DI:** `ISessionStore → SqliteSessionStore` (düz `AddSingleton` → 06'nın `TryAdd`
  InMemory'sini composition root sırasıyla ezer), `IKeyProvider`, `EncryptedAudioStore`.

## Kapsam DIŞI (bu tur, bilinçli — 07b)
- **Opus sıkıştırma** (~24 kbps, ~11 MB/saat) — bu tur şifreli ham/PCM saklanır; Opus (Concentus/native) → 07b.
- **OS keychain/DPAPI anahtar saklama** — `IKeyProvider` arkasında, 07b.
- Gelişmiş sorgu/sayfalama; ses oynatma; senkronizasyon.

## Bileşenler (`Infrastructure.Storage`)
- `StorageSchema` (SQL DDL) · `SqliteSessionStore : ISessionStore` · `IKeyProvider` + `FileKeyProvider` ·
  `AudioEncryptor` (AES-GCM şifrele/çöz) · `EncryptedAudioStore` (şifreli dosya yaz/oku/sil) · `AddSqliteStorage()`.
- Native tip (SqliteConnection) motor sınırında; `AudioEncryptor` saf (anahtar parametreli) → unit-testli.

## Kabul Kriteri (bu tur)
- `PurgeExpiredAudioAsync` süresi dolan sesi siler, metrikleri korur (temp-db + enjekte saat testi).
- Şifreli ses dosyası anahtarsız okunamaz (yanlış anahtar → `AuthenticationTagMismatchException`/çözülemez).
- SaveChunk → GetSession roundtrip chunk metriklerini + transkripti + zamanları korur.
- `ListSessionsAsync` kayıtlı oturumları döner.
- Tüm testler modelsiz yeşil (temp SQLite dosyası + temp dizinler).

## Kabul Kriteri (07b — ertelenmiş)
- 1 saatlik toplantı ses boyutu ~11 MB (Opus doğrulaması).
- Şifreleme anahtarı OS keychain/DPAPI'de; düz metin anahtar diskte yok.
