# Alt-Proje 07: Depolama + Gizlilik — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gizlilik-öncelikli kalıcılık: metrikler/transkript SQLite'ta kalıcı; ham ses AES-256-GCM ile şifreli dosyada, saklama süresi dolunca silinir (metrik korunur). `ISessionStore`'un gerçek (SQLite) implementasyonu.

**Architecture:** Yeni `SentimentAnalysis.Infrastructure.Storage` dilimi (master §9). `SqliteSessionStore : ISessionStore` (Microsoft.Data.Sqlite; native SqliteConnection motor sınırında, bağlantı-başına-işlem). Şifreleme: saf `AudioEncryptor` (AES-GCM, anahtar parametreli, unit-testli) + `EncryptedAudioStore` (dosya IO) + `IKeyProvider`/`FileKeyProvider` (bu tur dosya-anahtar; OS keychain → 07b). Opus sıkıştırma → 07b (bu tur şifreli ham/PCM). Bağımlılıklar içe: Infrastructure.Storage → Application → Core.

**Tech Stack:** .NET 9 · C# · xUnit 2.9.2 · Microsoft.Data.Sqlite 9.0.0 · `System.Security.Cryptography.AesGcm` (BCL)

## Global Constraints

- **Hedef framework:** `net9.0`. Nullable + ImplicitUsings açık.
- **Bağımlılık yönü:** Infrastructure.Storage → Application → Core. Core değişmez.
- **`git YOK`**: her task sonu `dotnet build && dotnet test`, commit değil.
- **Yerleşik sözleşme DEĞİŞMEZ (01/06):** `ISessionStore { Task SaveChunkAsync(Guid, ChunkResult, ct); Task<MeetingSession?> GetSessionAsync(Guid, ct); Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(ct); Task PurgeExpiredAudioAsync(ct); }`. Tipler: `ChunkResult(TimeSpan TimeStart, TimeSpan TimeEnd, FusedResult Fused, string Transcript)`, `FusedResult(ValenceArousal Combined, Emotion PrimaryEmotion, Emotion SecondaryEmotion, Confidence Confidence, bool Agreement, bool Incongruent, IncongruencePattern Pattern, double Congruence, double TextValence, double AudioValence)`, `MeetingSession(Guid Id, DateTimeOffset StartedAt, DetectedLanguage Language, IReadOnlyList<ChunkResult> Chunks)`.
- **Metadata:** `SaveChunkAsync` StartedAt/Language taşımaz → ilk chunk'ta oturum satırı: **StartedAt = enjekte saat, Language = `DetectedLanguage.Other`** (`InMemorySessionStore` ile birebir; toplantı-düzeyi gerçek metadata 06 pipeline'da).
- **AES-256-GCM:** anahtar 32 byte; nonce 12 byte (rastgele/kayıt); tag 16 byte. **Kurucu tag-size'lı olmalı:** `new AesGcm(key, tagSizeInBytes: 16)` (parametresiz kurucu SYSLIB0053 uyarısı verir — **0 uyarı şart**). Dosya düzeni: `[nonce(12)][tag(16)][ciphertext]`. Yanlış anahtar/kurcalama → `AuthenticationTagMismatchException`.
- **Anahtar:** `IKeyProvider.GetKey() → byte[32]`; `FileKeyProvider` app-veri/`StorageDirectory` altında `storage.key`; yoksa `RandomNumberGenerator.GetBytes(32)` ile üretir, Unix'te `File.SetUnixFileMode(..., 0600)`. **OS keychain/DPAPI → 07b** aynı arayüz.
- **Retention:** `PurgeExpiredAudioAsync` — `audio_saved_at < now − AudioRetentionDays` olan oturumların ses dosyasını siler + `audio_path` null'lar; **chunk/metrik satırları korunur**. Enjekte saat (`Func<DateTimeOffset>`) → test determinizmi.
- **SQLite:** bağlantı-başına-işlem (`using var conn = new SqliteConnection(cs); await conn.OpenAsync(ct);`), parametreli komut; şema açılışta (ctor) kurulur (`CREATE TABLE IF NOT EXISTS`). Thread-safety: SQLite dosya kilidi + bağlantı-başına-işlem.
- **`StorageConfig` (mevcut) additive:** yeni `StorageDirectory` (null → uygulama-veri varsayılanı). Mevcut `AudioRetentionDays`/`KeepAudio`/`OpusBitrate` değişmez.
- **`AddSqliteStorage()`:** `ISessionStore → SqliteSessionStore` **düz `AddSingleton`** (06 `TryAdd` InMemory'sini composition root sırasıyla ezer); `IKeyProvider → FileKeyProvider`, `EncryptedAudioStore` singleton.
- **Kapsam dışı (07b):** Opus sıkıştırma; OS keychain; ses oynatma. Bu tur şifreli ham/PCM saklanır.

### Mevcut tipler (tüketilir, değişmez)

```csharp
// Application.Configuration.StorageConfig (mevcut): (int AudioRetentionDays=7, bool KeepAudio=true, string OpusBitrate="24k")
// Core: ChunkResult, FusedResult, MeetingSession, ValenceArousal, Emotion, Confidence, IncongruencePattern, DetectedLanguage
```

### AES-GCM (Task 2; BCL, tag-size'lı kurucu)

```csharp
using System.Security.Cryptography;
// Encrypt:
var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);   // 12
var tag = new byte[AesGcm.TagByteSizes.MaxSize];                             // 16
var cipher = new byte[plaintext.Length];
using (var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize))               // tag-size ZORUNLU
    aes.Encrypt(nonce, plaintext, cipher, tag);
// blob = nonce || tag || cipher
```

---

### Task 1: `Infrastructure.Storage` iskeleti + `StorageConfig` genişletme + `IKeyProvider`/`FileKeyProvider`

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Storage/SentimentAnalysis.Infrastructure.Storage.csproj`
- Create: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/SentimentAnalysis.Infrastructure.Storage.Tests.csproj`
- Modify: `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` (`StorageConfig`'e `StorageDirectory` ekle — additive)
- Create: `src/SentimentAnalysis.Infrastructure.Storage/IKeyProvider.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Storage/FileKeyProvider.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/FileKeyProviderTests.cs`

**Interfaces:**
- Produces: `StorageConfig`'e `string? StorageDirectory = null` alanı (additive); `interface IKeyProvider { byte[] GetKey(); }`; `sealed class FileKeyProvider : IKeyProvider` (ctor `(AppConfig)`, `static string DefaultStorageDirectory()`).

- [ ] **Step 1: Projeleri oluştur ve çözüme ekle**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet new classlib -n SentimentAnalysis.Infrastructure.Storage -o src/SentimentAnalysis.Infrastructure.Storage -f net9.0
rm src/SentimentAnalysis.Infrastructure.Storage/Class1.cs
dotnet new xunit -n SentimentAnalysis.Infrastructure.Storage.Tests -o tests/SentimentAnalysis.Infrastructure.Storage.Tests -f net9.0
rm tests/SentimentAnalysis.Infrastructure.Storage.Tests/UnitTest1.cs
dotnet sln add src/SentimentAnalysis.Infrastructure.Storage tests/SentimentAnalysis.Infrastructure.Storage.Tests
dotnet add src/SentimentAnalysis.Infrastructure.Storage reference src/SentimentAnalysis.Application
dotnet add src/SentimentAnalysis.Infrastructure.Storage package Microsoft.Data.Sqlite --version 9.0.0
dotnet add tests/SentimentAnalysis.Infrastructure.Storage.Tests reference src/SentimentAnalysis.Infrastructure.Storage
```
csproj PropertyGroup'una `<Nullable>enable</Nullable>` + `<ImplicitUsings>enable</ImplicitUsings>` ekle (yoksa).

- [ ] **Step 2: `StorageConfig`'i genişlet (additive)**

`src/SentimentAnalysis.Application/Configuration/AppConfig.cs` — `StorageConfig` kaydına `StorageDirectory` ekle (mevcut alanları KORU). Son hali:

```csharp
public sealed record StorageConfig(
    int AudioRetentionDays = 7,
    bool KeepAudio = true,
    string OpusBitrate = "24k",
    string? StorageDirectory = null);   // 07 — db + ses + anahtar konumu; null → uygulama-veri varsayılanı
```

> Not: Additive (yeni alan defaultlu, sona). `AppConfig.Default` + `with` kırılmaz. Değişiklikten önce `grep -rn "new StorageConfig(" src tests` ile pozisyonel çağrı olmadığını teyit et.

- [ ] **Step 3: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Storage.Tests/FileKeyProviderTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class FileKeyProviderTests
{
    private static AppConfig CfgWithDir(string dir) =>
        AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void GetKey_GeneratesThirtyTwoByteKey_WhenAbsent()
    {
        var dir = NewTempDir();
        var key = new FileKeyProvider(CfgWithDir(dir)).GetKey();
        Assert.Equal(32, key.Length);
        Assert.True(File.Exists(Path.Combine(dir, "storage.key")));
        Assert.False(key.All(b => b == 0), "anahtar sıfır olmamalı");
    }

    [Fact]
    public void GetKey_IsStableAcrossCalls()
    {
        var dir = NewTempDir();
        var cfg = CfgWithDir(dir);
        var k1 = new FileKeyProvider(cfg).GetKey();
        var k2 = new FileKeyProvider(cfg).GetKey(); // aynı dosyadan okunur
        Assert.Equal(k1, k2);
    }

    [Fact]
    public void DefaultStorageDirectory_UnderApplicationData()
    {
        var def = FileKeyProvider.DefaultStorageDirectory();
        Assert.Contains("SentimentAnalysis", def);
    }
}
```

- [ ] **Step 4: Testin başarısız olduğunu doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Storage.Tests`. Expected: FAIL (tipler yok).

- [ ] **Step 5: `IKeyProvider`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Storage/IKeyProvider.cs`:

```csharp
namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Simetrik şifreleme anahtarı (AES-256, 32 byte) sağlar. Bu tur dosya-anahtar
/// (<see cref="FileKeyProvider"/>); OS keychain/DPAPI implementasyonu → 07b (aynı arayüz).</summary>
public interface IKeyProvider
{
    byte[] GetKey();
}
```

- [ ] **Step 6: `FileKeyProvider`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Storage/FileKeyProvider.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>AES-256 anahtarını <c>{StorageDirectory}/storage.key</c> dosyasında tutar; yoksa
/// kriptografik rastgele üretip yazar (Unix'te 0600). DÜZ METİN diskte — bu tur dev/geçici; OS
/// keychain/DPAPI ile değiştirilecek (07b). Thread-safe (dosya-var ise oku, atomik değil ama tek-yazar varsayımı).</summary>
public sealed class FileKeyProvider : IKeyProvider
{
    private const int KeySize = 32;
    private readonly string _keyPath;

    public FileKeyProvider(AppConfig config)
    {
        var dir = config.Storage.StorageDirectory ?? DefaultStorageDirectory();
        Directory.CreateDirectory(dir);
        _keyPath = Path.Combine(dir, "storage.key");
    }

    public byte[] GetKey()
    {
        if (File.Exists(_keyPath))
        {
            var existing = File.ReadAllBytes(_keyPath);
            if (existing.Length == KeySize) return existing;
        }
        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(_keyPath, key);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(_keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600
        return key;
    }

    public static string DefaultStorageDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SentimentAnalysis", "storage");
}
```

- [ ] **Step 7: Testin geçtiğini + tüm çözümün derlendiğini doğrula** — Run: `dotnet build && dotnet test`. Expected: 0 uyarı; yeni 3 test PASS; mevcut testler (148 + 6 skip) kırılmaz.

---

### Task 2: `AudioEncryptor` (saf: AES-GCM)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Storage/AudioEncryptor.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/AudioEncryptorTests.cs`

**Interfaces:**
- Produces: `static class AudioEncryptor { byte[] Encrypt(byte[] plaintext, byte[] key); byte[] Decrypt(byte[] blob, byte[] key); }`. Çıktı düzeni `[nonce(12)][tag(16)][ciphertext]`. Yanlış anahtar/kurcalama → `AuthenticationTagMismatchException`.

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Storage.Tests/AudioEncryptorTests.cs`:

```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class AudioEncryptorTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var key = Key();
        var data = Encoding.UTF8.GetBytes("gizli ses verisi 12345");
        var blob = AudioEncryptor.Encrypt(data, key);

        Assert.NotEqual(data, blob);                    // şifreli ≠ düz
        Assert.True(blob.Length >= data.Length + 28);   // nonce(12)+tag(16)+cipher
        Assert.Equal(data, AudioEncryptor.Decrypt(blob, key));
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var blob = AudioEncryptor.Encrypt(Encoding.UTF8.GetBytes("veri"), Key());
        Assert.Throws<AuthenticationTagMismatchException>(() => AudioEncryptor.Decrypt(blob, Key()));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var key = Key();
        var blob = AudioEncryptor.Encrypt(Encoding.UTF8.GetBytes("veri veri veri"), key);
        blob[^1] ^= 0xFF; // son ciphertext byte'ını boz
        Assert.Throws<AuthenticationTagMismatchException>(() => AudioEncryptor.Decrypt(blob, key));
    }

    [Fact]
    public void Encrypt_Empty_RoundTrips()
    {
        var key = Key();
        var blob = AudioEncryptor.Encrypt(Array.Empty<byte>(), key);
        Assert.Empty(AudioEncryptor.Decrypt(blob, key));
    }

    [Fact]
    public void Encrypt_DifferentNoncesEachCall()
    {
        var key = Key();
        var d = Encoding.UTF8.GetBytes("aynı veri");
        Assert.NotEqual(AudioEncryptor.Encrypt(d, key), AudioEncryptor.Encrypt(d, key)); // rastgele nonce
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — FAIL (`AudioEncryptor` yok).

- [ ] **Step 3: `AudioEncryptor`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Storage/AudioEncryptor.cs`:

```csharp
using System.Security.Cryptography;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Ham ses bytes'ını AES-256-GCM ile şifreler/çözer. Blob düzeni: <c>[nonce(12)][tag(16)][ciphertext]</c>.
/// Anahtarsız/kurcalanmış veri <see cref="AuthenticationTagMismatchException"/> atar. Saf (anahtar parametreli).</summary>
public static class AudioEncryptor
{
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize

    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plaintext.Length];
        using (var aes = new AesGcm(key, TagSize)) // tag-size ZORUNLU (SYSLIB0053)
            aes.Encrypt(nonce, plaintext, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
        return blob;
    }

    public static byte[] Decrypt(byte[] blob, byte[] key)
    {
        if (blob.Length < NonceSize + TagSize)
            throw new ArgumentException("Şifreli blob çok kısa (nonce+tag yok).", nameof(blob));

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[blob.Length - NonceSize - TagSize];
        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(blob, NonceSize + TagSize, cipher, 0, cipher.Length);

        var plaintext = new byte[cipher.Length];
        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plaintext); // yanlış anahtar/kurcalama → AuthenticationTagMismatchException
        return plaintext;
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Storage.Tests`. Expected: PASS.

---

### Task 3: `EncryptedAudioStore` (şifreli dosya IO)

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Storage/EncryptedAudioStore.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/EncryptedAudioStoreTests.cs`

**Interfaces:**
- Consumes: `IKeyProvider`, `AudioEncryptor`, `AppConfig`
- Produces: `sealed class EncryptedAudioStore { EncryptedAudioStore(AppConfig, IKeyProvider); string Write(Guid sessionId, byte[] rawAudio); byte[] Read(string path); void Delete(string path); }`. `Write` → `{StorageDirectory}/audio/{sessionId}.enc` (şifreli); yolu döner. `Read` çözer (dosyadaki içerik düz metin DEĞİL). `Delete` dosyayı siler (yoksa sessiz).

- [ ] **Step 1: Başarısız testi yaz**

`tests/SentimentAnalysis.Infrastructure.Storage.Tests/EncryptedAudioStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class EncryptedAudioStoreTests
{
    private static (EncryptedAudioStore store, string dir) NewStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };
        return (new EncryptedAudioStore(cfg, new FileKeyProvider(cfg)), dir);
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var (store, _) = NewStore();
        var data = Encoding.UTF8.GetBytes("ham ses örneği");
        var path = store.Write(Guid.NewGuid(), data);
        Assert.True(File.Exists(path));
        Assert.Equal(data, store.Read(path));
    }

    [Fact]
    public void WrittenFile_IsNotPlaintext()
    {
        var (store, _) = NewStore();
        var data = Encoding.UTF8.GetBytes("GIZLI_ANAHTAR_KELIME");
        var path = store.Write(Guid.NewGuid(), data);
        var onDisk = File.ReadAllBytes(path);
        // Düz metin diskte görünmemeli.
        Assert.DoesNotContain(Encoding.UTF8.GetString(onDisk), "GIZLI_ANAHTAR_KELIME");
        Assert.NotEqual(data, onDisk);
    }

    [Fact]
    public void Read_WithDifferentKey_Fails()
    {
        // Aynı dosya, farklı anahtar dizini → çözülemez.
        var (store, _) = NewStore();
        var path = store.Write(Guid.NewGuid(), Encoding.UTF8.GetBytes("veri"));

        var otherDir = Path.Combine(Path.GetTempPath(), "sa-audio2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(otherDir);
        var otherCfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = otherDir } };
        var otherStore = new EncryptedAudioStore(otherCfg, new FileKeyProvider(otherCfg));

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => otherStore.Read(path));
    }

    [Fact]
    public void Delete_RemovesFile_AndIsIdempotent()
    {
        var (store, _) = NewStore();
        var path = store.Write(Guid.NewGuid(), new byte[] { 1, 2, 3 });
        store.Delete(path);
        Assert.False(File.Exists(path));
        store.Delete(path); // ikinci sil → sessiz
    }
}
```

- [ ] **Step 2: Başarısız olduğunu doğrula** — FAIL (`EncryptedAudioStore` yok).

- [ ] **Step 3: `EncryptedAudioStore`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Storage/EncryptedAudioStore.cs`:

```csharp
using SentimentAnalysis.Application.Configuration;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Ham sesi şifreleyip <c>{StorageDirectory}/audio/{sessionId}.enc</c> altında saklar; okur/siler.
/// Anahtar <see cref="IKeyProvider"/>'dan. Diskte yalnızca AES-GCM şifreli veri bulunur.</summary>
public sealed class EncryptedAudioStore
{
    private readonly IKeyProvider _keys;
    private readonly string _audioDir;

    public EncryptedAudioStore(AppConfig config, IKeyProvider keys)
    {
        _keys = keys;
        var dir = config.Storage.StorageDirectory ?? FileKeyProvider.DefaultStorageDirectory();
        _audioDir = Path.Combine(dir, "audio");
        Directory.CreateDirectory(_audioDir);
    }

    public string Write(Guid sessionId, byte[] rawAudio)
    {
        var path = Path.Combine(_audioDir, sessionId.ToString("N") + ".enc");
        var blob = AudioEncryptor.Encrypt(rawAudio, _keys.GetKey());
        File.WriteAllBytes(path, blob);
        return path;
    }

    public byte[] Read(string path) => AudioEncryptor.Decrypt(File.ReadAllBytes(path), _keys.GetKey());

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
```

- [ ] **Step 4: Testin geçtiğini doğrula** — Run: `dotnet test tests/SentimentAnalysis.Infrastructure.Storage.Tests`. Expected: PASS.

---

### Task 4: `StorageSchema` + `SqliteSessionStore : ISessionStore` + `AddSqliteStorage()` DI

**Files:**
- Create: `src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs`
- Create: `src/SentimentAnalysis.Infrastructure.Storage/StorageServiceCollectionExtensions.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/SqliteSessionStoreTests.cs`
- Create: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/StorageDiTests.cs`
- Modify: `tests/SentimentAnalysis.Infrastructure.Storage.Tests/*.csproj` (DI test paketi)

**Interfaces:**
- Consumes: `ISessionStore`, `ChunkResult`, `FusedResult`, `MeetingSession` (Application/Core), `EncryptedAudioStore`, `AppConfig`; Microsoft.Data.Sqlite
- Produces: `static class StorageSchema { const string CreateSql; }`; `sealed class SqliteSessionStore : ISessionStore` (ctor `(AppConfig, EncryptedAudioStore, Func<DateTimeOffset>? clock = null)`, ek `Task<string> SaveAudioAsync(Guid sessionId, byte[] rawAudio, CancellationToken ct)`); `static class StorageServiceCollectionExtensions { IServiceCollection AddSqliteStorage(this IServiceCollection); }`.

- [ ] **Step 1: DI test paketini ekle**

```bash
cd /Users/betulfiliz/sentiment-analysis
dotnet add tests/SentimentAnalysis.Infrastructure.Storage.Tests package Microsoft.Extensions.DependencyInjection --version 10.0.10
```

- [ ] **Step 2: Başarısız testleri yaz**

`tests/SentimentAnalysis.Infrastructure.Storage.Tests/SqliteSessionStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class SqliteSessionStoreTests
{
    private static (SqliteSessionStore store, EncryptedAudioStore audio, string dir) NewStore(Func<DateTimeOffset>? clock = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };
        var audio = new EncryptedAudioStore(cfg, new FileKeyProvider(cfg));
        return (new SqliteSessionStore(cfg, audio, clock), audio, dir);
    }

    private static ChunkResult Result(double start, string transcript) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(start + 1),
            new FusedResult(new ValenceArousal(0.3, 0.6), Emotion.Mutlu, Emotion.Sakin,
                Confidence.Yuksek, true, false, IncongruencePattern.Yok, 0.9, 0.3, 0.2),
            transcript);

    [Fact]
    public async Task SaveChunk_GetSession_RoundTripsMetricsAndTranscript()
    {
        var (store, _, _) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0, "merhaba"), CancellationToken.None);
        await store.SaveChunkAsync(id, Result(1, "dünya"), CancellationToken.None);

        var session = await store.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(id, session!.Id);
        Assert.Equal(2, session.Chunks.Count);
        Assert.Equal("merhaba", session.Chunks[0].Transcript);
        Assert.Equal(TimeSpan.FromSeconds(1), session.Chunks[0].TimeEnd);
        Assert.Equal(Emotion.Mutlu, session.Chunks[0].Fused.PrimaryEmotion);
        Assert.Equal(Confidence.Yuksek, session.Chunks[0].Fused.Confidence);
        Assert.Equal(0.3, session.Chunks[0].Fused.Combined.Valence, precision: 5);
        Assert.Equal("dünya", session.Chunks[1].Transcript);
    }

    [Fact]
    public async Task GetSession_Unknown_ReturnsNull()
    {
        var (store, _, _) = NewStore();
        Assert.Null(await store.GetSessionAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListSessions_ReturnsAll()
    {
        var (store, _, _) = NewStore();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await store.SaveChunkAsync(a, Result(0, "a"), CancellationToken.None);
        await store.SaveChunkAsync(b, Result(0, "b"), CancellationToken.None);
        var list = await store.ListSessionsAsync(CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, s => s.Id == a);
        Assert.Contains(list, s => s.Id == b);
    }

    [Fact]
    public async Task Persistence_SurvivesNewStoreInstance()
    {
        var (store, audio, dir) = NewStore();
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0, "kalıcı"), CancellationToken.None);

        var cfg = AppConfig.Default with { Storage = AppConfig.Default.Storage with { StorageDirectory = dir } };
        var store2 = new SqliteSessionStore(cfg, audio);   // aynı db dosyası
        var session = await store2.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal("kalıcı", session!.Chunks[0].Transcript);
    }

    [Fact]
    public async Task PurgeExpiredAudio_DeletesOldAudio_KeepsMetrics()
    {
        // t0'da kayıt; saat 8 gün ileri → retention (7g) dolar.
        var now = DateTimeOffset.UnixEpoch;
        var (store, _, _) = NewStore(() => now);
        var id = Guid.NewGuid();
        await store.SaveChunkAsync(id, Result(0, "metrik"), CancellationToken.None);
        var audioPath = await store.SaveAudioAsync(id, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
        Assert.True(File.Exists(audioPath));

        now = DateTimeOffset.UnixEpoch.AddDays(8); // saat ilerledi
        await store.PurgeExpiredAudioAsync(CancellationToken.None);

        Assert.False(File.Exists(audioPath), "süresi dolan ses silinmeli");
        var session = await store.GetSessionAsync(id, CancellationToken.None);
        Assert.NotNull(session);                       // metrik korunur
        Assert.Single(session!.Chunks);
        Assert.Equal("metrik", session.Chunks[0].Transcript);
    }

    [Fact]
    public async Task PurgeExpiredAudio_KeepsFreshAudio()
    {
        var now = DateTimeOffset.UnixEpoch;
        var (store, _, _) = NewStore(() => now);
        var id = Guid.NewGuid();
        var audioPath = await store.SaveAudioAsync(id, new byte[] { 9 }, CancellationToken.None);

        now = DateTimeOffset.UnixEpoch.AddDays(3); // retention dolmadı
        await store.PurgeExpiredAudioAsync(CancellationToken.None);
        Assert.True(File.Exists(audioPath), "taze ses korunmalı");
    }
}
```

`tests/SentimentAnalysis.Infrastructure.Storage.Tests/StorageDiTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Infrastructure.Storage;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Storage.Tests;

public class StorageDiTests
{
    [Fact]
    public void AddSqliteStorage_ResolvesSessionStore()
    {
        var provider = new ServiceCollection()
            .AddApplicationCore()
            .AddSqliteStorage()
            .BuildServiceProvider();

        Assert.IsType<SqliteSessionStore>(provider.GetRequiredService<ISessionStore>());
        Assert.NotNull(provider.GetRequiredService<IKeyProvider>());
    }
}
```

- [ ] **Step 3: Başarısız olduğunu doğrula** — FAIL (`SqliteSessionStore`/`AddSqliteStorage` yok). (`Emotion.Mutlu/Sakin`, `Confidence.Yuksek` adlarını Core'dan teyit et.)

- [ ] **Step 4: `StorageSchema`'yı yaz**

`src/SentimentAnalysis.Infrastructure.Storage/StorageSchema.cs`:

```csharp
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
```

- [ ] **Step 5: `SqliteSessionStore`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Storage/SqliteSessionStore.cs`:

```csharp
using Microsoft.Data.Sqlite;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>Gerçek <see cref="ISessionStore"/>: metrik/transkript SQLite'ta kalıcı, ham ses şifreli dosyada
/// (<see cref="EncryptedAudioStore"/>). Retention dolunca ses silinir, metrik korunur. Bağlantı-başına-işlem
/// (SQLite dosya kilidi ile serileşir). Metadata sınırı: StartedAt = ilk kayıt (enjekte saat), Language = Other
/// (ChunkResult taşımaz; toplantı metadata'sı 06 pipeline'da).</summary>
public sealed class SqliteSessionStore : ISessionStore
{
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
                agreement, incongruent, pattern, congruence, text_valence, audio_valence)
            VALUES ($sid,
                (SELECT COALESCE(MAX(seq), -1) + 1 FROM chunks WHERE session_id = $sid),
                $ts, $te, $tr, $cv, $ca, $pe, $se, $cf, $ag, $inc, $pat, $cong, $tv, $av)
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
        cmd.Parameters.AddWithValue("$tv", f.TextValence);
        cmd.Parameters.AddWithValue("$av", f.AudioValence);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
                congruence, text_valence, audio_valence
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
                rd.GetDouble(11), rd.GetDouble(12), rd.GetDouble(13));
            chunks.Add(new ChunkResult(
                TimeSpan.FromMilliseconds(rd.GetInt64(0)), TimeSpan.FromMilliseconds(rd.GetInt64(1)),
                fused, rd.GetString(2)));
        }
        return chunks;
    }
}
```

- [ ] **Step 6: `AddSqliteStorage()`'ı yaz**

`src/SentimentAnalysis.Infrastructure.Storage/StorageServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Storage;

/// <summary>SQLite depolama DI kaydı. `ISessionStore` düz AddSingleton ile → 06'nın TryAdd InMemory'sini
/// composition root sırasıyla ezer (bu uzantı çağrıldığında gerçek store kazanır). `AddApplicationCore()` ile.</summary>
public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddSqliteStorage(this IServiceCollection services)
    {
        services.AddSingleton<IKeyProvider, FileKeyProvider>();
        services.AddSingleton<EncryptedAudioStore>();
        services.AddSingleton<ISessionStore, SqliteSessionStore>();
        return services;
    }
}
```

- [ ] **Step 7: Tam çözümün derlendiğini ve testlerin geçtiğini doğrula** — Run: `dotnet build && dotnet test`. Expected: Build **0 uyarı** (AesGcm tag-size'lı → SYSLIB0053 yok); **tüm** testler PASS (01–06 + 07); env-gated skip 6'da kalır. `SqliteSessionStore` DI'da `EncryptedAudioStore` + `AppConfig` çözülür; `Func<DateTimeOffset>?` opsiyonel ctor param DI'da default (UtcNow) kullanılır.

---

## Self-Review (plan yazarı tarafından)

**1. Spec coverage (07, bu tur):**
- SQLite metrik/transkript kalıcılık → Task 4 `SqliteSessionStore` + `StorageSchema`. ✔
- Retention/purge (ses siler, metrik korur) → Task 4 `PurgeExpiredAudioAsync` + testler. ✔
- AES-GCM ses şifreleme (anahtarsız okunamaz) → Task 2 `AudioEncryptor` + Task 3 `EncryptedAudioStore`. ✔
- Anahtar `IKeyProvider` arkasında (dosya, 07b keychain) → Task 1. ✔
- `StorageConfig` additive (`StorageDirectory`) → Task 1. ✔
- `AddSqliteStorage()` DI (InMemory'yi ezer) → Task 4. ✔

**2. Kapsam dışı (bilinçli, 07b):** Opus sıkıştırma (~11MB/saat); OS keychain/DPAPI anahtar; ses oynatma. Bu tur şifreli ham/PCM.

**3. Placeholder scan:** TODO/TBD yok. Dosya-anahtarın "dev/geçici, 07b keychain" olduğu işaretli. Metadata sınırı (Language=Other) dokümante + InMemory ile tutarlı. Tüm kod blokları tam.

**4. Type consistency:**
- `IKeyProvider.GetKey()`/`FileKeyProvider(AppConfig)` → Task 3 `EncryptedAudioStore` + Task 4 tüketir. ✔
- `AudioEncryptor.Encrypt/Decrypt(byte[], byte[])` → Task 3 tüketir. ✔
- `EncryptedAudioStore(AppConfig, IKeyProvider)` + `Write/Read/Delete` → Task 4 `SqliteSessionStore` tüketir. ✔
- `SqliteSessionStore(AppConfig, EncryptedAudioStore, Func<DateTimeOffset>?)` + `SaveAudioAsync` → test + DI tutarlı. ✔
- `StorageConfig.StorageDirectory` (additive) → tüm bileşenler `config.Storage.StorageDirectory ?? default`. ✔
- Core alan adları: **DİKKAT (implementer)** `Emotion.Mutlu/Sakin`, `Confidence.Yuksek`, `IncongruencePattern.Yok`, `ValenceArousal(v,a)`, `FusedResult(...)` sırası `grep -rn "enum Emotion\|enum Confidence\|record FusedResult" src/SentimentAnalysis.Core` ile teyit; FusedResult kurucu sırası: `(Combined, PrimaryEmotion, SecondaryEmotion, Confidence, Agreement, Incongruent, Pattern, Congruence, TextValence, AudioValence)` — DB yaz/oku bu sırayı korumalı.
- `StorageConfig` genişletmesi: `grep -rn "new StorageConfig(" src tests` ile pozisyonel çağrı yok teyidi (Task 1 Step 2).

**5. Mimari:** Infrastructure.Storage → Application → Core. SqliteConnection motor içinde; `AudioEncryptor` saf (anahtar parametreli, unit-testli); `EncryptedAudioStore`/`FileKeyProvider` dosya IO sınırında.

**6. Kaynak/eşzamanlılık:** bağlantı-başına-işlem (`using`/`await using`), SQLite dosya kilidi serileştirir; şema ctor'da idempotent (`IF NOT EXISTS`); `seq` alt-sorgu ile atomik artar (aynı bağlantıda tek INSERT). Retention `cutoff` enjekte saatle deterministik.

**Riskler / dikkat:**
- **`await OpenAsync`/`ExecuteReaderAsync`** Microsoft.Data.Sqlite'ta senkron-üstü sarmalayıcı; doğru çalışır, ilk build restore native SQLitePCLRaw'ı (osx-arm64) getirir → ilk `dotnet test` doğrular.
- **`SaveChunkAsync` seq alt-sorgusu:** aynı bağlantıda tek komut; eşzamanlı iki SaveChunk aynı sessionId'ye çakışırsa (nadir, tek-pipeline consumer) SQLite kilidi serileştirir; pipeline tek consumer olduğundan sıralı zaten.
- **DI opsiyonel ctor param:** `Func<DateTimeOffset>? clock = null` DI tarafından sağlanmaz → default (UtcNow) kullanılır; testler clock enjekte eder. `EncryptedAudioStore` + `AppConfig` DI'da mevcut (AppConfig `AddApplicationCore`, EncryptedAudioStore `AddSqliteStorage`).
- **Git kuralı:** commit yok; her task sonu `dotnet build && dotnet test`.

---

## Post-Review Düzeltmeleri (uygulama sırasında)

- Fix gerektiren bulgu YOK. Task 4 (SqliteSessionStore) tam review'dan **spec ✅ + quality approved** ile geçti; FusedResult alan-sırası write/read round-trip, purge/retention semantiği, bağlantı yaşam döngüsü, DI-override mekanizması doğrulandı.
- `AesGcm` tag-size'lı kurucu (`new AesGcm(key, 16)`) kullanıldı → SYSLIB0053 uyarısı yok (0 uyarı).
- Microsoft.Data.Sqlite osx-arm64 native temiz restore oldu; SQLite testleri iki tam-suite koşusunda kararlı (file-lock sorunu yok).

**Nihai durum:** 167 test geçti + 6 env-gated Skip (değişmedi — 07 modelsiz), 0 başarısız, 0 uyarı.

**Final whole-branch review: ATLANDI** (kullanıcı hız tercihi — Task 4 tam review'dan geçti, diğerleri saf/dosya-IO yeşil).

**Biriken deferred Minor'lar (Task 4):**
- M-a: DI-override testi `AddPipeline()`+`AddSqliteStorage()` uçtan-uca senaryosunu kapsamıyor (mekanizma DI semantiğiyle doğru; izole test var).
- M-b: WAL journal modu ayarlanmadı (varsayılan rollback yeterli; ileride yoğun eşzamanlı kullanımda WAL düşünülebilir).

**Kapsam dışı (bilinçli, 07b):** Opus sıkıştırma (~11MB/saat); OS keychain/DPAPI anahtar saklama (`IKeyProvider` arayüzü hazır); ses oynatma.
