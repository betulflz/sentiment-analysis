namespace SentimentAnalysis.Application.Configuration;

/// <summary>Füzyon başlangıç ağırlıkları ve eşikleri. Nihai değerler
/// Alt-proje 09 (eval) ile kalibre edilip buraya yansıtılır — keyfi sabit değil.</summary>
public sealed record FusionConfig(
    double AudioArousalWeight = 1.0,
    double TextArousalWeight  = 0.0,
    double TextValenceWeight  = 0.0,
    double AudioValenceWeight = 1.0,
    double IncongruenceThreshold = 0.8,       // |valence_text − valence_audio| eşiği
    double LowConfidenceThreshold = 0.35,     // altı "düşük güven"
    double HighConfidenceThreshold = 0.65,    // üstü "yüksek güven"
    double IncongruentValenceProsodyWeight = 1.0, // uyumsuzda valence prozodiye yaslanma payı
    double SnrLowDb = 0.0,                     // bu SNR'de ses güvenilirliği ~0
    double SnrHighDb = 15.0,                   // bu SNR'de ses güvenilirliği ~1
    double MinReliableSeconds = 3.0,           // altında süre kısa → ses güvenilirliği düşer
    double MinReliableWords = 3.0,             // altında transkript kısa → metin güvenilirliği düşer
    double LogProbFloor = -1.0,                // Whisper avg-logprob tabanı (bu değerde güven ~0)
    double IronyValenceFloor = 0.3,            // ironi için metin en az bu kadar olumlu
    double IronyArousalCeiling = 0.35,         // ironi için ses "düz" (düşük arousal)
    double IronyAudioValenceCeiling = 0.25,    // ironi için ses net olumlu değil
    int PeakMomentCount = 3,                   // toplantı özetinde tepe an sayısı
    double MinReliabilityForHighConfidence = 0.5, // rAudio*rText bunun altındaysa "Yuksek" güven verilmez
    double DistributionTemperature = 0.35,     // 8-duygu yakınlık payı keskinliği — KALİBRE EDİLMEMİŞ (eval-09)
    // Nötr yarıçapı: Nötr artık "en yakın merkez" yarışına girmez; merkez (0, 0.5) çevresindeki
    // bu yarıçaplı diskin dışındaki her nokta gerçek 7 duygudan birine düşer. Klasik kuralda
    // Nötr merkezde, diğerleri kenarda olduğu için Nötr orantısız büyük bir havza kazanıyordu
    // (a=0,5'te valence −0,33…+0,40 arası her şey Nötr). null → klasik kural.
    // BAŞLANGIÇ DEĞERİ, KALİBRE EDİLMEMİŞ (eval-09).
    double? NeutralRadius = 0.2);

/// <summary>Depolama/gizlilik ayarları (master §D13).</summary>
public sealed record StorageConfig(
    int AudioRetentionDays = 7,
    bool KeepAudio = true,
    string OpusBitrate = "24k",
    string? StorageDirectory = null);   // 07 — db + ses + anahtar konumu; null → uygulama-veri varsayılanı

/// <summary>STT profil ayarları (master §D6, spec 03 fizibilite ölçümü).</summary>
public sealed record SttConfig(
    string LiveModel = "large-v3-turbo",   // canlı varsayılan (M1 8GB'da ~779 MB, RTF 0.17)
    string? DeepModel = "large-v3",        // opsiyonel toplantı-sonu derin geçiş (1.91 GB tepe)
    string Quantization = "q5_0",          // model dosya adı: ggml-{model}-{quantization}.bin
    string? ModelDirectory = null,         // null → FileSttModelProvider varsayılanı (uygulama veri klasörü)
    int Threads = 4,                       // spike'ta 4 iş parçacığı optimaldi
    string? Language = "tr");              // null → otomatik dil algılama (çok-dilli-hazır)

