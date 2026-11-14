using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SentimentAnalysis.Application.Diarization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

public sealed partial class TranscriptReviewRowVm : ObservableObject
{
    public ReviewedTranscriptLine Original { get; }
    public string TimeRange => $"{Original.Start:hh\\:mm\\:ss}–{Original.End:hh\\:mm\\:ss}";
    [ObservableProperty] private string _text;
    [ObservableProperty] private string _speaker;
    public bool Uncertain => string.IsNullOrWhiteSpace(Speaker)
        || (Speaker.Trim() == Original.Speaker && Original.Uncertain);
    partial void OnSpeakerChanged(string value) => OnPropertyChanged(nameof(Uncertain));
    public ReviewedTranscriptLine Snapshot() => Original with
    { Text = Text ?? string.Empty, Speaker = Speaker?.Trim() ?? string.Empty, Uncertain = Uncertain };
    public TranscriptReviewRowVm(ReviewedTranscriptLine line)
    { Original = line; _text = line.Text; _speaker = line.Speaker; }
}

public sealed partial class TranscriptReviewViewModel : ObservableObject
{
    private readonly ITranscriptReviewStore? _store;
    private readonly ISpeakerDataReader? _source;
    private readonly double _minOverlap;
    private int _generation;
    private int _editVersion;
    private IReadOnlyList<ReviewedTranscriptLine> _baseline = Array.Empty<ReviewedTranscriptLine>();
    public Guid? SessionId { get; private set; }
    public bool IsAvailable => _store is not null && _source is not null;
    public IReadOnlyList<TranscriptReviewRowVm> Rows { get; private set; } = Array.Empty<TranscriptReviewRowVm>();
    public bool HasRows => Rows.Count > 0;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardCommand))]
    private bool _isDirty;
    [ObservableProperty] private bool _hasSavedChanges;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardCommand))]
    private bool _isSaving;
    [ObservableProperty] private bool _isLoading;
    public event Action<IReadOnlyList<ReviewedTranscriptLine>>? ReviewApplied;

    public TranscriptReviewViewModel(ITranscriptReviewStore? store = null, ISpeakerDataReader? source = null, double minOverlap = 0.5)
    { _store = store; _source = source; _minOverlap = minOverlap; }

    public void Clear()
    {
        ++_generation;
        SessionId = null;
        _baseline = Array.Empty<ReviewedTranscriptLine>();
        SetRows(_baseline);
        IsDirty = HasSavedChanges = IsLoading = false;
        Status = string.Empty;
    }

    public async Task LoadAsync(Guid sessionId)
    {
        Clear();
        if (!IsAvailable) return;
        SessionId = sessionId;
        int generation = _generation;
        IsLoading = true;
        Status = "Metin yükleniyor…";
        try
        {
            var saved = await _store!.GetTranscriptReviewAsync(sessionId, CancellationToken.None);
            var lines = saved ?? await ReadAutomaticAsync(sessionId);
            if (generation != _generation) return;
            _baseline = lines.ToArray();
            SetRows(_baseline);
            HasSavedChanges = saved is not null;
            Status = HasRows ? (HasSavedChanges ? "Kaydedilmiş düzeltmeler yüklendi." : "Otomatik metin; kontrol edip düzeltebilirsin.")
                : "Bu oturumda düzeltilecek metin yok.";
            if (saved is not null) ReviewApplied?.Invoke(_baseline);
        }
        catch (Exception)
        {
            if (generation == _generation) Status = "Metin yüklenemedi; kayıt değiştirilmedi.";
        }
        finally
        {
            if (generation == _generation) IsLoading = false;
        }
    }

    private async Task<IReadOnlyList<ReviewedTranscriptLine>> ReadAutomaticAsync(Guid id)
    {
        var transcript = await _source!.GetTranscriptSegmentsAsync(id, CancellationToken.None);
        if (transcript.Count == 0)
        {
            var chunks = await _source.GetChunkResultsAsync(id, CancellationToken.None);
            transcript = chunks.Select(c => new TranscriptSegment(c.Transcript, c.TimeStart, c.TimeEnd)).ToArray();
        }
        var speakers = await _source.GetSpeakerSegmentsAsync(id, CancellationToken.None);
        return SpeakerLineBuilder.Build(SpeakerAligner.AlignSplitting(transcript, speakers, _minOverlap))
            .Where(l => l.End > l.Start)
            .Select(l => new ReviewedTranscriptLine(l.Start, l.End, l.Text,
                l.Speaker.Index < 0 ? string.Empty : l.Speaker.DefaultLabel, l.Uncertain)).ToArray();
    }

    // Arka plan konuşmacı geçişi kullanıcı taslağını veya kayıtlı düzeltmeleri ezemez.
    public async Task RefreshSpeakersAsync(Guid sessionId)
    {
        if (!IsAvailable || SessionId != sessionId || IsLoading || IsDirty || HasSavedChanges) return;
        int generation = _generation, editVersion = _editVersion;
        try
        {
            var lines = await ReadAutomaticAsync(sessionId);
            if (generation != _generation || editVersion != _editVersion || IsDirty || HasSavedChanges) return;
            _baseline = lines;
            SetRows(lines);
        }
        catch (Exception)
        {
            if (generation == _generation) Status = "Konuşmacılar güncellenemedi; mevcut metin korundu.";
        }
    }

    private void SetRows(IReadOnlyList<ReviewedTranscriptLine> lines)
    {
        foreach (var row in Rows) row.PropertyChanged -= RowChanged;
        Rows = lines.Select(l => new TranscriptReviewRowVm(l)).ToArray();
        foreach (var row in Rows) row.PropertyChanged += RowChanged;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasRows));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TranscriptReviewRowVm.Text) or nameof(TranscriptReviewRowVm.Speaker))) return;
        ++_editVersion;
        IsDirty = !Rows.Select(r => r.Snapshot()).SequenceEqual(_baseline);
        Status = IsDirty ? "Kaydedilmemiş değişiklikler var." : string.Empty;
    }

    private bool CanSave() => IsAvailable && SessionId.HasValue && HasRows && IsDirty && !IsSaving && !IsLoading;
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        var id = SessionId!.Value;
        int generation = _generation;
        var snapshot = Rows.Select(r => r.Snapshot()).ToArray();
        IsSaving = true;
        try
        {
            await _store!.SaveTranscriptReviewAsync(id, snapshot, CancellationToken.None);
            if (generation != _generation) return;
            _baseline = snapshot;
            HasSavedChanges = true;
            IsDirty = !Rows.Select(r => r.Snapshot()).SequenceEqual(snapshot);
            Status = IsDirty ? "Önceki düzenleme kaydedildi; yeni değişiklikler henüz kaydedilmedi." : "Düzeltmeler kaydedildi.";
            ReviewApplied?.Invoke(snapshot);
        }
        catch (Exception)
        {
            if (generation == _generation) Status = "Düzeltmeler kaydedilemedi; taslağın ekranda duruyor.";
        }
        finally { IsSaving = false; }
    }

    private bool CanDiscard() => IsDirty && !IsSaving;
    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard()
    {
        ++_editVersion;
        SetRows(_baseline);
        IsDirty = false;
        Status = "Kaydedilmemiş değişiklikler geri alındı.";
    }
}
