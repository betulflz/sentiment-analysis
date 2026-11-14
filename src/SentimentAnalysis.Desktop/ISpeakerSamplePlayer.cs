namespace SentimentAnalysis.Desktop;

/// <summary>"Siz hangisiniz?" panelinde bir konuşmacının kısa ses örneğini çalar (karşı tarafa odak).</summary>
public interface ISpeakerSamplePlayer
{
    /// <summary>Bu platformda çalınabilir mi? Değilse "Dinle" düğmesi gizlenir.</summary>
    bool IsSupported { get; }

    /// <summary>16 kHz mono örneği çalar; yeni çağrı öncekini durdurur. İptalde/bitişte geçici dosya silinir.</summary>
    Task PlayAsync(float[] mono16k, CancellationToken ct);
}
