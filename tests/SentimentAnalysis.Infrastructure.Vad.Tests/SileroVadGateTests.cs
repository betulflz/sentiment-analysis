using System;
using System.IO;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;
using SentimentAnalysis.Infrastructure.Vad;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Vad.Tests;

public class SileroVadGateTests
{
    private sealed class FixedModelProvider(string path) : IVadModelProvider
    {
        public string ResolveModelPath(string fileName) => path;
    }

    private sealed class UnusedModelProvider : IVadModelProvider
    {
        public string ResolveModelPath(string fileName) =>
            throw new InvalidOperationException("dispose sonrası/guard öncesi model çözülmemeliydi");
    }

    [Fact]
    public void HasSpeech_Throws_ForNon16kHz()
    {
        using var gate = new SileroVadGate(AppConfig.Default, new UnusedModelProvider());
        var chunk = new AudioChunk(new float[8000], SampleRate: 8000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.Throws<ArgumentException>(() => gate.HasSpeech(chunk));
    }

    [Fact]
    public void HasSpeech_AfterDispose_Throws()
    {
        var gate = new SileroVadGate(AppConfig.Default, new UnusedModelProvider());
        gate.Dispose();
        var chunk = new AudioChunk(new float[16000], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.Throws<ObjectDisposedException>(() => gate.HasSpeech(chunk));
    }

    [SkippableFact]
    public void HasSpeech_RealModel_SilenceIsNotSpeech()
    {
        var modelPath = Environment.GetEnvironmentVariable("VAD_MODEL_PATH");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "VAD_MODEL_PATH ayarlı değil; gerçek-model testi atlandı.");

        using var gate = new SileroVadGate(AppConfig.Default, new FixedModelProvider(modelPath!));
        var silence = new AudioChunk(new float[16000], 16000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.False(gate.HasSpeech(silence)); // spike: sessizlik prob≈0.0006 < 0.5
    }

    [SkippableFact]
    public void HasSpeech_RealModel_SpeechWavIsSpeech()
    {
        var modelPath = Environment.GetEnvironmentVariable("VAD_MODEL_PATH");
        var wavPath = Environment.GetEnvironmentVariable("VAD_SAMPLE_SPEECH_WAV");
        Skip.If(string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath),
            "VAD_MODEL_PATH ayarlı değil; atlandı.");
        Skip.If(string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath),
            "VAD_SAMPLE_SPEECH_WAV ayarlı değil; konuşma örneği yok.");

        // WAV'ı test içinde okumamak için: 16k mono PCM16 bekleniyor; basit okuyucu.
        var samples = ReadWav16kMono(wavPath!);
        using var gate = new SileroVadGate(AppConfig.Default, new FixedModelProvider(modelPath!));
        var chunk = new AudioChunk(samples, 16000, TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16000.0));
        Assert.True(gate.HasSpeech(chunk), "konuşma içeren örnekte HasSpeech true beklenir");
    }

    private static float[] ReadWav16kMono(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        r.ReadBytes(12);
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            var id = new string(r.ReadChars(4));
            int size = r.ReadInt32();
            if (id == "data")
            {
                int n = size / 2;
                var s = new float[n];
                for (int i = 0; i < n; i++) s[i] = r.ReadInt16() / 32768f;
                return s;
            }
            r.BaseStream.Seek(size, SeekOrigin.Current);
        }
        throw new InvalidDataException("data chunk yok");
    }
}
