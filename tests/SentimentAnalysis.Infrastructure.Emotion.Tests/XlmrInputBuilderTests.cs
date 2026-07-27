using System.Collections.Generic;
using System.Linq;
using SentimentAnalysis.Infrastructure.Emotion;
using Xunit;

namespace SentimentAnalysis.Infrastructure.Emotion.Tests;

public class XlmrInputBuilderTests
{
    [Fact]
    public void Build_AddsBosOffsetEos()
    {
        // Ham SP id'leri → +1 offset, başa <s>=0, sona </s>=2.
        var input = XlmrInputBuilder.Build(new[] { 10, 20, 30 });
        Assert.Equal(new long[] { 0, 11, 21, 31, 2 }, input.InputIds);
        Assert.Equal(new long[] { 1, 1, 1, 1, 1 }, input.AttentionMask);
    }

    [Fact]
    public void Build_EmptyText_JustBosEos()
    {
        var input = XlmrInputBuilder.Build(System.Array.Empty<int>());
        Assert.Equal(new long[] { 0, 2 }, input.InputIds);
        Assert.Equal(new long[] { 1, 1 }, input.AttentionMask);
    }

    [Fact]
    public void Build_TruncatesToMaxTokens()
    {
        // 600 ham id + BOS + EOS → 512'ye kırpılmalı (510 ham + 2 özel).
        var raw = Enumerable.Range(5, 600).ToList();
        var input = XlmrInputBuilder.Build(raw, maxTokens: 512);
        Assert.Equal(512, input.InputIds.Length);
        Assert.Equal(0, input.InputIds[0]);                       // <s>
        Assert.Equal(2, input.InputIds[^1]);                      // </s>
        Assert.Equal(510, input.InputIds.Skip(1).SkipLast(1).Count());
        Assert.All(input.AttentionMask, m => Assert.Equal(1, m));
        Assert.Equal(512, input.AttentionMask.Length);
    }

    [Fact]
    public void Build_MaskMatchesIdsLength()
    {
        var input = XlmrInputBuilder.Build(new[] { 7, 8 });
        Assert.Equal(input.InputIds.Length, input.AttentionMask.Length);
    }
}
