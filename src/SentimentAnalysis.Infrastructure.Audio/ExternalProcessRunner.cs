using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SentimentAnalysis.Infrastructure.Audio;

public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        
        var stdOutBuilder = new StringBuilder();
        var stdErrBuilder = new StringBuilder();

        using var outputCloseEvent = new SemaphoreSlim(0);
        using var errorCloseEvent = new SemaphoreSlim(0);

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null)
            {
                outputCloseEvent.Release();
            }
            else
            {
                stdOutBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data == null)
            {
                errorCloseEvent.Release();
            }
            else
            {
                stdErrBuilder.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"'{fileName}' işlemi başlatılamadı. Dosya erişilebilir veya geçerli bir yürütülebilir dosya olmayabilir.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Ses dönüştürme aracı başlatılamadı veya bulunamadı: {fileName}. Lütfen aracın kurulu olduğundan emin olun.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Register cancellation
        using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Shield any exception from shadow-killing to prevent overriding the cancellation token exception
            }
        });

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Shield exceptions to preserve original OperationCanceledException
            }
            throw;
        }

        // Wait for stdout and stderr to finish reading with a timeout linked to the cancellation token
        try
        {
            await Task.WhenAll(outputCloseEvent.WaitAsync(ct), errorCloseEvent.WaitAsync(ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // If task was canceled while waiting for buffers
            throw;
        }

        return new ProcessResult(process.ExitCode, stdErrBuilder.ToString().Trim(), stdOutBuilder.ToString().Trim());
    }
}
