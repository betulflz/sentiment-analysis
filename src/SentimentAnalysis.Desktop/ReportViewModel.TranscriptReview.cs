using System;
using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

public sealed partial class ReportViewModel
{
    private IReadOnlyList<ReviewedTranscriptLine>? _reviewedTranscript;

    private void ClearTranscriptReview()
    {
        _reviewedTranscript = null;
        TranscriptReview.Clear();
    }

    private void ApplyReviewedTranscript(IReadOnlyList<ReviewedTranscriptLine> review)
    {
        _reviewedTranscript = review;
        DiarizationStatus = "Kaydedilmiş metin ve konuşmacı düzeltmeleri gösteriliyor.";
        var names = review.Select(l => l.Speaker.Trim()).Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var indexes = names.Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        var lines = review.Select(l => new SpeakerLine(
            indexes.TryGetValue(l.Speaker.Trim(), out var index) ? new SpeakerId(index) : SpeakerId.Unknown,
            l.Text, l.Start, l.End, l.Uncertain || string.IsNullOrWhiteSpace(l.Speaker))).ToArray();
        SpeakerLines = lines.Select(l => new SpeakerLineVm(l, l.Speaker.Index < 0 ? null : names[l.Speaker.Index])).ToArray();
        HasSpeakers = SpeakerLines.Count > 0;
        OnPropertyChanged(nameof(SpeakerLines));
        SetOpenEvents(OpenEventDetector.Detect(lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToArray())
            .Select(e => new OpenEventVm(Clock(e.At), OpenEventVm.TitleOf(e.Kind),
                e.Speaker is { Index: >= 0 } who ? names[who.Index] : string.Empty, e.Source, e.Suggestion)).ToArray());

        // Yeni kişi etiketleri eski ses/duygu ölçümleriyle eşdeğer değildir.
        // Düzeltme yapılınca kişi kartları yeniden analiz edilene kadar gösterilmez.
        SpeakerEmotions = Array.Empty<SpeakerEmotionVm>();
        TalkBalance = Array.Empty<TalkShareVm>();
        FaceMappings = Array.Empty<SpeakerFaceRowVm>();
        OnPropertyChanged(nameof(SpeakerEmotions));
        OnPropertyChanged(nameof(TalkBalance));
        OnPropertyChanged(nameof(FaceMappings));
        OnPropertyChanged(nameof(HasFaceMappings));
        OnPropertyChanged(nameof(FaceMappingHint));
    }
}
