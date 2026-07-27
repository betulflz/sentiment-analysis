namespace SentimentAnalysis.Application.Configuration;

/// <summary>Füzyon başlangıç ağırlıkları ve eşikleri. Nihai değerler
/// Alt-proje 09 (eval) ile kalibre edilip buraya yansıtılır — keyfi sabit değil.</summary>
public sealed record FusionConfig(
    double AudioArousalWeight = 0.75,
    double TextArousalWeight  = 0.25,
    double TextValenceWeight  = 0.70,
    double AudioValenceWeight = 0.30,
    double IncongruenceThreshold = 0.8,       // |valence_text − valence_audio| eşiği
    double LowConfidenceThreshold = 0.35,     // altı "düşük güven"
    double HighConfidenceThreshold = 0.65,    // üstü "yüksek güven"
    double IncongruentValenceProsodyWeight = 0.75, // uyumsuzda valence prozodiye yaslanma payı
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
    double DistributionTemperature = 0.35);    // 8-duygu yakınlık payı softmax sıcaklığı (spec 08d)

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
    string? Language = null)               // null → otomatik dil algılama; "tr"/"en" → sabitle
{
    /// <summary>Ayarlar arayüzünün kullandığı "otomatik" değeri (null'ın metin karşılığı).</summary>
    public const string AutoLanguage = "auto";

    /// <summary>
    /// Whisper'a verilecek dil kodu; <c>null</c> ise otomatik algılama.
    /// <para>Varsayılan <c>"tr"</c> iken İngilizce konuşma da Türkçe olarak <b>çözülmeye zorlanıyordu</b>:
    /// Whisper dili sabitlendiğinde İngilizceyi Türkçe fonetiğe uydurup anlamsız metin üretir, metin
    /// duygu kanalı da o metni analiz ederdi. Çok-dilli olmak tasarımın açık hedefi olduğundan
    /// (XLM-R zaten 100+ dil kapsar) varsayılan otomatik algılamaya alındı.</para>
    /// </summary>
    public string? WhisperLanguage =>
        string.IsNullOrWhiteSpace(Language) || Language.Equals(AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? null
            : Language;
}

/// <summary>Duygu modelleri (prozodi + metin) ayarları (spec 04).</summary>
public sealed record EmotionConfig(
    string ProsodyModelFile = "prosody-msp-dim.onnx", // audeering wav2vec2 boyutsal (model.onnx yeniden adlandırılır)
    string? ModelDirectory = null,                    // null → FileEmotionModelProvider varsayılanı
    string? ExecutionProvider = null,                 // null → platforma göre otomatik; "coreml" | "cpu"
    string TextModelFile = "text-emotion-xlmr.onnx",  // 04b — XLM-R 11-duygu ONNX
    string TokenizerFile = "sentencepiece.bpe.model", // 04b — XLM-R-base SentencePiece (~5 MB)
    // --- Türkçe'ye özel ikinci prozodi kanalı (SeaBenSea HuBERT, TurEV-DB) ---
    string TurkishSerModelFile = "prosody-turkish-ser.onnx",
    /// <summary>null → dosya varsa kullan, yoksa sessizce yalnızca genel modelle devam et.
    /// true/false ile açıkça zorlanabilir (true iken dosya yoksa açılışta anlaşılır hata verir).</summary>
    bool? UseTurkishSer = null,
    double GeneralProsodyWeight = 0.5,   // audeering (çok dilli, boyutsal) taban payı
    double TurkishSerWeight = 0.5)       // TurEV-DB (Türkçe'ye özel) taban payı — eval-09 kalibre eder
{
    /// <summary>
    /// Etkin ONNX Runtime execution provider'ı. <see cref="ExecutionProvider"/> açıkça verilmediyse
    /// platforma göre seçilir: macOS → CoreML (yoksa CPU'ya düşer), diğerleri → CPU.
    /// <para>Önceki sürümde varsayılan sabit <c>"coreml"</c>'di; Windows/Linux'ta bu her oturum
    /// açılışında başarısız bir <c>AppendExecutionProvider_CoreML</c> denemesine yol açıyordu
    /// (yakalanıyordu ama anlamsız işti ve niyeti gizliyordu).</para>
    /// </summary>
    public string ResolvedExecutionProvider =>
        ExecutionProvider ?? (OperatingSystem.IsMacOS() ? "coreml" : "cpu");
}

/// <summary>VAD (Silero) ayarları (spec 05). Eşik eval-09 kalibre eder.</summary>
public sealed record VadConfig(
    string VadModelFile = "silero_vad.onnx",
    string? ModelDirectory = null,        // null → FileVadModelProvider varsayılanı
    double SpeechThreshold = 0.5);        // max pencere prob ≥ bu → konuşma

/// <summary>Pipeline orkestrasyon ayarları (spec 06).</summary>
public sealed record PipelineConfig(
    int ChannelCapacity = 32);   // bounded kanal kapasitesi; taşarsa en eski chunk düşer

/// <summary>Uygulama genel konfigürasyonu (stajyerin config.py karşılığı).</summary>
public sealed record AppConfig(
    FusionConfig Fusion,
    StorageConfig Storage,
    SttConfig Stt,
    EmotionConfig Emotion,
    VadConfig Vad,
    PipelineConfig Pipeline)
{
    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig(),
        Emotion: new EmotionConfig(),
        Vad: new VadConfig(),
        Pipeline: new PipelineConfig());
}
