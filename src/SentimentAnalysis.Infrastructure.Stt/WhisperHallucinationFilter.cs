using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SentimentAnalysis.Infrastructure.Stt;

/// <summary>Whisper'ın sessiz/gürültülü seste eğitim verisindeki altyazı ve video kapanışlarından
/// uydurduğu Türkçe kalıplar (ölçüm: whisper-beam-01, 30 sn sessizlik → "Altyazı M.K.").
/// Yalnızca segmentin TAMAMI kalıptan ibaretse eşleşir; gerçek cümle içinde geçen sözcükler korunur.</summary>
public static partial class WhisperHallucinationFilter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // Normalleştirilmiş metinde (küçük harf, noktalama → boşluk) tam eşleşme.
    [GeneratedRegex(
        @"^(?:" +
        @"alt ?yazı(?: \p{L}){0,3}" +            // Altyazı M.K. — yalnız tek harfli baş harfler ("Altyazı var mı" korunur)
        @"|izlediğiniz için teşekkür(?:ler| ederim| ederiz)" +
        @"|(?:(?:kanalıma |kanalımıza )?(?:videoyu )?beğenmeyi ve )?(?:kanalıma |kanalımıza )?abone olmayı unutmayın" +
        @"|bir sonraki videoda görüşmek üzere" +
        @")$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsHallucination(string text)
    {
        var normalized = Normalize(text);
        return normalized.Length > 0 && Pattern().IsMatch(normalized);
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = true;
        foreach (var ch in text.ToLower(Tr))
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); space = false; }
            else if (!space) { sb.Append(' '); space = true; }
        }
        return sb.ToString().TrimEnd();
    }
}
