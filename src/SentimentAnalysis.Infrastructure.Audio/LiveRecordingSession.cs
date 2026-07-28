using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed class LiveRecordingSession : ILiveRecordingSession
{
    private readonly IAudioCaptureSource _micSource;
    private readonly int _sampleRate = 16000;
    
    private string? _tempWavPath;
    private WavRecorder? _recorder;
    private CancellationTokenSource? _recordingCts;
    private Task? _recordTask;
    private bool _isRecording;

    public bool IsRecording => _isRecording;

    public LiveRecordingSession(IAudioCaptureSource micSource)
    {
        _micSource = micSource ?? throw new ArgumentNullException(nameof(micSource));
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_isRecording)
            throw new InvalidOperationException("Zaten bir kayıt sürüyor.");

        _isRecording = true;
        _tempWavPath = Path.Combine(Path.GetTempPath(), "sa-mic-" + Guid.NewGuid().ToString("N") + ".wav");
        _recorder = new WavRecorder(_tempWavPath, _sampleRate);
        
        // Link to the caller's cancellation token, so if application cancels we also abort
        _recordingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _recordTask = Task.Run(async () =>
        {
            try
            {
                var options = new CaptureOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero);
                await foreach (var chunk in _micSource.CaptureAsync(options, _recordingCts.Token).ConfigureAwait(false))
                {
                    _recorder.WriteSamples(chunk.Samples, chunk.SampleRate, 1);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal stop
            }
            catch (Exception)
            {
                Cleanup();
                throw;
            }
        });

        return Task.CompletedTask;
    }

    public async Task<RecordedAudioResult> StopAsync(CancellationToken ct)
    {
        if (!_isRecording)
            throw new InvalidOperationException("Aktif bir kayıt yok.");

        _isRecording = false;

        if (_recordingCts != null)
        {
            await _recordingCts.CancelAsync().ConfigureAwait(false);
        }

        if (_recordTask != null)
        {
            try
            {
                await _recordTask.ConfigureAwait(false);
            }
            catch { }
        }

        if (_recorder == null || _tempWavPath == null)
        {
            Cleanup();
            throw new InvalidOperationException("Kayıt başlatılamadı veya düzgün yapılandırılmadı.");
        }

        _recorder.FinalizeWav();
        var sampleCount = _recorder.SampleCount;
        var peak = _recorder.PeakSeen;

        _recorder.Dispose();
        _recorder = null;

        if (_recordingCts != null)
        {
            _recordingCts.Dispose();
            _recordingCts = null;
        }

        var path = _tempWavPath;
        _tempWavPath = null; // ownership transferred to caller
        
        return new RecordedAudioResult(path, sampleCount, peak);
    }

    private void Cleanup()
    {
        _isRecording = false;
        if (_recorder != null)
        {
            try { _recorder.Dispose(); } catch { }
            _recorder = null;
        }
        if (_tempWavPath != null && File.Exists(_tempWavPath))
        {
            try { File.Delete(_tempWavPath); } catch { }
            _tempWavPath = null;
        }
        if (_recordingCts != null)
        {
            try { _recordingCts.Dispose(); } catch { }
            _recordingCts = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cleanup();
        await Task.CompletedTask;
    }
}
