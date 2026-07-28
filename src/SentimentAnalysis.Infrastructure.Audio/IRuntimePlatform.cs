namespace SentimentAnalysis.Infrastructure.Audio;

public interface IRuntimePlatform
{
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }
}
