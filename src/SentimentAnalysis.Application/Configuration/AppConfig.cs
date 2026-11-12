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
    double? NeutralRadius = 0.2,
    // Yüzün valence ağırlığı (11d): tam güvenilir, tam görünür yüz = sesle eşit pay. KALİBRE EDİLMEMİŞ.
    double FaceValenceWeight = 1.0);

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
    int ChannelCapacity = 32,   // bounded kanal kapasitesi; taşarsa en eski chunk düşer
    // "Durdur" sonrası kanalda kalan chunk'ları işlemek için toplam zaman bütçesi. Bu süre
    // dolarsa drenaj bir HATA sayılmaz — elde ne varsa onunla bir özet üretilir ve işlenemeyen
    // chunk'lar DrainSkippedCount ile AYRICA raporlanır (sessizce yutulmaz).
    // Neden config, neden koda gömülü değil: makine hızı platforma göre değişir (Windows
    // dizüstü vs M1), ama kullanıcının "Durdur donmasın" garantisi değişmez — sabit gömmek,
    // tek bir platformda ölçülen sayıyı diğerine dayatmak demekti.
    // Neden 30: WarmUpAsync model yükleme maliyetini bu yoldan çıkardığı için drenajın kalan
    // tek maliyeti kararlı-durum çıkarım süresidir; daha uzun bir tolerans kullanıcıya donmuş
    // bir uygulama gibi görünür.
    double DrainTimeBudgetSeconds = 30.0);

/// <summary>Konuşmacı ayrımı (diarization) ayarları — spec 10.
/// Değerler 10a spike'ında GERÇEK DONANIMDA ÖLÇÜLDÜ; keyfi varsayılan değildir
/// (bkz. <c>10a-diarization-spike-olcumu.md</c>).</summary>
public sealed record DiarizationConfig(
    bool Enabled = false,                 // opt-in: kapalıyken davranış 10b öncesiyle aynı
                                          // (ses de saklanmaz — gizlilik varsayılanı, master §12.5)
    double ClusteringThreshold = 0.7,     // YENİDEN ÖLÇÜLDÜ (2026-10-27), wespeaker_en_voxceleb_resnet34_LM.
                                          // Küme SAYISI değil konuşmacı başına SÜRE okunarak:
                                          //   canlı kamera oturumu (76 sn, gerçek 2): 0,4→5 küme, 0,5–0,6→3, 0,7–0,8→2 (26,5 / 42,9 sn)
                                          //   iki-kisi.mp4 (77 sn, gerçek 2):         0,4→3 (2,5 sn kırıntı), 0,7→2 (13,9 / 60,1 sn)
                                          //   toplanti.wav (120,6 sn, gerçek 2):      0,4→"2" ama 111,9 / 1,9 sn (fiilen 1), ≥0,45→1
                                          // Önceki 0,4 seçimi "tam 2 küme" sayımına dayanıyordu; o iki kümeden biri 1,9 sn'lik
                                          // kırıntıydı. toplanti'daki iki ses bu modelle hiçbir eşikte (ve NumClusters=2 ile de)
                                          // düzgün ayrılmıyor — 0,7 onu kötüleştirmiyor, diğer ikisini düzeltiyor.
                                          // SINIR: 3+ kişili kayıtla doğrulanmadı; yüksek eşik benzer sesleri birleştirmeye eğilimli.
                                          // Ölçüm: scratchpad/diar-session-sweep (kayıtlı oturum sesi bellekte okunur).
    double MinOverlapRatio = 0.5,         // SpeakerAligner eşiği: örtüşme >= bu oran → kesin atama;
                                          // 0 < örtüşme < bu oran → yine en iyi adaya atanır ama
                                          // Uncertain=true işaretlenir; hiç örtüşme (aday) yoksa
                                          // Bilinmeyen döner. Amaç dürüst belirsizlik — bilgiyi
                                          // çöpe atmadan, uydurma atama da yapmadan (master §12.2)
    int MaxRecordedMinutes = 90,          // ses biriktirme üst sınırı (16 kHz PCM16 ≈ 115 MB/saat)
    // Konuşmacı prozodisi (10d): duygu, konuşmacının KENDİ sesinden ölçülür.
    // 1 sn altı bölgeler prozodi için yetersiz bağlam taşır; 60 sn ise ortalama için fazlasıyla
    // yeterlidir — tüm sesi işlemek doğruluk kazandırmadan süreyi katlar.
    // BAŞLANGIÇ DEĞERLERİ, KALİBRE EDİLMEMİŞ (eval-09).
    double SpeakerProsodyMinRegionSeconds = 1.0,
    double SpeakerProsodyBudgetSeconds = 60.0);

