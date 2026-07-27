using Microsoft.ML.Tokenizers;

namespace SentimentAnalysis.Infrastructure.Emotion;

/// <summary>XLM-R SentencePiece tokenizer sarmalayıcısı (spec §04b reçetesi). BOS/EOS'u SentencePiece
/// seviyesinde KAPATIR; XLM-R özel token'larını (<s>/</s>) + fairseq offset'i <see cref="XlmrInputBuilder"/>
/// deterministik ekler. Tokenizer bir kez yüklenir (motor tarafından singleton tutulur).</summary>
public sealed class XlmrTokenizerAdapter : IDisposable
{
    private readonly SentencePieceTokenizer _tokenizer;
    private bool _disposed;

    /// <param name="spmStream">XLM-R-base `sentencepiece.bpe.model` içeriği.</param>
    public XlmrTokenizerAdapter(Stream spmStream)
    {
        // BOS/EOS kapalı → EncodeToIds ham SentencePiece piece id'leri verir; özel token'ları biz ekleriz.
        // NOT: Microsoft.ML.Tokenizers 2.0.0 gerçek parametre adı `addBeginningOfSentence`
        // (brief'teki `addBeginOfSentence` derlenmedi — CS1739; XML doc ile teyit edildi).
        _tokenizer = SentencePieceTokenizer.Create(
            spmStream, addBeginningOfSentence: false, addEndOfSentence: false);
    }

    public XlmrInput Encode(string text, int maxTokens = 512)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var raw = _tokenizer.EncodeToIds(text ?? string.Empty);
        return XlmrInputBuilder.Build(raw, maxTokens);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        (_tokenizer as IDisposable)?.Dispose();   // sürüme göre IDisposable olabilir/olmayabilir
    }
}
