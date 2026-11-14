using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace SentimentAnalysis.Application.Tests;

/// <summary>
/// Üretim kaynaklarında <c>Console.Write*</c> bulunmamalıdır. <b>Neden bir test:</b> bu kural
/// gözle korunamaz — bir sonraki hata ayıklama turunda tekrar eklenir ve fark edilmez.
/// <list type="bullet">
/// <item>Paketlenmiş uygulamada (macOS <c>.app</c>, Windows GUI) bağlı bir konsol yoktur:
/// çıktı hiçbir yere gitmez, yani maliyet ödenir fayda alınmaz.</item>
/// <item>Windows'ta konsolsuz bir süreçte <c>Console</c> yazımı sessizce yutulur; platformlar
/// arasında farklı davranır, dolayısıyla güvenilir bir teşhis aracı değildir.</item>
/// <item>En önemlisi: PortAudio geri çağrımı gibi gerçek zamanlı yollarda metin biçimleme +
/// I/O yapmak ses kopmasına yol açar — teşhis aracı, teşhis ettiği sorunu üretir.</item>
/// </list>
/// Teşhis çıktısı yerine <c>IDiagnosticLog</c> kullanılır: üretimde sessiz, gerektiğinde açılır.
/// </summary>
public class NoConsoleLoggingInProductionTests
{
    [Fact]
    public void UretimKaynaklarinda_KonsolGunluguYok()
    {
        var srcRoot = FindSourceRoot();
        Assert.True(srcRoot is not null,
            "kaynak ağacı bulunamadı; bu test depo içinden çalıştırılmalıdır");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGenerated(file)) continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("Console.Write", StringComparison.Ordinal)) continue;
                offenders.Add($"{Path.GetRelativePath(srcRoot, file)}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Üretim kodunda konsol günlüğü bulundu; bunun yerine IDiagnosticLog kullanın:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>Bu test dosyasından yukarı yürüyerek çözüm kökünü, oradan <c>src</c>'yi bulur.</summary>
    private static string? FindSourceRoot([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SentimentAnalysis.sln")))
            {
                var src = Path.Combine(dir, "src");
                return Directory.Exists(src) ? src : null;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>Derleme çıktıları ve üretilmiş dosyalar kuralın kapsamı dışındadır.</summary>
    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.EndsWith(".g.cs", StringComparison.Ordinal)
        || path.EndsWith(".Designer.cs", StringComparison.Ordinal);
}
