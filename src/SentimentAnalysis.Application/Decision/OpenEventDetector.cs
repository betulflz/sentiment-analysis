using System.Text.RegularExpressions;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>İt.4 açık olay türleri.</summary>
public enum OpenEventKind { CevapsizSoru, BelirsizSonrakiAdim, UzunTekKisi }

/// <summary>Kanıtlı açık olay: <paramref name="Source"/> olayı doğuran cümlenin kendisidir (öneri kaynağıyla birlikte gösterilir).</summary>
public sealed record OpenEvent(OpenEventKind Kind, TimeSpan At, SpeakerId? Speaker, string Source, string Suggestion);

/// <summary>Eşikler. BAŞLANGIÇ DEĞERLERİ, kalibre edilmedi (İt.4 kör insan değerlendirmesi ölçecek).</summary>
public sealed record OpenEventConfig(
    double AnswerWindowSeconds = 15,   // soru bitiminden sonra bu süre içinde başka biri konuşmazsa cevapsız
    double ClosingMinSeconds = 60,     // "son bölüm": en az son 60 sn …
    double ClosingShare = 0.2,         // … ya da sürenin son %20'si (hangisi uzunsa)
    double MonologueSeconds = 90,      // tek kişinin kesintisiz konuşması bu süreyi aşarsa olay
    double MonologueMaxGapSeconds = 5);

/// <summary>Konuşmacı etiketli satırlardan açık olayları bulur (İt.4: kanıta dayalı öneri). Saf, deterministik.
/// Kurallar sözcük düzeyindedir — anlam çıkarmaz; amacı kullanıcıya kaynağıyla birlikte "buna bak" demektir.</summary>
public static class OpenEventDetector
{
    // Whisper soru işaretini sık atlar: soru eki (mı/mi/mu/mü + kişi ekleri) ayrı sözcük olarak yeter.
    private static readonly Regex QuestionParticle = new(
        @"(?<!\p{L})m[ıiuü](?:s[ıiuü]n(?:[ıiuü]z)?|y[ıiuü]m|y[ıiuü]z|d[ıiuü]r)?(?!\p{L})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Somut sonraki adım işaretleri: zaman sözcükleri, gelecek zaman, takip eylemleri.
    private static readonly Regex NextStep = new(
        @"yarın|haftaya|gelecek hafta|önümüzdeki|pazartesi|salı|çarşamba|perşembe|cuma|cumartesi|pazar\b|" +
        @"ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık|saat \d|\d+\s*(gün|hafta|ay)\b|" +
        @"[ae]c[ae][kğ]|gönder|ararım|arayalım|görüşelim|toplantı|e-?posta|mail",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    public static IReadOnlyList<OpenEvent> Detect(IReadOnlyList<SpeakerLine> lines, OpenEventConfig? config = null)
    {
        var cfg = config ?? new OpenEventConfig();
        var ordered = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).OrderBy(l => l.Start).ToList();
        var events = new List<OpenEvent>();
        if (ordered.Count == 0) return events;

        UnansweredQuestions(ordered, cfg, events);
        Monologues(ordered, cfg, events);
        MissingNextStep(ordered, cfg, events);
        return events.OrderBy(e => e.At).ToList();
    }

    internal static bool IsQuestion(string text)
    {
        var t = text.Trim();
        if (t.EndsWith('?')) return true;
        // Soru işareti yoksa: soru eki cümlenin SON 3 sözcüğünde olmalı ve ardından "diye" (aktarma) gelmemeli.
        // Gerçek kayıtta (semsi-01) "… kelimeler mi bunlar diye … hatırlıyorum" yanlış "cevapsız soru" üretiyordu.
        var words = Regex.Split(t.ToLower(Tr), @"[^\p{L}\d]+").Where(w => w.Length > 0).ToArray();
        for (int i = words.Length - 1; i >= Math.Max(0, words.Length - 3); i--)
        {
            var m = QuestionParticle.Match(words[i]);
            if (m.Success && m.Length == words[i].Length)
                return !words.Skip(i + 1).Contains("diye");
        }
        return false;
    }

    private static void UnansweredQuestions(List<SpeakerLine> lines, OpenEventConfig cfg, List<OpenEvent> events)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var q = lines[i];
            if (q.Uncertain || !IsQuestion(q.Text)) continue;
            var deadline = q.End + TimeSpan.FromSeconds(cfg.AnswerWindowSeconds);
            // Konuşmacısı belirsiz satır da cevap sayılır: emin olmadan "cevapsız" denmez.
            bool answered = lines.Skip(i + 1).TakeWhile(l => l.Start <= deadline)
                .Any(l => l.Uncertain || l.Speaker != q.Speaker);
            if (answered) continue;
            var src = q.Text.Trim();
            events.Add(new OpenEvent(OpenEventKind.CevapsizSoru, q.Start, q.Speaker, src,
                $"Cevapsız kaldı: \"{src}\" — soruyu yeniden sorun ya da cevabı yazılı isteyin."));
        }
    }

    private static void Monologues(List<SpeakerLine> lines, OpenEventConfig cfg, List<OpenEvent> events)
    {
        int start = 0;
        for (int i = 1; i <= lines.Count; i++)
        {
            bool breaks = i == lines.Count
                || lines[i].Uncertain || lines[start].Uncertain
                || lines[i].Speaker != lines[start].Speaker
                || (lines[i].Start - lines[i - 1].End).TotalSeconds > cfg.MonologueMaxGapSeconds;
            if (!breaks) continue;
            var first = lines[start];
            double seconds = (lines[i - 1].End - first.Start).TotalSeconds;
            if (!first.Uncertain && seconds >= cfg.MonologueSeconds)
            {
                events.Add(new OpenEvent(OpenEventKind.UzunTekKisi, first.Start, first.Speaker, first.Text.Trim(),
                    $"Tek kişi {Math.Round(seconds)} sn kesintisiz konuştu — karşı tarafa söz verin, açık uçlu bir soru sorun."));
            }
            start = i;
        }
    }

    private static void MissingNextStep(List<SpeakerLine> lines, OpenEventConfig cfg, List<OpenEvent> events)
    {
        var begin = lines[0].Start;
        var end = lines.Max(l => l.End);
        double closing = Math.Max(cfg.ClosingMinSeconds, cfg.ClosingShare * (end - begin).TotalSeconds);
        var from = end - TimeSpan.FromSeconds(closing);
        var tail = lines.Where(l => l.End > from).ToList();
        if (tail.Any(l => NextStep.IsMatch(l.Text.ToLower(Tr)))) return;
        var last = lines[^1];
        events.Add(new OpenEvent(OpenEventKind.BelirsizSonrakiAdim, last.Start, last.Uncertain ? null : last.Speaker,
            last.Text.Trim(),
            "Görüşme net bir sonraki adım olmadan bitti — kim, neyi, ne zaman yapacak; yazılı olarak netleştirin."));
    }
}
