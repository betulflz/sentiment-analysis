namespace SentimentAnalysis.Infrastructure.Audio;

public enum AudioConversionToolKind
{
    Afconvert,
    Ffmpeg
}

public sealed record ResolvedAudioTool(
    string ExecutablePath,
    AudioConversionToolKind Kind);

public interface IAudioToolResolver
{
    ResolvedAudioTool Resolve();
}
