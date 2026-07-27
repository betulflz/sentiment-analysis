namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>Tek örnek için ONNX girdisi.</summary>
public readonly record struct XlmrInput(long[] InputIds, long[] AttentionMask);

/// <summary>Ham SentencePiece piece id'lerini XLM-R `input_ids`/`attention_mask`'ine çevirir
/// (spec §04b, spike ile doğrulandı): +1 fairseq offset, başa &lt;s&gt;=0, sona &lt;/s&gt;=2,
/// pozisyonel sınır için ham id'ler maxTokens-2'ye kırpılır. attention_mask hepsi 1 (padding yok). Saf.</summary>
public static class XlmrInputBuilder
{
    public const int BosId = 0;         // XLM-R <s>
    public const int EosId = 2;         // XLM-R </s>
    public const int FairseqOffset = 1; // sentencepiece → fairseq vocab kaydırması

    public static XlmrInput Build(IReadOnlyList<int> rawPieceIds, int maxTokens = 512)
    {
        int maxRaw = System.Math.Max(0, maxTokens - 2);       // BOS + EOS payı
        int count = System.Math.Min(rawPieceIds.Count, maxRaw);

        var ids = new long[count + 2];
        ids[0] = BosId;
        for (int i = 0; i < count; i++) ids[i + 1] = rawPieceIds[i] + FairseqOffset;
        ids[^1] = EosId;

        var mask = new long[ids.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = 1;    // tek örnek, padding yok

        return new XlmrInput(ids, mask);
    }
}
