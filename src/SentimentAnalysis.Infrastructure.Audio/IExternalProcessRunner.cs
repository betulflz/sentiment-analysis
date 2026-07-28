using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed record ProcessResult(int ExitCode, string StandardError, string StandardOutput);

public interface IExternalProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct);
}
