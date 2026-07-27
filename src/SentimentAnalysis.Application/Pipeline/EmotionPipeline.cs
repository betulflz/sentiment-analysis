using System.Threading.Channels;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Yakalama akışını bounded kanal ile orkestre eder: producer chunk'ları yazar (DropOldest —
/// canlı akıcılık için bloklanmaz, taşarsa en eski düşer), tek consumer <see cref="IChunkProcessor"/>'dan
/// geçirip timeline'a + <see cref="ISessionStore"/>'a SIRAYLA yazar; sonda <see cref="IMeetingAggregator"/>
/// ile <see cref="MeetingSummary"/> üretir. Bir chunk'taki bileşen hatası izole edilir (o chunk atlanır).
/// Tek koşu/örnek varsayılır (Timeline/DroppedCount koşu-başı durum).</summary>
public sealed class EmotionPipeline
{
    private readonly IChunkProcessor _processor;
    private readonly ISessionStore _store;
    private readonly IMeetingAggregator _aggregator;
    private readonly int _capacity;

    private readonly List<ChunkResult> _timeline = new();
    private int _droppedCount;
    private int _drainSkippedCount;

    /// <summary>"Durdur" sonrası kanalda kalan chunk'ları drene ederken en fazla kaç tanesi işlenir —
    /// SINIRSIZ değil: kanal 32 kapasiteli ve mikrofon modunda chunk başına 10 sn'lik ses taşıyabilir;
    /// Whisper+ONNX chunk başına 8 sn'den yavaşsa (M1 Air 8GB'da olası) kanal sürekli dolu kalır ve
    /// sınırsız drenaj "Durdur"u dakikalarca donuk bırakır (bkz. 08e düzeltme turu Fix 1). 8 seçildi:
    /// en YENİ ~80 sn'lik konuşmayı (kullanıcının demin söylediği, önemsediği kısmı) kapsar ve en kötü
    /// durumda bile aşağıdaki <see cref="DrainTimeBudget"/> içinde kalma ihtimali yüksektir.
    /// Kapasitenin (32) tamamını değil bunu seçmemizin nedeni: geri kalanı zaten DroppedCount'a değil
    /// (kanal taşması değil bu), <see cref="DrainSkippedCount"/>'a sayılır — sessizce kaybolmaz.</summary>
    private const int MaxDrainedChunksAfterStop = 8;

    /// <summary>Drenajın toplam zaman bütçesi. Bu süre dolarsa drenaj bir HATA sayılmaz — elde ne
    /// varsa onunla bir <see cref="MeetingSummary"/> üretilir (dürüstlük ilkesi: işlenemeyen kalan
    /// chunk'lar <see cref="DrainSkippedCount"/> ile ayrıca raporlanır, sessizce yutulmaz). 30 sn:
    /// kullanıcının "Durdur" sonrası makul bir bekleme toleransı + "asla sonsuza dek donmama" garantisi.</summary>
    private static readonly TimeSpan DrainTimeBudget = TimeSpan.FromSeconds(30);

    public EmotionPipeline(IChunkProcessor processor, ISessionStore store, IMeetingAggregator aggregator, AppConfig config)
    {
        _processor = processor;
        _store = store;
        _aggregator = aggregator;
        _capacity = Math.Max(1, config.Pipeline.ChannelCapacity);
    }

    public IReadOnlyList<ChunkResult> Timeline => _timeline;
    public int DroppedCount => Volatile.Read(ref _droppedCount);

    /// <summary>"Durdur" sonrası drenaj sınırı (<see cref="MaxDrainedChunksAfterStop"/>) veya zaman
    /// bütçesi (<see cref="DrainTimeBudget"/>) yüzünden işlenemeden atlanan chunk sayısı.
    /// <see cref="DroppedCount"/>'tan AYRI tutulur (o yalnızca kanal taşmasını sayar) ama ikisi de
    /// kullanıcıya "yetişilemeyen ses" olarak BİRLİKTE gösterilmelidir (bkz. LiveViewModel).</summary>
    public int DrainSkippedCount => Volatile.Read(ref _drainSkippedCount);

