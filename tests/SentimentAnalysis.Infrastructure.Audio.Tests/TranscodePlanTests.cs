using SentimentAnalysis.Infrastructure.Audio;

namespace SentimentAnalysis.Infrastructure.Audio.Tests;

/// <summary>
/// <see cref="TranscodePlan"/> saf olduğu için platform davranışı gerçek makineden bağımsız
/// doğrulanabilir: <c>isMacOs</c> parametre olarak verilir, çalıştığımız OS'a bakılmaz.
/// </summary>
public class TranscodePlanTests
{
    [Theory]
    [InlineData(".wav")]
    [InlineData(".WAV")]
    public void IsNative_WavIsNative(string ext) => Assert.True(TranscodePlan.IsNative(ext));

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".M4A")]
    [InlineData(".flac")]
    [InlineData(".ogg")]
    [InlineData(".mp4")]
    public void IsTranscodable_KnownFormats(string ext) => Assert.True(TranscodePlan.IsTranscodable(ext));

    [Theory]
    [InlineData(".txt")]
    [InlineData(".pdf")]
    [InlineData("")]
    public void UnknownExtension_IsNeitherNativeNorTranscodable(string ext)
    {
        Assert.False(TranscodePlan.IsNative(ext));
        Assert.False(TranscodePlan.IsTranscodable(ext));
    }

    [Fact]
    public void Wav_IsNotListedAsTranscodable()
    {
        // WAV doğrudan çözülür; dönüştürme listesine de girerse fabrika gereksiz yere
        // ffmpeg çağırır (ve ffmpeg yoksa çalışan bir senaryo bozulur).
        Assert.False(TranscodePlan.IsTranscodable(".wav"));
    }

    [Fact]
    public void For_MacOs_UsesAfconvertWithMonoSixteenKhz()
    {
        var cmd = TranscodePlan.For("/in/a.mp3", "/out/b.wav", isMacOs: true);

        Assert.Equal("afconvert", cmd.ToolDisplayName);
        Assert.Contains("/usr/bin/afconvert", cmd.ToolCandidates);
        Assert.Contains("LEI16@16000", cmd.Arguments);      // 16 kHz PCM16
        Assert.Equal("1", ArgAfter(cmd.Arguments, "-c"));   // mono
        Assert.Equal("/in/a.mp3", cmd.Arguments[^2]);
        Assert.Equal("/out/b.wav", cmd.Arguments[^1]);
    }

    [Fact]
    public void For_NonMacOs_UsesFfmpegWithMonoSixteenKhzPcm16()
    {
        var cmd = TranscodePlan.For(@"C:\in\a.mp3", @"C:\out\b.wav", isMacOs: false);

        Assert.Equal("ffmpeg", cmd.ToolDisplayName);
        Assert.Contains("ffmpeg", cmd.ToolCandidates);
        Assert.Equal("1", ArgAfter(cmd.Arguments, "-ac"));
        Assert.Equal("16000", ArgAfter(cmd.Arguments, "-ar"));
        Assert.Equal("pcm_s16le", ArgAfter(cmd.Arguments, "-acodec"));
        Assert.Equal(@"C:\in\a.mp3", ArgAfter(cmd.Arguments, "-i"));
        Assert.Equal(@"C:\out\b.wav", cmd.Arguments[^1]);
    }

    [Fact]
    public void For_Ffmpeg_IsNonInteractiveAndOverwrites()
    {
        // -nostdin olmadan ffmpeg terminali yutabilir; -y olmadan varolan geçici dosyada
        // "overwrite?" diye sorup asılı kalır.
        var cmd = TranscodePlan.For("a.mp3", "b.wav", isMacOs: false);
        Assert.Contains("-nostdin", cmd.Arguments);
        Assert.Contains("-y", cmd.Arguments);
    }

    [Fact]
    public void For_Ffmpeg_IgnoresVideoStream()
    {
        // mp4/mkv girdilerinde -vn olmadan ffmpeg video akışını da yazmaya çalışır ve WAV muxer hata verir.
        var cmd = TranscodePlan.For("a.mp4", "b.wav", isMacOs: false);
        Assert.Contains("-vn", cmd.Arguments);
    }

    [Fact]
    public void For_OutputIsAlwaysLastArgument()
    {
        foreach (var mac in new[] { true, false })
            Assert.Equal("out.wav", TranscodePlan.For("in.mp3", "out.wav", mac).Arguments[^1]);
    }

    [Fact]
    public void UnsupportedMessage_ListsSupportedFormats()
    {
        var msg = TranscodePlan.UnsupportedMessage(".xyz");
        Assert.Contains(".xyz", msg);
        Assert.Contains("wav", msg);
        Assert.Contains("mp3", msg);
    }

    private static string ArgAfter(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count - 1; i++)
            if (args[i] == flag) return args[i + 1];
        throw new Xunit.Sdk.XunitException($"'{flag}' argümanı bulunamadı: {string.Join(" ", args)}");
    }
}
