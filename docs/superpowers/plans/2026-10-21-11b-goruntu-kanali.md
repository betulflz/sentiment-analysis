# Alt-Proje 11b Uygulama Planı — Görüntü Kanalı

> **Agentic worker'lar için:** GEREKLİ ALT-BECERİ: `superpowers:subagent-driven-development` (önerilen)
> veya `superpowers:executing-plans`. Adımlar checkbox (`- [ ]`) sözdizimi kullanır.

**Goal:** Video dosyası (gerçek hızda) ve kameradan kare alıp yüz bulma, takip, ifade ve baş yönü
analizi yapmak. Her ses penceresi için 11c'nin tükettiği `FaceWindowSummary` listesini üretmek.

**Architecture:** Saf parçalar (takip, FER+ eşlemesi, taban çizgisi, pencere özeti, zaman çizelgesi)
`Application/Vision/` altında. Süreç ve ONNX parçaları yeni `SentimentAnalysis.Infrastructure.Vision`
projesinde. Görüntü hattı ses hattına paralel koşar: `VisionAnalyzer` kareleri işleyip
`VisionTimeline`'a yazar. Her `ChunkResult` geldiğinde aynı zaman aralığının özeti çekilir. Video
dosyasında ses de aynı dosyadan, gerçek hızda ayrı bir ffmpeg süreciyle gelir.

**Tech Stack:** .NET 9 · ONNX Runtime 1.28.0 · ffmpeg (Homebrew, alt süreç) · xUnit 2.9 + Xunit.SkippableFact 1.5.61.

**Spec:** [`2026-10-21-canli-gorusme-karar-destek-design.md`](../specs/2026-10-21-canli-gorusme-karar-destek-design.md) §3.1–3.2, §3.7–3.8, §6.2
· **Ölçüm:** [`11a-goruntu-spike-olcumu.md`](../specs/subprojects/11a-goruntu-spike-olcumu.md) (kontratlar ve sayılar buradan)

## Global Constraints

