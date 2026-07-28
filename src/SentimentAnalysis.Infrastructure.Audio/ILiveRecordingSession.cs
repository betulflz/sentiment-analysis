using System;
using System.Threading;
using System.Threading.Tasks;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed record RecordedAudioResult(string WavPath, int SampleCount, double PeakAmplitude);

public interface ILiveRecordingSession : IAsyncDisposable
{
    bool IsRecording { get; }
    Task StartAsync(CancellationToken ct);
    Task<RecordedAudioResult> StopAsync(CancellationToken ct);
}