/// <summary>Karar destek motoru ayarları (spec 11 §6.4). BAŞLANGIÇ DEĞERLERİ, KALİBRE EDİLMEMİŞ.</summary>
public sealed record DecisionConfig(
    double InterventionThreshold = 0.30, // max ihtiyaç bunun altındaysa "Devam et" / olumlu takip adımı
    double InterventionExitThreshold = 0.25, // müdahale gösterilirken "Devam et"e dönmek için max ihtiyaç bunun altına inmeli (titreme bandı)
    double QualityRecovery = 0.30,       // "veri yetersiz" durumundan çıkmak için kalite bunun üstüne çıkmalı (titreme bandı)
    double SwitchMargin = 0.05,          // yeni öneri mevcudu bu kadar yakınlık farkıyla geçerse hemen değişir
    int SwitchPersistence = 2,           // ya da art arda bu kadar pencere önde kalırsa (titreme önleme)
    double InsufficientQuality = 0.25,   // altı: veri yetersiz → "Devam et", güven Düşük
    double LowQuality = 0.4,             // altı: güven Düşük
    double HighQuality = 0.7,            // üstü: güven Yüksek
    int TrendWindows = 6,                // K6 eğimi için son pencere sayısı (şimdiki dahil)
    int RepeatLimit = 2,                 // aynı öneri art arda bu kadar pencere gösterilince yakın alternatife geçilir (çeşitlilik)
    double DiversityMargin = 0.2,        // alternatifin yakınlığı birinciden en fazla bu kadar geride olabilir
    double PositiveThreshold = 0.5,      // olumluluk bunun üstündeyse (ve sorun yoksa) olumlu öneri verilir
    double ContextWeight = 0.3,          // duygu/tempo/hareket uyumunun TOPSIS yakınlığına katkısı (0 = kapalı)
    // 12: konuşma payı (ses tanıtıldıysa). TAHMİN, KALİBRE EDİLMEMİŞ.
    double TalkWindowSeconds = 60,       // konuşma payının bakıldığı son süre
    double TalkMinSeconds = 15,          // bu pencerede en az bu kadar konuşma yoksa pay bilinmiyor sayılır
    double HostDominanceCue = 0.6,       // yönetenin payı bunun üstünde: "dinle/sor/kısa tut" öne çıkar
    double HostDominanceRule = 0.75);    // başka sorun yokken bunun üstü: "Sus ve dinle" kuralla önerilir

