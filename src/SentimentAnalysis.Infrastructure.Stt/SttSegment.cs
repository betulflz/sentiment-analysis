namespace SentimentAnalysis.Infrastructure.Stt;

/// <summary>Whisper.net <c>SegmentData</c> ile saf <see cref="TranscriptAssembler"/> arasındaki
/// sınır DTO'su — native tipleri motor sınırında tutar, birleştirme mantığını test edilebilir kılar.</summary>
public sealed record SttSegment(string Text, TimeSpan Start, TimeSpan End, double Probability, string LanguageCode);