    public async Task<MeetingSummary> RunAsync(
        IAudioCaptureSource source, CaptureOptions options, Guid sessionId,
        DateTimeOffset startedAt, DetectedLanguage language,
        IProgress<ChunkResult>? progress, CancellationToken ct)
    {
        _timeline.Clear();
        _droppedCount = 0;
        _drainSkippedCount = 0;

        var channel = Channel.CreateBounded<AudioChunk>(
            new BoundedChannelOptions(_capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            itemDropped: _ => Interlocked.Increment(ref _droppedCount));

        // NOT: Task.Run'un KENDİSİ artık ct ile iptal edilmez (ikinci argüman yok) — üretici,
        // "Durdur" sonrası kanalı temiz biçimde tamamlayabilmek için ct zaten iptalliyken bile
        // HER ZAMAN çalışmaya başlamalı. Gerçek iptal-farkındalığı CaptureAsync(options, ct)'in
        // kendi işi (bkz. MicrophoneCaptureSource: iptali artık kendisi yutup tamponun artığını
        // vererek normal biter — bu üretici katmanındaki catch(OperationCanceledException) yalnızca
        // ct zaten iptalliyken kaynağın hiç başlayamadığı ekstrem bir uç durum içindir).
        var producer = Task.Run(async () =>
        {
            Exception? producerError = null;
            try
            {
                await foreach (var chunk in source.CaptureAsync(options, ct).ConfigureAwait(false))
                    // DropOldest modunda WriteAsync asla bloklanmaz/beklemez (TryWrite hızlı
                    // yolu her zaman başarılı olur) — bu yüzden burada iptal edilmiş ct ile
                    // çağırmak güvenli DEĞİL: gereksiz yere bir istisna riski almamak için
                    // CancellationToken.None kullanıyoruz (iptalden sonra da üretilen chunk'lar
                    // kanala güvenle yazılabilsin).
                    await channel.Writer.WriteAsync(chunk, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // "Durdur": canlı mikrofon oturumunda bu artık normal bir bitiştir, hata değil
                // (kullanıcı talebi: Durdur bir RAPOR üretmeli). Kaynak bunu genelde kendisi
                // yutar (bkz. MicrophoneCaptureSource); buraya yalnızca ct daha CaptureAsync
                // hiç başlamadan iptal olduysa düşülür. Kanalı hatasız tamamlıyoruz.
            }
            catch (Exception ex)
            {
                // Kaynağın gerçek hatası (ör. MicrophonePermissionException): kanalı BU
                // hatayla tamamla ki tüketicinin ReadAllAsync'i onu fırlatsın. Aksi halde
                // kanal sessizce normal biter ve çalışma boş bir özete düşer — spec bunu
                // yasaklıyor (bkz. sub-project 08e planı).
                producerError = ex;
            }
            finally
            {
                channel.Writer.Complete(producerError);
            }
        });

        bool cancelledMidRun = false;
        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                await ProcessOneAsync(chunk, sessionId, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // "Durdur": kanal okuması ct iptal olduğu AN fırlar — kanalda o sırada zaten
            // kuyruğa alınmış ama henüz okunmamış chunk'lar olsa bile (bu, gerçek dünyada
            // gözlemlenen kök neden: kullanıcı konuştu, chunk üretildi, ama "Durdur"a basılır
            // basılmaz okuma iptal olup kanaldaki veri hiç işlenmeden düşüyordu). Bunu normal
            // bitiş sayıp aşağıda kalanları drene ediyoruz — hata olarak yukarı atmıyoruz.
            cancelledMidRun = true;
        }
        finally
        {
            // Üretici, kaynağını (mikrofon kuyruğunu) HER durumda (iptal dahil) durdurup
            // kanalı tamamlar — bunu burada gözlüyoruz ki drene etmeden önce üretici KESİN
            // bitmiş olsun (aksi halde aşağıdaki TryRead döngüsü verilerin hâlâ yazılmakta
            // olduğu bir kanalı erken/eksik drene edebilir).
            try { await producer.ConfigureAwait(false); } catch { /* iptal/üretici hatası yutulur; asıl akış yukarı gider */ }
        }

        if (cancelledMidRun)
        {
            // Üretici artık kesin bitti (yukarıda beklendi) — kanalda kalan (henüz işlenmemiş)
            // chunk'ları kaybetmeden TryRead ile topla (TryRead asla bloklamaz — hang riski yok,
            // kanal boşalınca döngü kendiliğinden biter). Kanal kapasitesi zaten sınırlı (varsayılan
            // 32) olduğundan bu liste sınırsız büyümez.
            var pendingChunks = new List<AudioChunk>();
            while (channel.Reader.TryRead(out var pending))
                pendingChunks.Add(pending);

            // Yalnızca en YENİ MaxDrainedChunksAfterStop chunk'ı işleriz (kullanıcının demin
            // konuştuğu, önemsediği ses) — kapasitenin tamamını sınırsız drene etmek "Durdur"u
            // dakikalarca donuk bırakabilir (bkz. Fix 1). Kapak öncesi (en eski, en az önemli)
            // olanlar sessizce değil, SAYILARAK atlanır.
            int skippedByCap = Math.Max(0, pendingChunks.Count - MaxDrainedChunksAfterStop);
            if (skippedByCap > 0)
                Interlocked.Add(ref _drainSkippedCount, skippedByCap);

            // Zaman bütçesi: model çağrıları iptal edilmiş ct İLE YAPILAMAZ (anında iptal olurdu) —
            // bu yüzden drene ederken taze bir CancellationTokenSource (timeout) kullanılır.
            // Bütçe dolması bir HATA değildir (aşağıdaki catch), yalnızca drenajı erken keser.
            using var drainCts = new CancellationTokenSource(DrainTimeBudget);
            int i = skippedByCap;
            try
            {
                for (; i < pendingChunks.Count; i++)
                    await ProcessOneAsync(pendingChunks[i], sessionId, progress, drainCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Zaman bütçesi doldu: elimizdeki kadarıyla devam ederiz (dürüstlük ilkesi —
                // sessizce yutma yok), kalan işlenemeyenler de atlanmış sayılır.
                int remaining = pendingChunks.Count - i;
                if (remaining > 0) Interlocked.Add(ref _drainSkippedCount, remaining);
            }
        }

        return BuildSummary(sessionId, startedAt, language);
    }

    /// <summary>Tek bir chunk'ı işleyip (VAD sessizse atlanır) zaman çizelgesine/depoya yazar.
    /// Bir chunk'ın işleme hatası hattı çökertmez (spec: degradasyon, çökme yok) — yalnızca
    /// iptal (<see cref="OperationCanceledException"/>) yukarı akar, çünkü o bir chunk hatası
    /// değil, koşunun kendisinin durumudur.</summary>
    private async Task ProcessOneAsync(
        AudioChunk chunk, Guid sessionId, IProgress<ChunkResult>? progress, CancellationToken ct)
    {
        ChunkResult? result;
        try
        {
            result = await _processor.ProcessAsync(chunk, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Bir chunk'ın hatası hattı çökertmez (spec: degradasyon, çökme yok).
            return;
        }

        if (result is null) return; // VAD sessiz
        _timeline.Add(result);
        progress?.Report(result);
        await _store.SaveChunkAsync(sessionId, result, ct).ConfigureAwait(false);
    }

    /// <summary>O ana kadar toplanan <see cref="Timeline"/>'dan bir <see cref="MeetingSummary"/> üretir.
    /// Motor kendisi bir koşunun ne zaman "normal" sayılacağına karar vermez — <see cref="RunAsync"/>
    /// akışın sonuna ulaştığında bunu kullanır; çağıran taraf (ör. canlı mikrofonda "Durdur" ile gelen
    /// iptal) bunu kendi normal-bitiş kararı için de kullanabilir (bkz. <c>PipelineAnalysisRunner</c>).
    /// Boş zaman çizelgesinde <see cref="IMeetingAggregator"/> geçerli, boş-ama-çökmeyen bir özet döner.</summary>
    public MeetingSummary BuildSummary(Guid sessionId, DateTimeOffset startedAt, DetectedLanguage language)
    {
        var session = new MeetingSession(sessionId, startedAt, language, _timeline.ToList());
        return _aggregator.Aggregate(session);
    }
}
