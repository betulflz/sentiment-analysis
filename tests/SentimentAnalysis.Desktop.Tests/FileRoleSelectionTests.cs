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
}