/// <summary>Duygu modelleri (prozodi + metin) ayarları (spec 04).</summary>
public sealed record EmotionConfig(
    string ProsodyModelFile = "prosody-msp-dim.onnx", // audeering wav2vec2 boyutsal (model.onnx yeniden adlandırılır)
    string? ModelDirectory = null,                    // null → FileEmotionModelProvider varsayılanı
    // ÖLÇÜLDÜ (2026-07-28, M1 Air 8GB): CoreML bu iki model için ZARARLI.
    //   metin duygu (XLM-R, 1.1 GB): coreml 36,6-44,4 sn  vs  cpu 2,5 sn   (~15x YAVAŞ)
    //   prozodi (msp-dim):           coreml 2,5-3,1 sn    vs  cpu 1,4 sn
    // Çıktılar birebir aynı (va=0.80/0.70) — CoreML doğruluk kazancı sağlamıyor, yalnızca
    // model derleme maliyeti getiriyor ve bu maliyet SÜREÇLER ARASI ÖNBELLEKLENMİYOR
    // (her açılışta yeniden ödeniyor). Bir koşuda CoreML derlemesi "I/O error" ile de patladı.
    // Sonuç: canlı mikrofonda ilk chunk 40+ sn sürüyor, "Durdur" sonrası 30 sn'lik drenaj
    // bütçesini aşıyor ve kullanıcı SIFIR sonuç görüyordu. Bkz. master §8 (CoreML varsayımı
    // bu ölçümle çürütüldü) ve §12.4 (kararlar ölçülür).
    string ExecutionProvider = "cpu",                 // "cpu" (ölçülmüş varsayılan) | "coreml"
    string TextModelFile = "text-emotion-xlmr.onnx",  // 04b — XLM-R 11-duygu ONNX
    string TokenizerFile = "sentencepiece.bpe.model", // 04b — XLM-R-base SentencePiece (~5 MB)
    // Prozodi kazancı: msp-dim ham valence'ı pratikte 0,35–0,65 bandında kalır → domain'de
    // −0,3…+0,3. Bu aralık EmotionWheel'in nötr havzasının TAMAMEN içindedir; yani kazançsız
    // ses kanalı, ton ne olursa olsun, etiketi Nötr'ün dışına taşıyamaz. Kazanç nötr merkez
    // etrafında simetrik genişletmedir — işareti değil yayılımı düzeltir.
    // BAŞLANGIÇ DEĞERİ, KALİBRE EDİLMEMİŞ (eval-09 Türkçe etiketli veriyle ölçecek).
    double ProsodyValenceGain = 2.0,
    double ProsodyArousalGain = 2.0);

/// <summary>VAD (Silero) ayarları (spec 05). Eşik eval-09 kalibre eder.</summary>
public sealed record VadConfig(
    string VadModelFile = "silero_vad.onnx",
    string? ModelDirectory = null,        // null → FileVadModelProvider varsayılanı
    double SpeechThreshold = 0.5);        // max pencere prob ≥ bu → konuşma

/// <summary>Pipeline orkestrasyon ayarları (spec 06).</summary>
public sealed record PipelineConfig(
    int ChannelCapacity = 32);   // bounded kanal kapasitesi; taşarsa en eski chunk düşer

/// <summary>Konuşmacı ayrımı (diarization) ayarları — spec 10.
/// Değerler 10a spike'ında GERÇEK DONANIMDA ÖLÇÜLDÜ; keyfi varsayılan değildir
/// (bkz. <c>10a-diarization-spike-olcumu.md</c>).</summary>
public sealed record DiarizationConfig(
    bool Enabled = false,                 // opt-in: kapalıyken davranış 10b öncesiyle aynı
                                          // (ses de saklanmaz — gizlilik varsayılanı, master §12.5)
    double ClusteringThreshold = 0.5,     // wespeaker_en_voxceleb_resnet34_LM ile ölçüldü: iki
                                          // konuşmacılı gerçek seste 0,3–0,5 aralığı tam 2 küme
                                          // buluyor, 0,6+ tek kümeye çöküyor (embedding modeli
                                          // 3dspeaker'dan wespeaker'a değişince eşik de değişti —
                                          // önceki 0,8, farklı bir embedding uzayı için ölçülmüştü)
    double MinOverlapRatio = 0.5,         // SpeakerAligner kapısı: altında kalan → Bilinmeyen
    int MaxRecordedMinutes = 90);         // ses biriktirme üst sınırı (16 kHz PCM16 ≈ 115 MB/saat)

/// <summary>Uygulama genel konfigürasyonu (stajyerin config.py karşılığı).</summary>
public sealed record AppConfig(
    FusionConfig Fusion,
    StorageConfig Storage,
    SttConfig Stt,
    EmotionConfig Emotion,
    VadConfig Vad,
    PipelineConfig Pipeline)
{
    /// <summary>Konuşmacı ayrımı (spec 10). Additive: varsayılanlı olduğundan mevcut
    /// <c>AppConfig</c> kurucu çağrılarının hiçbiri değişmez.</summary>
    public DiarizationConfig Diarization { get; init; } = new();

    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig(),
        Emotion: new EmotionConfig(),
        Vad: new VadConfig(),
        Pipeline: new PipelineConfig());
}
