using System;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed class RuntimePlatform : IRuntimePlatform
{
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsMacOS => OperatingSystem.IsMacOS();
}
