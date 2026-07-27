using System;
using System.IO;
using System.Linq;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class XlmrTokenizerAdapterTests
{
    // Gerçek SentencePiece modeli (~5 MB) olmadan tokenize edilemez → env-gated.
    // XLMR_SPM_PATH = XLM-R-base sentencepiece.bpe.model dosyası.
    [SkippableFact]
    public void Encode_ProducesBosEosWrappedIds()
    {
        var spm = Environment.GetEnvironmentVariable("XLMR_SPM_PATH");
        Skip.If(string.IsNullOrWhiteSpace(spm) || !File.Exists(spm),
            "XLMR_SPM_PATH ayarlı değil; gerçek-tokenizer testi atlandı.");

        using var stream = File.OpenRead(spm!);
        using var adapter = new XlmrTokenizerAdapter(stream);
        var input = adapter.Encode("Bugün çok mutluyum.");

        Assert.Equal(0, input.InputIds[0]);                   // <s>
        Assert.Equal(2, input.InputIds[^1]);                  // </s>
        Assert.True(input.InputIds.Length > 2, "en az bir gerçek token beklenir");
        Assert.Equal(input.InputIds.Length, input.AttentionMask.Length);
        Assert.All(input.AttentionMask, m => Assert.Equal(1, m));
        // Spike referansı: XLM-R iç token'ları > 3 (özel token bölgesi 0..3'ün ötesinde).
        Assert.All(input.InputIds.Skip(1).SkipLast(1), id => Assert.True(id > 3, $"beklenmedik özel token {id}"));
    }
}
