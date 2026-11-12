using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>"Sesimi tanıt" (12): mikrofondan tek bir <see cref="RoleConfig.EnrollRecordSeconds"/> sn'lik parça alır ve
/// <see cref="SpeakerRoleTracker.Enroll"/>'a verir. Ses diske yazılmaz; ses imzası yalnız bellekte kalır.</summary>
public sealed class MicrophoneVoiceEnrollment : IVoiceEnrollment
{
    private readonly SpeakerRoleTracker _tracker;
    private readonly AppConfig _config;
    private readonly IDiagnosticLog _log;

    public MicrophoneVoiceEnrollment(SpeakerRoleTracker tracker, AppConfig config, IDiagnosticLog log)
    {
        _tracker = tracker;
        _config = config;
        _log = log;
    }

    public bool IsEnrolled => _tracker.Enrolled;

    public double RecordSeconds => _config.Role.EnrollRecordSeconds;

    public void Clear() => _tracker.ClearEnrollment();

    public async Task<EnrollmentResult> RecordAsync(CancellationToken ct)
    {
        try
        {
            using var mic = new MicrophoneCaptureSource(_log);
            var options = new CaptureOptions(TimeSpan.FromSeconds(RecordSeconds), TimeSpan.Zero);
            await foreach (var chunk in mic.CaptureAsync(options, ct).ConfigureAwait(false))
                // Embedding (~3 çağrı) UI iş parçacığını tutmasın.
                return await Task.Run(() => _tracker.Enroll(chunk.Samples), ct).ConfigureAwait(false);
            return new EnrollmentResult(false, false, 0, "Mikrofondan ses alınamadı.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Mikrofon izni reddi, model eksik vb.: görüşme yine yapılabilir, yalnız rol ayrımı olmaz.
            return new EnrollmentResult(false, false, 0, $"Ses tanıtılamadı: {ex.Message}");
        }
    }
}