- **Görüntü diske yazılmaz** (spec §4). Kareler bellekte işlenip bırakılır.
- `IVideoCaptureSource` asla süresiz bloklanmaz: iptalde ffmpeg öldürülür; ilk kare `FirstFrameTimeoutSeconds` içinde gelmezse anlamlı bir istisna atılır (11a §3: izin istemi açıkken ffmpeg bloklanır).
- ffmpeg mutlak yolla çağrılır (11a §2: `.app` PATH'ine güvenilmez).
- Zaman ekseni: kare ve ses zamanları oturum başından itibaren (`TimeSpan`), aynı eksen.
- 11a sayıları: 5 fps alma, her 2. kare analiz, ORT `IntraOpNumThreads = 2`, YuNet skor 0.6, NMS IoU 0.3, |yaw| > 0.2 = dönük.
- `FaceWindowSummary` tipi 11c Task 1'de tanımlanır. Bu plan onu **üretir**, tanımını değiştirmez. (11c önce biter.)
- UI/native dilim (Task 6) **kullanıcı gerçek donanımda görmeden bitmez** (08e dersi).
- Her task sonunda tüm suite yeşil, 0 uyarı.

## Review Focus

1. **Kamera izni istemi açık / reddedilmiş:** kullanıcı süresiz donmuş ekran değil, Türkçe açıklama görür ("Kamera izni bekleniyor…", "Kamera izni reddedildi: Sistem Ayarları › Gizlilik › Kamera") — Task 4 testleri.
2. **Sessiz video** (ses yolu yok, ffmpeg exit 234): "Bu videoda ses yok" mesajı; görüntü analizi yine çalışır ama ses penceresi oluşmaz — Task 4 testi; Task 6 canlı doğrulama.
3. **Yüz bir süre kaybolup geri gelir** (kamera geçişi): aynı kişi yeni indeks almamalı (yerinden çok kaymadıysa) — Task 1 testi.
4. **"Durdur" görüntü hattını bekletmez:** ses hattı biter, görüntü görevi iptal edilir, ffmpeg süreçleri ölür (zombi süreç kalmaz) — Task 3 ve 4 testleri.
5. **Kişinin ilk 10 sn'si henüz yokken** taban çizgisi: varsayılan taban kullanılır, NaN yok — Task 2 testi.

## Dosya Yapısı

| Dosya | Sorumluluk | Task |
|---|---|---|
| `src/SentimentAnalysis.Core/Vision.cs` | `VideoFrame`, `FaceBox`, `FaceObservation` | 1 |
| `src/SentimentAnalysis.Application/Abstractions/Interfaces.cs` | + `IVideoCaptureSource`, `IFaceDetector`, `IFaceExpressionModel` | 1 |
| `src/SentimentAnalysis.Application/Configuration/AppConfig.cs` | + `VisionConfig`, `AppConfig.Vision` | 1 |
| `src/SentimentAnalysis.Application/Vision/FaceTracker.cs` | Kareler arası kararlı yüz indeksi | 1 |
| `src/SentimentAnalysis.Application/Vision/FaceSignals.cs` | FER+ olasılık → valence/olumsuzluk/gülümseme; yaw | 2 |
| `src/SentimentAnalysis.Application/Vision/VisionTimeline.cs` | Gözlem deposu + taban çizgisi + `Summarize(start,end)` | 2 |
| `src/SentimentAnalysis.Application/Vision/VisionAnalyzer.cs` | Kaynak → algıla → takip et → sınıfla → zaman çizelgesi | 3 |
| `src/SentimentAnalysis.Infrastructure.Vision/*` (yeni proje) | `FfmpegLocator`, `AvFoundationDevices`, `FfmpegVideoCaptureSource`, `FfmpegRealtimeAudioSource`, istisnalar | 4 |
| `src/SentimentAnalysis.Infrastructure.Vision/YuNet*.cs`, `FerPlus*.cs` | ONNX modelleri + saf çözümleme/ön işleme | 5 |
| `tests/SentimentAnalysis.Infrastructure.Vision.Tests/` (yeni) | Task 4–5 testleri | 4, 5 |
| `src/SentimentAnalysis.Desktop*/…` | Kamera modu, video dosyası, runner bağlantısı, satırda yüz notu | 6 |

---

### Task 1: Tipler, soyutlamalar, config, yüz takibi

**Interfaces — Produces:**
```csharp
// Core/Vision.cs
public sealed record VideoFrame(byte[] Rgb24, int Width, int Height, TimeSpan Time);
public readonly record struct FaceBox(float X, float Y, float W, float H);
/// Kps: 10 değer (sağ göz, sol göz, burun, ağız sağ, ağız sol; x,y sırasıyla). Kare pikselinde.
public sealed record DetectedFace(FaceBox Box, float Score, float[] Kps);
public sealed record FaceObservation(int FaceIndex, TimeSpan Time, FaceBox Box, double Yaw, float[] Expression);
// Interfaces.cs
public interface IVideoCaptureSource { IAsyncEnumerable<VideoFrame> CaptureAsync(CancellationToken ct); } // iptal sözleşmesi IAudioCaptureSource ile aynı
public interface IFaceDetector { IReadOnlyList<DetectedFace> Detect(VideoFrame frame); }
public interface IFaceExpressionModel { float[] Classify(VideoFrame frame, FaceBox box); } // 8 olasılık, FER+ sırası
// AppConfig.cs
public sealed record VisionConfig(int CaptureFps = 5, int AnalyzeEvery = 2, int OnnxThreads = 2,
    float MinFaceScore = 0.6f, float NmsIou = 0.3f, double LookAwayYaw = 0.2, double BaselineSeconds = 10,
    double FirstFrameTimeoutSeconds = 10, string CameraName = "FaceTime",
    double MinReliableFaceHeight = 48, double MinUsableFaceHeight = 24, // KALİBRE EDİLMEMİŞ (11a §7)
    string YuNetModelFile = "face_detection_yunet_2023mar.onnx", string FerPlusModelFile = "emotion-ferplus-8.onnx",
    string? ModelDirectory = null);
// AppConfig: public VisionConfig Vision { get; init; } = new();
// Application/Vision/FaceTracker.cs
public sealed class FaceTracker { public IReadOnlyList<(int Index, DetectedFace Face)> Assign(IReadOnlyList<DetectedFace> faces); }
```
Takip kuralı: ilk karede yüzler soldan sağa 0,1,2… alır. Sonraki karelerde her yüz, bilinen izlerle
IoU > 0.3 ise o ize, değilse merkezi son bilinen merkeze `0.5 × kutu genişliği`nden yakın olan ize
(en yakın) atanır. Hiçbiri tutmazsa yeni indeks alır. Bir iz aynı karede iki yüze verilmez. İzler
silinmez (kaybolan yüz geri gelince aynı indeks).

- [ ] **Step 1: Failing tests** (`Application.Tests/Vision/FaceTrackerTests.cs`): `FirstFrame_LeftToRight`;
  `SmallMovement_KeepsIndex`; `Reappears_AfterGap_KeepsIndex` *(Review Focus 3)*; `NewFaceFarAway_GetsNextIndex`;
  `TwoFacesNeverShareIndex`; `AppConfig_Default_HasVisionDefaults` (`AnalyzeEvery == 2`).
- [ ] **Step 2:** FAIL. **Step 3:** Uygula. **Step 4:** PASS. **Step 5:** Commit `feat(11b): görüntü tipleri, config ve yüz takibi`.

### Task 2: Yüz sinyalleri ve zaman çizelgesi

**Interfaces — Consumes:** Task 1. `FaceWindowSummary` (11c).
**Produces:**
```csharp
public static class FaceSignals
{
    // FER+ sırası: nötr, mutlu, şaşkın, üzgün, kızgın, iğrenmiş, korkmuş, küçümseyen
    public static readonly double[] ClassValence = { 0, 0.8, 0.2, -0.6, -0.7, -0.6, -0.6, -0.5 };
    public static double Valence(float[] p);            // Σ p·ClassValence
    public static double Negative(float[] p);           // p[3]+p[4]+p[5]+p[6]+p[7]
    public static bool Smiling(float[] p);              // p[1] > 0.5
    public static double Yaw(float[] kps);              // (burun_x − göz_orta_x)/göz_arası; göz_arası < 1 → 0
    public static double Reliability(FaceBox box, VisionConfig cfg); // c01((H − MinUsable)/(MinReliable − MinUsable))
}
public sealed class VisionTimeline
{
    public VisionTimeline(VisionConfig cfg);
    public void AddFrame(TimeSpan time, IReadOnlyList<FaceObservation> faces); // yüzsüz analiz karesi de eklenir (görünürlük paydası)
    public IReadOnlyList<FaceWindowSummary> Summarize(TimeSpan start, TimeSpan end); // thread-safe
    public void Clear();
}
```
`Summarize`: [start, end) aralığındaki analiz kareleri. Bu aralıkta en az bir kez görülen her yüz için:
VisibleRatio = görüldüğü kare / aralıktaki analiz karesi; LookAwayRatio = görüldüğü karelerde |yaw| > LookAwayYaw oranı;
taban çizgisi = yüzün ilk `BaselineSeconds` içindeki gözlemlerinin ortalama Valence ve Negative'i (o süre dolana dek
o ana kadarki gözlemler; hiç yoksa varsayılan **valence −0,15, negative 0,27** — 11a §5 dinlenme yüzü).
ValenceShift = c(ort. Valence − taban, −1, 1); NegativeExpression = c01((ort. Negative − tabanNeg)/(1 − tabanNeg));
SmileRatio = gülümseyen kare oranı; Reliability = ortalama `Reliability(box)`. Sonuç FaceIndex sırasıyla.

