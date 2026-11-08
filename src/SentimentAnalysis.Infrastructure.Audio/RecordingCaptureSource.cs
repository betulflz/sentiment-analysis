using System.Runtime.CompilerServices;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Bir <see cref="IAudioCaptureSource"/>'u sarıp akan chunk'ların PCM'ini biriktirir —
/// arka plan konuşmacı ayrımının (alt-proje 10b) girdisi. Chunk akışını DEĞİŞTİRMEZ.
/// <para><b>İptal sözleşmesi (kritik):</b> <see cref="IAudioCaptureSource"/> üzerinde belgelendiği
/// gibi implementasyonlar iptal anında kısa sürede dönmeli, asla sonsuza dek bloklamamalıdır —
/// <c>EmotionPipeline</c> üreticiyi <c>finally</c> içinde zaman aşımsız bekler. Bu dekoratör iç
/// kaynağı SAYDAM biçimde geçirir: ek <c>try/catch</c>, bekleme veya yutma YOKTUR. Buraya bir
/// hata yakalama eklemek, "Durdur"da uygulamayı sonsuza dek asabilir ve hiçbir test bunu
/// yakalamaz.</para>
/// <para><b>Neden üst sınır:</b> 16 kHz mono PCM16 ≈ 115 MB/saat. Sınırsız biriktirme uzun bir
/// toplantıda M1 8GB'da uygulamayı şişirir. Sınır aşılırsa chunk akışı sürer (analiz kesilmez)
/// ama biriktirme durur ve <see cref="LimitReached"/> true olur — sessizce kırpma yerine
/// görünür bir durum (master §12.2, dürüstlük).</para>
/// <para><b>Zaman hizası:</b> akıştaki chunk'lar örtüşür (canlı/video 10 sn + 2 sn, dosya 30 sn + 5 sn).
/// Kayıt, chunk'ların <see cref="AudioChunk.Start"/>/<see cref="AudioChunk.End"/> zamanlarına göre yalnızca
/// henüz kaydedilmemiş kısmı ekler — kayıttaki konum = analiz zaman çizelgesindeki zaman. Aksi halde her
/// chunk'ta örtüşme kadar kayan konuşmacı bölgeleri transkript satırlarını yanlış kişiye bağlıyordu.</para></summary>
public sealed class RecordingCaptureSource(IAudioCaptureSource inner, int maxSamples) : IAudioCaptureSource
{
    private readonly List<float> _pcm = new();
    private TimeSpan _recordedUntil = TimeSpan.Zero; // kayıt bu mutlak zamana kadar dolu

    /// <summary>Üst sınır aşıldığı için sesin bir kısmı biriktirilmedi mi? Kullanıcıya
    /// bildirilmelidir — konuşmacı ayrımı yalnızca biriken kısmı kapsar.</summary>
    public bool LimitReached { get; private set; }

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in inner.CaptureAsync(options, ct).ConfigureAwait(false))
        {
            Accumulate(chunk);
            yield return chunk;
        }
    }

    private void Accumulate(AudioChunk chunk)
    {
        if (LimitReached) return;

        // Örtüşen baş kısım zaten kayıtta; arada boşluk varsa sessizlikle doldurulur (hiza korunur).
        int skip = (int)Math.Round((_recordedUntil - chunk.Start).TotalSeconds * chunk.SampleRate);
        if (chunk.End > _recordedUntil) _recordedUntil = chunk.End;
        if (skip >= chunk.Samples.Length) return;
        var fresh = skip >= 0 ? chunk.Samples.AsSpan(skip) : chunk.Samples.AsSpan();
        int gap = Math.Max(0, -skip);

        int room = maxSamples - _pcm.Count;
        if (room <= 0) { LimitReached = true; return; }

        int take = Math.Min(room, gap + fresh.Length);
        for (int i = 0; i < Math.Min(gap, take); i++) _pcm.Add(0f);
        for (int i = 0; i < take - gap; i++) _pcm.Add(fresh[i]);
        // Sığan kadarını aldık; sığmayan kısım varsa dur: yarım bir chunk sessizce atılmasın.
        if (take < gap + fresh.Length) LimitReached = true;
    }

    /// <summary>Biriktirilen sesi 16-bit little-endian mono PCM olarak döndürür.
    /// Taşan örnekler kırpılır (sarmalanma sesi bozardı).</summary>
    public byte[] GetPcm16()
    {
        var bytes = new byte[_pcm.Count * 2];
        for (int i = 0; i < _pcm.Count; i++)
        {
            short s = (short)Math.Clamp(_pcm[i] * 32768f, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }
}
