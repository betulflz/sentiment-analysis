using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Stt;

/// <summary>Whisper ISO 639-1 dil kodunu dil-bağımsız <see cref="DetectedLanguage"/>'a eşler
/// (çok-dilli-hazır; master §D5). Bilinmeyen kod → Other.</summary>
public static class LanguageMap
{
    public static DetectedLanguage FromIso(string? code) => code?.ToLowerInvariant() switch
    {
        "tr" => DetectedLanguage.Turkish,
        "en" => DetectedLanguage.English,
        _ => DetectedLanguage.Other,
    };
}