- [ ] **Step 1: Failing tests:** `FaceSignals_Valence_KnownVector` (`p` = mutlu 1 → 0.8; nötr .73 üzgün .26 → −0.156);
  `Yaw_Frontal_NearZero`, `Yaw_Turned`; `Reliability_Ramp` (H 24 → 0, 36 → 0.5, 48+ → 1);
  `Summarize_VisibleAndLookAway`; `Summarize_BaselineRemovesRestingSadness` (ilk 10 sn nötr .73/üzgün .26, sonra aynı → ValenceShift ≈ 0, NegativeExpression ≈ 0);
  `Summarize_BeforeBaseline_UsesDefault_NoNaN` *(Review Focus 5)*; `Summarize_EmptyWindow_EmptyList`;
  `Summarize_ConcurrentAddAndRead_NoException` (paralel 1000 AddFrame + Summarize).
- [ ] **Step 2:** FAIL. **Step 3:** Uygula. **Step 4:** PASS. **Step 5:** Commit `feat(11b): yüz sinyalleri, taban çizgisi ve pencere özeti`.

### Task 3: Görüntü analizcisi

**Produces:**
```csharp
public sealed class VisionAnalyzer
{
    public VisionAnalyzer(IFaceDetector detector, IFaceExpressionModel expression, VisionTimeline timeline, VisionConfig cfg);
    /// Kaynak bitene / ct iptal olana dek. Her AnalyzeEvery'inci kare işlenir. Bir karedeki model hatası
    /// o kareyi atlar ve ErrorCount'u artırır (hat düşmez). Kaynak istisnası yukarı fırlar.
    public Task RunAsync(IVideoCaptureSource source, CancellationToken ct);
    public VideoFrame? LatestFrame { get; }                       // 11d önizlemesi için (son alınan kare)
    public IReadOnlyList<(int Index, FaceBox Box)> LatestFaces { get; }
    public int ErrorCount { get; }
}
```
- [ ] **Step 1: Failing tests** (sahte kaynak/algılayıcı/model): `ProcessesEveryNthFrame` (10 kare, AnalyzeEvery 2 → algılayıcı 5 çağrı);
  `WritesObservationsToTimeline`; `DetectorThrows_SkipsFrame_CountsError`; `Cancellation_StopsPromptly` (sonsuz kaynak, iptal → 1 sn içinde biter) *(Review Focus 4)*;
  `SourceThrows_Propagates`.
