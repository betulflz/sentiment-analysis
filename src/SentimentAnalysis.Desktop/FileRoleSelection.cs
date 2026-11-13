using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Dosya modunda "Siz hangisiniz?" seçiminin sonucu (karşı tarafa odak). Analiz koşucusu
/// <see cref="Roles"/>'u, oturum sonrası konuşmacı geçişi <see cref="Prepared"/>'ı okur. Paylaşılan tekil durum;
/// dosya/mod değişince ve oturum sonrası geçiş bitince temizlenir (bayat rol kullanılmasın).</summary>
public interface IFileRoleSelection
{
    IRoleLabeler? Roles { get; }
    SpeakerPreparationResult? Prepared { get; }
    SpeakerId? Host { get; }
    void Set(SpeakerPreparationResult prepared, SpeakerId host);
    void SetMixed();
    void Clear();
}

public sealed class FileRoleSelection : IFileRoleSelection
{
    private readonly object _gate = new();
    private (IRoleLabeler? Roles, SpeakerPreparationResult? Prepared, SpeakerId? Host) _state;

    public IRoleLabeler? Roles { get { lock (_gate) return _state.Roles; } }
    public SpeakerPreparationResult? Prepared { get { lock (_gate) return _state.Prepared; } }
    public SpeakerId? Host { get { lock (_gate) return _state.Host; } }

    public void Set(SpeakerPreparationResult prepared, SpeakerId host)
    {
        TimeSpan? covered = prepared.Truncated ? TimeSpan.FromSeconds(prepared.Audio.Length / 16000.0) : null;
        lock (_gate) _state = (new DiarizationRoleLabeler(prepared.Segments, host, covered), prepared, host);
    }

    public void SetMixed() { lock (_gate) _state = default; }

    public void Clear() { lock (_gate) _state = default; }
}
