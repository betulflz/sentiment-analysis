using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

public sealed class CrossPlatformAudioConversionTests
{
    private sealed class FakePlatform : IRuntimePlatform
    {
        public bool IsWindows { get; set; }
        public bool IsLinux { get; set; }
        public bool IsMacOS { get; set; }
    }

    private sealed class FakeProcessRunner : IExternalProcessRunner
    {
        public int CallCount { get; private set; }
        public string? LastFileName { get; private set; }
        public List<string> LastArguments { get; } = new();
        public CancellationToken LastCancellationToken { get; private set; }

        public Func<string, IEnumerable<string>, CancellationToken, Task<ProcessResult>>? Handler { get; set; }

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct)
        {
            CallCount++;
            LastFileName = fileName;
            LastArguments.Clear();
            LastArguments.AddRange(arguments);
            LastCancellationToken = ct;

            if (Handler != null)
            {
                return Handler(fileName, arguments, ct);
            }
            return Task.FromResult(new ProcessResult(0, "", ""));
        }
    }

    [Fact]
    public void Windows_ShouldNotSelectAfconvert_ShouldSelectFfmpeg()
    {
        var platform = new FakePlatform { IsWindows = true };
        var resolver = new AudioToolResolver(platform);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve());
            Assert.Contains("FFmpeg bulunamadı", ex.Message);
            Assert.DoesNotContain("afconvert", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void Linux_ShouldNotSelectAfconvert_ShouldSelectFfmpeg()
    {
        var platform = new FakePlatform { IsLinux = true };
        var resolver = new AudioToolResolver(platform);

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve());
            Assert.Contains("FFmpeg bulunamadı", ex.Message);
            Assert.DoesNotContain("afconvert", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void UnsupportedPlatform_ShouldThrowMeaningfulException()
    {
        var platform = new FakePlatform(); // None are true
        var resolver = new AudioToolResolver(platform);

        var ex = Assert.Throws<PlatformNotSupportedException>(() => resolver.Resolve());
        Assert.Contains("Bu işletim sistemi için ses dönüştürme desteği bulunmuyor", ex.Message);
    }

    [Fact]
    public void SentimentFfmpegPath_WhenValid_ShouldBeSelected()
    {
        var platform = new FakePlatform { IsWindows = true };
        var resolver = new AudioToolResolver(platform);

        // Create a temporary dummy file to point our env var to
        var tempFile = Path.Combine(Path.GetTempPath(), "dummy-ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "");

        var originalEnv = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        try
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", tempFile);

            var resolved = resolver.Resolve();
            Assert.Equal(tempFile, resolved.ExecutablePath);
            Assert.Equal(AudioConversionToolKind.Ffmpeg, resolved.Kind);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", originalEnv);
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void SentimentFfmpegPath_WhenInvalid_ShouldThrowExplicitError()
    {
        var platform = new FakePlatform { IsWindows = true };
        var resolver = new AudioToolResolver(platform);

        var originalEnv = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        try
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", "C:\\nonexistent\\ffmpeg.exe");

            var ex = Assert.Throws<FileNotFoundException>(() => resolver.Resolve());
            Assert.Contains("SENTIMENT_FFMPEG_PATH ortam değişkeninde belirtilen dosya bulunamadı", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", originalEnv);
        }
    }

    [Fact]
    public async Task CrossPlatformAudioConverter_ShouldGenerateCorrectFfmpegArguments()
    {
        var platform = new FakePlatform { IsWindows = true };
        var tempFile = Path.Combine(Path.GetTempPath(), "dummy-ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "");

        var originalEnv = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", tempFile);

        try
        {
            var resolver = new AudioToolResolver(platform);
            var runner = new FakeProcessRunner();
            
            // Output simulation
            var dummyOutputWav = Path.Combine(Path.GetTempPath(), "dummy-out-" + Guid.NewGuid().ToString("N") + ".wav");
            runner.Handler = (file, args, ct) =>
            {
                // Write valid WAV format to satisfy verification
                var dummyBytes = WriteWav(1, 16, 16000, 1, new byte[2]);
                File.WriteAllBytes(dummyOutputWav, dummyBytes);
                return Task.FromResult(new ProcessResult(0, "", ""));
            };

            var converter = new CrossPlatformAudioConverter(resolver, runner);
            var dummyInput = Path.Combine(Path.GetTempPath(), "dummy-in-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.WriteAllText(dummyInput, "some mp3 contents");

            try
            {
                await converter.ConvertAsync(dummyInput, dummyOutputWav, CancellationToken.None);

                Assert.Equal(tempFile, runner.LastFileName);
                var args = runner.LastArguments;
                Assert.Contains("-y", args);
                Assert.Contains("-hide_banner", args);
                Assert.Contains("-loglevel", args);
                Assert.Contains("error", args);
                Assert.Contains("-vn", args);
                Assert.Contains("-ac", args);
                Assert.Contains("1", args);
                Assert.Contains("-ar", args);
                Assert.Contains("16000", args);
                Assert.Contains("-c:a", args);
                Assert.Contains("pcm_s16le", args);
                Assert.Equal(dummyInput, args[args.IndexOf("-i") + 1]);
                Assert.Equal(dummyOutputWav, args.Last());
            }
            finally
            {
                try { File.Delete(dummyInput); } catch { }
                try { File.Delete(dummyOutputWav); } catch { }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", originalEnv);
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task ConversionFailure_ShouldCleanUpPartialOutput_AndThrowMeaningfulError()
    {
        var platform = new FakePlatform { IsWindows = true };
        var tempFile = Path.Combine(Path.GetTempPath(), "dummy-ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "");

        var originalEnv = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", tempFile);

        try
        {
            var resolver = new AudioToolResolver(platform);
            var runner = new FakeProcessRunner();
            var dummyOutputWav = Path.Combine(Path.GetTempPath(), "dummy-out-" + Guid.NewGuid().ToString("N") + ".wav");
            
            runner.Handler = (file, args, ct) =>
            {
                // Create a partial/corrupted file
                File.WriteAllText(dummyOutputWav, "partial data");
                return Task.FromResult(new ProcessResult(1, "Specific Ffmpeg Error message", ""));
            };

            var converter = new CrossPlatformAudioConverter(resolver, runner);
            var dummyInput = Path.Combine(Path.GetTempPath(), "dummy-in-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.WriteAllText(dummyInput, "some mp3 contents");

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => converter.ConvertAsync(dummyInput, dummyOutputWav, CancellationToken.None));
                
                Assert.Contains("Ses dönüştürme işlemi başarısız oldu", ex.Message);
                Assert.Contains("Specific Ffmpeg Error message", ex.Message);
                
                // Assert partial file was deleted
                Assert.False(File.Exists(dummyOutputWav));
                // Assert original input file is intact
                Assert.True(File.Exists(dummyInput));
            }
            finally
            {
                try { File.Delete(dummyInput); } catch { }
                try { File.Delete(dummyOutputWav); } catch { }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", originalEnv);
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task EmptyOutput_ShouldThrowError_AndCleanUpFile()
    {
        var platform = new FakePlatform { IsWindows = true };
        var tempFile = Path.Combine(Path.GetTempPath(), "dummy-ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "");

        var originalEnv = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", tempFile);

        try
        {
            var resolver = new AudioToolResolver(platform);
            var runner = new FakeProcessRunner();
            var dummyOutputWav = Path.Combine(Path.GetTempPath(), "dummy-out-" + Guid.NewGuid().ToString("N") + ".wav");
            
            runner.Handler = (file, args, ct) =>
            {
                // Write empty output file
                File.WriteAllBytes(dummyOutputWav, Array.Empty<byte>());
                return Task.FromResult(new ProcessResult(0, "", ""));
            };

            var converter = new CrossPlatformAudioConverter(resolver, runner);
            var dummyInput = Path.Combine(Path.GetTempPath(), "dummy-in-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.WriteAllText(dummyInput, "some mp3 contents");

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => converter.ConvertAsync(dummyInput, dummyOutputWav, CancellationToken.None));
                
                Assert.Contains("geçerli bir başlık içermek için çok küçük", ex.Message);
                Assert.False(File.Exists(dummyOutputWav));
            }
            finally
            {
                try { File.Delete(dummyInput); } catch { }
                try { File.Delete(dummyOutputWav); } catch { }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", originalEnv);
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task InvalidWavHeader_ShouldThrowError_AndCleanUpFile()
    {
        var platform = new FakePlatform { IsWindows = true };
        var tempFile = Path.Combine(Path.GetTempPath(), "dummy-ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "");

        var originalEnv = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", tempFile);

        try
        {
            var resolver = new AudioToolResolver(platform);
            var runner = new FakeProcessRunner();
            var dummyOutputWav = Path.Combine(Path.GetTempPath(), "dummy-out-" + Guid.NewGuid().ToString("N") + ".wav");
            
            runner.Handler = (file, args, ct) =>
            {
                // Write non-WAV bytes (44 bytes) to pass minimum header length check and fail RIFF signature
                var invalidBytes = new byte[44];
                invalidBytes[0] = 0x99;
                File.WriteAllBytes(dummyOutputWav, invalidBytes);
                return Task.FromResult(new ProcessResult(0, "", ""));
            };

            var converter = new CrossPlatformAudioConverter(resolver, runner);
            var dummyInput = Path.Combine(Path.GetTempPath(), "dummy-in-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.WriteAllText(dummyInput, "some mp3 contents");

            try
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => converter.ConvertAsync(dummyInput, dummyOutputWav, CancellationToken.None));
                
                Assert.Contains("RIFF' imzası bulunamadı", ex.Message);
                Assert.False(File.Exists(dummyOutputWav));
            }
            finally
            {
                try { File.Delete(dummyInput); } catch { }
                try { File.Delete(dummyOutputWav); } catch { }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", originalEnv);
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public async Task ConvertingCaptureSource_ShouldConvertOnlyOnce_AndCleanUpTempWavOnFinish()
    {
        var platform = new FakePlatform { IsWindows = true };
        var tempFile = Path.Combine(Path.GetTempPath(), "dummy-ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(tempFile, "");

        var originalEnv = Environment.GetEnvironmentVariable("SENTIMENT_FFMPEG_PATH");
        Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", tempFile);

        try
        {
            var resolver = new AudioToolResolver(platform);
            var runner = new FakeProcessRunner();
            
            string? capturedTempWavPath = null;
            runner.Handler = (file, args, ct) =>
            {
                // Retrieve output path from arguments list
                var outPath = args.Last();
                capturedTempWavPath = outPath;

                // Write a valid WAV format with fmt and data chunks
                var dummyBytes = WriteWav(1, 16, 16000, 1, new byte[32000]); // 1 second of silent audio
                File.WriteAllBytes(outPath, dummyBytes);
                return Task.FromResult(new ProcessResult(0, "", ""));
            };

            var converter = new CrossPlatformAudioConverter(resolver, runner);
            var dummyInput = Path.Combine(Path.GetTempPath(), "dummy-in-" + Guid.NewGuid().ToString("N") + ".mp3");
            File.WriteAllText(dummyInput, "some mp3 contents");

            try
            {
                var source = new ConvertingCaptureSource(dummyInput, converter);
                
                // First capture run
                var options = new CaptureOptions(TimeSpan.FromSeconds(1), TimeSpan.Zero);
                var list = new List<AudioChunk>();
                await foreach (var chunk in source.CaptureAsync(options, CancellationToken.None))
                {
                    list.Add(chunk);
                }

                // Verify converter was called exactly once
                Assert.Equal(1, runner.CallCount);
                Assert.NotNull(capturedTempWavPath);
                
                // Assert the temp file is cleaned up after finishing
                Assert.False(File.Exists(capturedTempWavPath));

                // Trying to run CaptureAsync a second time on the same instance should throw InvalidOperationException
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    await foreach (var _ in source.CaptureAsync(options, CancellationToken.None)) { }
                });
            }
            finally
            {
                try { File.Delete(dummyInput); } catch { }
                if (capturedTempWavPath != null)
                {
                    try { File.Delete(capturedTempWavPath); } catch { }
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SENTIMENT_FFMPEG_PATH", originalEnv);
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void WavRecorder_ShouldWriteCorrectHeaders_AndBlockWriteAfterFinalize()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "test-rec-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var recorder = new WavRecorder(tempPath, 16000, 1, 16))
            {
                var samples = new float[16000]; // 1 second of silent audio
                recorder.WriteSamples(samples, 16000, 1);
                recorder.FinalizeWav();

                // Assert second finalize does not throw or corrupt
                recorder.FinalizeWav();

                // Assert writing after finalize throws
                Assert.Throws<InvalidOperationException>(() => recorder.WriteSamples(samples, 16000, 1));
            }

            // Assert validation succeeds
            WavValidator.ValidateWav(tempPath);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [Fact]
    public void WavRecorder_ShouldValidateInputFormat()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "test-format-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using var recorder = new WavRecorder(tempPath, 16000, 1, 16);
            var samples = new float[100];
            
            // Wrong sample rate
            Assert.Throws<ArgumentException>(() => recorder.WriteSamples(samples, 44100, 1));
            
            // Wrong channels
            Assert.Throws<ArgumentException>(() => recorder.WriteSamples(samples, 16000, 2));
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private class FakeAudioCaptureSource : IAudioCaptureSource
    {
        public bool IsCapturing { get; set; }
        public List<float[]> YieldedSamples { get; } = new();

        public async IAsyncEnumerable<AudioChunk> CaptureAsync(CaptureOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            IsCapturing = true;
            try
            {
                foreach (var samples in YieldedSamples)
                {
                    if (ct.IsCancellationRequested) yield break;
                    yield return new AudioChunk(samples, 16000, TimeSpan.Zero, TimeSpan.FromSeconds(1));
                    await Task.Yield();
                }
            }
            finally
            {
                IsCapturing = false;
            }
        }
    }

    [Fact]
    public async Task LiveRecordingSession_LifecycleAndCleanup()
    {
        var micMock = new FakeAudioCaptureSource();
        micMock.YieldedSamples.Add(new float[16000]); // 1 second of data
        
        var session = new LiveRecordingSession(micMock);
        
        Assert.False(session.IsRecording);
        
        using var cts = new CancellationTokenSource();
        await session.StartAsync(cts.Token);
        
        Assert.True(session.IsRecording);
        
        // Wait a brief moment for the background loop
        await Task.Delay(100);
        
        var result = await session.StopAsync(CancellationToken.None);
        
        Assert.False(session.IsRecording);
        Assert.True(File.Exists(result.WavPath));
        Assert.Equal(16000, result.SampleCount);
        
        // Validate WAV file
        WavValidator.ValidateWav(result.WavPath);
        
        // Clean up (caller is responsible for deleting WAV file after StopAsync success)
        await session.DisposeAsync();
        if (File.Exists(result.WavPath))
        {
            File.Delete(result.WavPath);
        }
        Assert.False(File.Exists(result.WavPath));
    }


    private static byte[] WriteWav(short audioFormat, short bitsPerSample, int sampleRate, int channels, byte[] data)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + data.Length);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write(audioFormat);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(data.Length);
        w.Write(data);
        w.Flush();
        return ms.ToArray();
    }
}
