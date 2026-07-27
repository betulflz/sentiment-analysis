namespace SentimentAnalysis.Core;

/// <summary>Nihai sonucun güven düzeyi.</summary>
public enum Confidence { Yuksek, Orta, Dusuk }

/// <summary>Söz-ton uyumsuzluğunun örüntüsü.</summary>
public enum IncongruencePattern { Yok, Maskeleme, Saka, OlasiIroni }

/// <summary>Algılanan konuşma dili (çok-dilli-hazır).</summary>
public enum DetectedLanguage { Turkish, English, Other }

/// <summary>Ham ses parçası: 16 kHz mono float PCM.</summary>
public sealed record AudioChunk(float[] Samples, int SampleRate, TimeSpan Start, TimeSpan End);

/// <summary>STT çıktısı.</summary>
public sealed record Transcript(string Text, DetectedLanguage Language, double AvgLogProb);

/// <summary>Metin modelinin ham çıktısı.</summary>
public sealed record TextEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Prozodi modelinin ham çıktısı.</summary>
public sealed record ProsodyEmotion(ValenceArousal Va, double ModelConfidence);

/// <summary>Füzyon ağırlığını besleyen girdi kalite sinyalleri.</summary>
public sealed record QualitySignals(
    double AudioSnr,
    double SpeechRatio,
    TimeSpan Duration,
    int TranscriptWordCount,
    double TranscriptAvgLogProb);
