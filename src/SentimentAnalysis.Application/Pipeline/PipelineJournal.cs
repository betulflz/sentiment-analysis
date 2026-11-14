using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

public enum PipelineExecutionMode { Realtime, Offline }

internal sealed class PipelineJournal
{
    private readonly object _sync = new();
    private readonly List<ChunkObservation> _chunks = new();
    private string _sourceStatus = "not-started";
    private string? _sourceFailure, _storageFailure;

    public void Reset()
    {
        lock (_sync)
        {
            _chunks.Clear();
            _sourceStatus = "running";
            _sourceFailure = _storageFailure = null;
        }
    }

    public int Capture(AudioChunk chunk)
    {
        lock (_sync)
        {
            int index = _chunks.Count;
            _chunks.Add(new(index, chunk.Start.TotalSeconds, chunk.End.TotalSeconds, ChunkDisposition.Pending));
            return index;
        }
    }

    public void Complete(int index, ChunkDisposition disposition, double seconds = 0, string? failure = null)
    {
        lock (_sync) _chunks[index] = _chunks[index] with
            { Disposition = disposition, ProcessingSeconds = seconds, FailureType = failure };
    }

    public void EnrichmentFailed(int index, Exception error)
    {
        lock (_sync) _chunks[index] = _chunks[index] with { EnrichmentFailureType = error.GetType().Name };
    }

    public void SourceEnded(string status, Exception? error = null)
    {
        lock (_sync) { _sourceStatus = status; _sourceFailure = error?.GetType().Name; }
    }

    public void StorageFailed(Exception error)
    {
        lock (_sync) _storageFailure = error.GetType().Name;
    }

    public void FinishPending()
    {
        lock (_sync)
            for (int i = 0; i < _chunks.Count; i++)
                if (_chunks[i].Disposition == ChunkDisposition.Pending)
                    _chunks[i] = _chunks[i] with { Disposition = ChunkDisposition.Unprocessed };
    }

    public AnalysisDiagnostics Snapshot()
    {
        lock (_sync)
        {
            var chunks = _chunks.ToArray();
            double captured = UnionSeconds(chunks);
            double evaluated = UnionSeconds(chunks.Where(c => c.Disposition is ChunkDisposition.Speech or ChunkDisposition.NoSpeech));
            return new(chunks, captured, evaluated, Math.Max(0, captured - evaluated),
                _sourceStatus, _sourceFailure, _storageFailure);
        }
    }

    private static double UnionSeconds(IEnumerable<ChunkObservation> chunks)
    {
        double total = 0, start = 0, end = 0;
        bool seen = false;
        foreach (var c in chunks.Where(c => c.EndSeconds > c.StartSeconds).OrderBy(c => c.StartSeconds))
        {
            if (!seen || c.StartSeconds > end)
            {
                if (seen) total += end - start;
                start = c.StartSeconds;
                end = c.EndSeconds;
                seen = true;
            }
            else end = Math.Max(end, c.EndSeconds);
        }
        return total + (seen ? end - start : 0);
    }
}