- [ ] **Step 2:** FAIL. **Step 3:** Uygula. **Step 4:** PASS. **Step 5:** Commit `feat(11b): görüntü analizcisi`.

### Task 4: ffmpeg kaynakları (yeni Infrastructure.Vision projesi)

**Files:** `src/SentimentAnalysis.Infrastructure.Vision/SentimentAnalysis.Infrastructure.Vision.csproj` (Application + Infrastructure.Audio referansı, OnnxRuntime 1.28.0), test projesi (Emotion.Tests csproj deseni, SkippableFact). İkisi `SentimentAnalysis.sln`'e eklenir.

**Produces:**
```csharp
public sealed class VideoSourceException : Exception { public VideoSourceException(string message, Exception? inner = null); }
public static class FfmpegLocator { public static string Find(); } // SENTIMENT_FFMPEG_PATH → /opt/homebrew/bin → /usr/local/bin → PATH; yoksa VideoSourceException("ffmpeg bulunamadı. Kurmak için: brew install ffmpeg")
public static class AvFoundationDevices { public static int? FindVideoIndex(string listDevicesStderr, string nameContains); } // yalnızca "video devices" bölümü
public static class FfmpegArgs
{
    public static string VideoFile(string path, int fps);   // "-hide_banner -loglevel error -re -i \"{path}\" -vf \"fps={fps},scale=640:360:force_original_aspect_ratio=decrease,pad=640:360:(ow-iw)/2:(oh-ih)/2\" -pix_fmt rgb24 -f rawvideo pipe:1"
    public static string Camera(int index, int fps);        // "-hide_banner -loglevel error -f avfoundation -pixel_format nv12 -framerate 30 -video_size 1280x720 -i \"{index}:none\" -vf \"fps={fps},scale=640:360\" -pix_fmt rgb24 -f rawvideo pipe:1"
    public static string AudioFileRealtime(string path);    // "-hide_banner -loglevel error -re -i \"{path}\" -vn -ac 1 -ar 16000 -f s16le pipe:1"
    public static string ExplainExit(int exitCode, string stderr, bool camera); // Türkçe kullanıcı mesajı
}
public sealed class FfmpegVideoCaptureSource : IVideoCaptureSource
{
    public static FfmpegVideoCaptureSource ForFile(string path, VisionConfig cfg);
    public static FfmpegVideoCaptureSource ForCamera(VisionConfig cfg); // -list_devices çıktısından CameraName ile indeks
}
public sealed class FfmpegRealtimeAudioSource : IAudioCaptureSource { public FfmpegRealtimeAudioSource(string path); } // Pcm16Converter + StreamingChunkBuffer
```
Kare zamanı = `kare_sırası / CaptureFps`. İlk kare `FirstFrameTimeoutSeconds` içinde gelmezse kamera →
`VideoSourceException("Kamera izni bekleniyor ya da kamera açılamadı…")`. Süreç ≠ 0 çıkarsa → `ExplainExit`.
İptal ya da `DisposeAsync` olunca süreç `Kill(entireProcessTree: true)` ile durdurulur.