/// <summary>Canlı konuşmacı rolü ayarları (12). Pencere/eşik 12a spike'ında ÖLÇÜLDÜ (TitaNet-small, 4 oturum,
/// dengeli doğruluk %95, LOSO %95). Tanıtma ayrı bir kayıtla yapılır: akış içi "ilk saniyeler" tanıtması gerçek
/// doğrulamada %64'e düştü (karşı taraf araya giriyor).</summary>
public sealed record RoleConfig(
    bool Enabled = true,                  // model yoksa özellik kendiliğinden kapanır (rol bilgisi gelmez)
    double WindowSeconds = 1.5,           // 12a: 1,5 sn → %95, 16 ms/pencere (2,0 sn %96 ama daha geç tepki)
    double HopSeconds = 0.5,
    double Threshold = 0.41,              // 12a tek eşik (LOSO ile kontrol edildi)
    double EnrollRecordSeconds = 8.0,     // "Sesimi tanıt" kaydının süresi (sessizlik ayıklanır)
    double MinEnrollSpeechSeconds = 4.0,  // ayıklandıktan sonra en az bu kadar konuşma olmalı
    double MinCounterpartSeconds = 2.0,   // karışık parçada karşı tarafın ayrı duygusu için en az konuşma (prozodi bağlamı)
    double NegligibleHostSeconds = 0.5,   // yönetenin konuşması bunun altındaysa parça zaten karşı tarafındır
    double DoubtThreshold = 0.45);        // tanıtmanın iki yarısı bundan az benzerse "iki kişi konuşmuş olabilir"
                                          // (2026-11-12 ölçümü: 3+3 sn karışık örneklerin 8/8'i ≤ 0,42; temizlerin 7/8'i ≥ 0,50)

/// <summary>Görüntü kanalı ayarları (spec 11 §6.2). Sayılar 11a spike'ında M1 Air 8 GB'ta ÖLÇÜLDÜ
/// (bkz. <c>11a-goruntu-spike-olcumu.md</c>); yüz boyu eşikleri ise tahmindir.</summary>
public sealed record VisionConfig(
    int CaptureFps = 5,                 // ffmpeg'den alınan kare hızı
    int AnalyzeEvery = 2,               // her N'inci kare analiz edilir (2,5 fps; kare başı ~72 ms)
    int OnnxThreads = 2,                // ORT IntraOpNumThreads (Whisper GPU'da, çakışma ölçülmedi)
    float MinFaceScore = 0.6f,          // YuNet skor eşiği
    float NmsIou = 0.3f,                // YuNet NMS
    double LookAwayYaw = 0.2,           // |yaw| bunun üstü "kameradan dönük" (düz ≤ 0,05, dönük medyan 0,37)
    double BaselineSeconds = 10,        // kişi başı ifade taban çizgisi süresi (dinlenme yüzü ~%26 "üzgün")
    double FirstFrameTimeoutSeconds = 10, // ilk kare gelmezse (izin istemi açık) anlamlı hata
    string CameraName = "FaceTime",     // kamera ADIYLA seçilir — Continuity Camera sırayı değiştirir
    double MinReliableFaceHeight = 48,  // KALİBRE EDİLMEMİŞ: bu boyun altında ifade güveni düşer
    double MinUsableFaceHeight = 24,    // KALİBRE EDİLMEMİŞ: bu boyda ifade güveni 0
    string YuNetModelFile = "face_detection_yunet_2023mar.onnx",
    string FerPlusModelFile = "emotion-ferplus-8.onnx",
    string? ModelDirectory = null,      // null → ~/Library/Application Support/SentimentAnalysis/models
    double CameraFirstFrameTimeoutSeconds = 60); // kamera: izin istemi açıkken ffmpeg bloklanır (11a §3)

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

    /// <summary>Karar destek motoru (spec 11). Additive, varsayılanlı.</summary>
    public DecisionConfig Decision { get; init; } = new();

    /// <summary>Görüntü kanalı (spec 11). Additive, varsayılanlı.</summary>
    public VisionConfig Vision { get; init; } = new();

    /// <summary>Canlı "Yöneten / Karşı taraf" ayrımı (12). Additive, varsayılanlı.</summary>
    public RoleConfig Role { get; init; } = new();

    public static AppConfig Default => new(
        Fusion: new FusionConfig(),
        Storage: new StorageConfig(),
        Stt: new SttConfig(),
        Emotion: new EmotionConfig(),
        Vad: new VadConfig(),
        Pipeline: new PipelineConfig());
}
