using System;
using System.IO;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed class WavRecorder : IDisposable
{
    private readonly FileStream _fs;
    private readonly BinaryWriter _writer;
    private readonly int _sampleRate;
    private readonly short _channels;
    private readonly short _bitsPerSample;
    private long _dataBytesWritten;
    private bool _isFinalized;
    private readonly object _lock = new();

    public int SampleRate => _sampleRate;
    public short Channels => _channels;
    public int SampleCount => (int)(_dataBytesWritten / (_bitsPerSample / 8) / _channels);
    public double PeakSeen { get; private set; }

    public WavRecorder(string path, int sampleRate = 16000, short channels = 1, short bitsPerSample = 16)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _bitsPerSample = bitsPerSample;

        _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        _writer = new BinaryWriter(_fs);

        // Write dummy header (44 bytes) to reserve space
        var dummy = new byte[44];
        _writer.Write(dummy);
    }

    public void WriteSamples(float[] samples, int sampleRate = 16000, short channels = 1)
    {
        lock (_lock)
        {
            if (_isFinalized)
                throw new InvalidOperationException("WavRecorder finalize edildikten sonra veri kabul edemez.");

            if (sampleRate != _sampleRate)
                throw new ArgumentException($"Geçersiz örnekleme hızı: {sampleRate}. Beklenen: {_sampleRate}");

            if (channels != _channels)
                throw new ArgumentException($"Geçersiz kanal sayısı: {channels}. Beklenen: {_channels}");

            foreach (var s in samples)
            {
                double abs = Math.Abs(s);
                if (abs > PeakSeen) PeakSeen = abs;

                float clamped = Math.Clamp(s, -1.0f, 1.0f);
                short pcmSample = (short)(clamped * 32767);
                _writer.Write(pcmSample);
                _dataBytesWritten += 2;
            }
        }
    }

    public void FinalizeWav()
    {
        lock (_lock)
        {
            if (_isFinalized) return;
            _isFinalized = true;

            _fs.Seek(0, SeekOrigin.Begin);

            int fileLength = (int)(36 + _dataBytesWritten);
            int byteRate = _sampleRate * _channels * (_bitsPerSample / 8);
            short blockAlign = (short)(_channels * (_bitsPerSample / 8));

            _writer.Write(new[] { 'R', 'I', 'F', 'F' });
            _writer.Write(fileLength);
            _writer.Write(new[] { 'W', 'A', 'V', 'E' });
            _writer.Write(new[] { 'f', 'm', 't', ' ' });
            _writer.Write(16); // format chunk size
            _writer.Write((short)1); // format: PCM
            _writer.Write(_channels);
            _writer.Write(_sampleRate);
            _writer.Write(byteRate);
            _writer.Write(blockAlign);
            _writer.Write(_bitsPerSample);
            _writer.Write(new[] { 'd', 'a', 't', 'a' });
            _writer.Write((int)_dataBytesWritten);

            _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_isFinalized)
            {
                try { FinalizeWav(); } catch { }
            }
            _writer.Dispose();
            _fs.Dispose();
        }
    }
}
