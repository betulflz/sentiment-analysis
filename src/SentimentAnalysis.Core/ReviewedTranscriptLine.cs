namespace SentimentAnalysis.Core;

/// <summary>Kullanıcının rapor için düzenlediği satır; otomatik model çıktısının yerine yazılmaz.</summary>
public sealed record ReviewedTranscriptLine(
    TimeSpan Start, TimeSpan End, string Text, string Speaker, bool Uncertain = false);