`ExplainExit` eşlemeleri (11a ölçümleri): stderr'de `Cannot use` + kamera → "Kamera izni reddedildi. Sistem Ayarları › Gizlilik ve Güvenlik › Kamera'dan izin verin." ·
`does not contain any stream` → "Bu videoda ses yok — yalnızca görüntü analiz edilecek." · `No such file` → "Video dosyası bulunamadı." · diğer → "ffmpeg hata verdi (kod {exit}): {stderr ilk satır}".

- [ ] **Step 1: Failing tests:** `FindVideoIndex_ParsesSpikeOutput` (11a §3 listesi: "FaceTime" → 0, "iPhone" → 1, ses bölümündeki "Mikrofon" → null);
  `Args_VideoFile_ContainsRealtimeAndFilter`; `ExplainExit_CameraDenied`, `_NoAudio`, `_Missing`, `_Other` *(Review Focus 1, 2)*;
  `FfmpegLocator_EnvPathMissing_Throws`; SkippableFact (env `SENTIMENT_TEST_VIDEO`): `VideoFile_RealVideo_FramesAtFps` (ilk 2 sn'de 9–11 kare, boyut 691200),
  `Audio_RealVideo_ProducesChunks`, `Cancel_KillsProcess` (iptal sonrası 2 sn içinde ffmpeg süreci yok) *(Review Focus 4)*.
- [ ] **Step 2:** FAIL. **Step 3:** Uygula. **Step 4:** PASS (yerel olarak `SENTIMENT_TEST_VIDEO=scratchpad/test-videolari/iki-kisi.mp4` ile atlanmadan da koş).
- [ ] **Step 5:** Commit `feat(11b): ffmpeg görüntü ve gerçek hızlı ses kaynakları`.

### Task 5: YuNet ve FER+ modelleri

**Produces:**
```csharp
public static class YuNetDecoder
{
    /// 11a §4'teki doğrulanmış çözümleme. outputs: "cls_8".."kps_32" → düz float dizileri.
    public static IReadOnlyList<DetectedFace> Decode(IReadOnlyDictionary<string, float[]> outputs, float minScore, float nmsIou);
    public static float[] ToInput(VideoFrame frame); // 1×3×640×640 BGR 0–255, 640×360 kare üstte, altı 0
}
public static class FerPlusPreprocess { public static float[] ToInput(VideoFrame frame, FaceBox box); } // kare kırp (kenar max(w,h)·1.15), gri, 64×64 bilinear, 0–255
public sealed class YuNetFaceDetector : IFaceDetector, IDisposable { public YuNetFaceDetector(VisionConfig cfg); }
public sealed class FerPlusExpressionModel : IFaceExpressionModel, IDisposable { public FerPlusExpressionModel(VisionConfig cfg); } // softmax
```
Model yolu: `ModelDirectory ?? ~/Library/Application Support/SentimentAnalysis/models`. Dosya yoksa
`VideoSourceException("Yüz modeli bulunamadı: {yol}")`.

- [ ] **Step 1: Failing tests:** `Decode_SingleSyntheticAnchor` (stride 8, satır 10 sütun 20, cls=obj=1, bbox `0,0,ln2,ln2` → kutu merkezi (160,80), w=h=16, skor 1);
  `Decode_BelowThreshold_Empty`; `Decode_Nms_KeepsHigher`; `ToInput_BgrOrderAndPadding` (kırmızı piksel → kanal 2 = 255; y=400 → 0);
  `FerPreprocess_UniformGray_ConstantInput`; SkippableFact (model + `SENTIMENT_TEST_VIDEO`): `RealModels_InterviewVideo_TwoFaces` (ilk 20 analiz karesinde her karede 2 yüz),
  `RealModels_Classify_SumsToOne`.
- [ ] **Step 2:** FAIL. **Step 3:** Uygula. **Step 4:** PASS. **Step 5:** Commit `feat(11b): YuNet ve FER+ ONNX modelleri`.

### Task 6: Uygulamaya bağlama + canlı doğrulama

**Files:** Modify `Desktop/CaptureMode.cs` (+`Kamera`), `Desktop/LiveViewModel.cs` (+`UseCamera`, mikrofonla karşılıklı dışlayıcı), `Desktop/ChunkRowVm.cs` (+`FaceNote`),
`Desktop/IAnalysisRunner.cs` (+ additive `IReadOnlyList<FaceWindowSummary> FacesFor(ChunkResult r) => Array.Empty<…>()`),
`Desktop.Avalonia/PipelineAnalysisRunner.cs`, `CompositionRoot.cs`, `AvaloniaAudioFilePicker.cs` (+ "Video": `*.mp4 *.mov *.m4v`), `Info.plist` (+`NSCameraUsageDescription`), `Views/LiveView.axaml` (kamera anahtarı, satırda `FaceNote`).

Davranış:
- Dosya modu + video uzantısı (`.mp4 .mov .m4v`) → ses `FfmpegRealtimeAudioSource`, görüntü `FfmpegVideoCaptureSource.ForFile`. Kamera modu → `MicrophoneCaptureSource` + `ForCamera`. Ses dosyası ve mikrofon modunda görüntü yok (davranış değişmez).
- Video/Kamera modunda chunk 10 sn / örtüşme 2 sn (mikrofon gibi).
- Runner görüntü görevini ses hattıyla birlikte başlatır. Ses hattı bitince ya da Durdur'a basılınca görüntü görevini iptal edip bekler. Görüntü hatası (`VideoSourceException`) analizi durdurmaz: mesaj `CaptureError`'a yazılır, ses analizi sürer.
- Sessiz videoda (`FfmpegRealtimeAudioSource` "Bu videoda ses yok") kullanıcı mesajı görür. Görüntü analizi süre boyunca devam eder.
- `ChunkRowVm.FaceNote`: `"Yüz 1: dönük %20, olumsuz ifade %10, gülümseme %0 · Yüz 2: …"`. Yüz yoksa boş.
- Info.plist: `NSCameraUsageDescription` = "Görüşülen kişinin yüz ifadesini analiz edebilmek için kameraya erişmesi gerekiyor. Görüntü yalnızca bu bilgisayarda işlenir, kaydedilmez."

- [ ] **Step 1: Failing tests** (`Desktop.Tests`): `UseCamera_SetsModeKamera_AndClearsMicrophone`; `ChunkRowVm_FaceNote_Format`;
  `ChunkRowVm_NoFaces_EmptyNote`; `LiveViewModel_Report_UsesRunnerFacesFor` (sahte runner).
- [ ] **Step 2:** FAIL. **Step 3:** Uygula (runner ve composition root testsiz app katmanı; 08e notu). **Step 4:** Tüm suite PASS, 0 uyarı.
- [ ] **Step 5: Canlı doğrulama — KULLANICI KAPISI.** `./build-macos-app.sh` → `open`. Kullanıcı şunları görür ve onaylar:
  (a) `iki-kisi.mp4`: video gerçek hızda ilerliyor, satırlarda `Yüz 1/Yüz 2` notları var;
  (b) kamera modu: izin istemi çıkıyor, izin sonrası satırlarda yüz notu var;
  (c) Durdur iki modda da 2 sn içinde raporu açıyor ve `pgrep ffmpeg` boş.
  Onay gelmeden commit edilmez.
- [ ] **Step 6:** Commit `feat(11b): video ve kamera modlarını uygulamaya bağla`.
