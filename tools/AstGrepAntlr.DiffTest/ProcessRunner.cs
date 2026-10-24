using System.Diagnostics;
using System.Text;

namespace AstGrepAntlr.DiffTest;

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

internal static class ProcessRunner
{
    public static Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default
    ) => RunAsync(fileName, arguments, workingDirectory, timeout: null, cancellationToken);

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan? timeout,
        CancellationToken cancellationToken = default
    )
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var timeoutCts = timeout is { } t
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        timeoutCts?.CancelAfter(timeout!.Value);
        var effective = timeoutCts?.Token ?? cancellationToken;

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(effective);
            var stderrTask = process.StandardError.ReadToEndAsync(effective);
            await process.WaitForExitAsync(effective);
            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore kill failures
            }

            return new ProcessResult(
                124,
                "",
                $"difftest: process timed out after {timeout?.TotalSeconds:0}s: {fileName} {string.Join(' ', arguments.Take(6))}..."
            );
        }
    }
}
