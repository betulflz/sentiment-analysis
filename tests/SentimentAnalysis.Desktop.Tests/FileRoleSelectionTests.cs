using System;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class FileRoleSelectionTests
{
    [Fact]
    public void Kirpilmis_Dosya_SinirSonrasiRolsuz()
    {
        var A = new SpeakerId(0);
        var prepared = new SpeakerPreparationResult(DiarizationOutcome.Completed,
            new[] { new SpeakerSegment(A, TimeSpan.Zero, TimeSpan.FromSeconds(10)) }, Array.Empty<SpeakerChoice>(),
            new float[16000 * 10], Truncated: true);
        var sel = new FileRoleSelection();
        sel.Set(prepared, A);
        var past = sel.Roles!.Label(new AudioChunk(new float[16000], 16000, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11)));
        Assert.False(past.Enrolled);
    }

    private static SpeakerPreparationResult Prep() => new(DiarizationOutcome.Completed,
        new[] { new SpeakerSegment(new SpeakerId(0), TimeSpan.Zero, TimeSpan.FromSeconds(1)) }, Array.Empty<SpeakerChoice>(),
        new float[16000], Truncated: false);

    [Fact]
    public void ClearIf_YeniSecimiSilmez()
    {
        // Eski dosyanın arka plan geçişi bitip temizlerken kullanıcı yeni dosya için seçim yapmış olabilir.
        var sel = new FileRoleSelection();
        var old = Prep();
        var fresh = Prep();
        sel.Set(old, new SpeakerId(0));
        sel.Set(fresh, new SpeakerId(0));

        sel.ClearIf(old);

        Assert.Same(fresh, sel.Prepared);
    }

    [Fact]
    public void ClearIf_AyniSecimiTemizler()
    {
        var sel = new FileRoleSelection();
        var p = Prep();
        sel.Set(p, new SpeakerId(0));
        sel.ClearIf(p);
        Assert.Null(sel.Prepared);
        Assert.Null(sel.Roles);
    }
}
