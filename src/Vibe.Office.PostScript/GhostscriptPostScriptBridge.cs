using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vibe.Office.FlowDocument.IO;

/// <summary>
/// Default <see cref="IPostScriptBridge"/> implementation that delegates EPS/PS conversion to Ghostscript.
/// </summary>
public sealed class GhostscriptPostScriptBridge : IPostScriptBridge
{
    private static readonly string[] DefaultExecutableCandidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ["gswin64c.exe", "gswin32c.exe", "gs"]
        : ["gs"];

    private readonly PostScriptConversionOptions _options;

    public GhostscriptPostScriptBridge(PostScriptConversionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task ConvertPostScriptToPdfAsync(
        string sourcePath,
        string targetPdfPath,
        PostScriptKind kind,
        CancellationToken cancellationToken = default)
    {
        ValidateSourceAndTarget(sourcePath, targetPdfPath);
        var arguments = new List<string>(10)
        {
            "-dSAFER",
            "-dBATCH",
            "-dNOPAUSE",
            "-dNOPROMPT",
            "-sDEVICE=pdfwrite",
            "-dCompatibilityLevel=1.7",
            $"-sOutputFile={targetPdfPath}"
        };

        if (kind == PostScriptKind.Eps)
        {
            arguments.Add("-dEPSCrop");
        }

        arguments.Add(sourcePath);
        return ExecuteGhostscriptAsync(arguments, cancellationToken);
    }

    public Task ConvertPdfToPostScriptAsync(
        string sourcePdfPath,
        string targetPath,
        PostScriptKind kind,
        CancellationToken cancellationToken = default)
    {
        ValidateSourceAndTarget(sourcePdfPath, targetPath);
        var device = kind == PostScriptKind.Eps ? "eps2write" : "ps2write";
        var arguments = new List<string>(9)
        {
            "-dSAFER",
            "-dBATCH",
            "-dNOPAUSE",
            "-dNOPROMPT",
            "-dLanguageLevel=3",
            $"-sDEVICE={device}",
            $"-sOutputFile={targetPath}",
            sourcePdfPath
        };

        return ExecuteGhostscriptAsync(arguments, cancellationToken);
    }

    private async Task ExecuteGhostscriptAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var candidates = ResolveExecutableCandidates();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("Ghostscript executable was not configured.");
        }

        Exception? lastExecutableFailure = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var executable = candidates[index];
            try
            {
                await ExecuteGhostscriptProcessAsync(executable, arguments, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (ExecutableNotFoundException ex)
            {
                lastExecutableFailure = ex;
            }
        }

        var candidateList = string.Join(", ", candidates);
        throw new InvalidOperationException(
            $"Ghostscript executable was not found. Install Ghostscript or set '{nameof(PostScriptConversionOptions.GhostscriptPath)}'. Tried: {candidateList}.",
            lastExecutableFailure);
    }

    private async Task ExecuteGhostscriptProcessAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        for (var index = 0; index < arguments.Count; index++)
        {
            startInfo.ArgumentList.Add(arguments[index]);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (IsExecutableNotFound(ex))
        {
            throw new ExecutableNotFoundException(executable, ex);
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        var timeout = NormalizeTimeout(_options.ProcessTimeout);
        if (timeout.HasValue)
        {
            timeoutCts = new CancellationTokenSource(timeout.Value);
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        }

        var effectiveToken = linkedCts?.Token ?? cancellationToken;
        try
        {
            await process.WaitForExitAsync(effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminate(process);
            throw new TimeoutException($"Ghostscript conversion exceeded timeout of {timeout}.");
        }
        catch
        {
            TryTerminate(process);
            throw;
        }
        finally
        {
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }

        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var message = BuildFailureMessage(process.ExitCode, stdOut, stdErr);
            throw new InvalidOperationException(message);
        }
    }

    private IReadOnlyList<string> ResolveExecutableCandidates()
    {
        if (!string.IsNullOrWhiteSpace(_options.GhostscriptPath))
        {
            return [_options.GhostscriptPath.Trim()];
        }

        return DefaultExecutableCandidates;
    }

    private static TimeSpan? NormalizeTimeout(TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return TimeSpan.FromMinutes(2);
        }

        return timeout;
    }

    private static string BuildFailureMessage(int exitCode, string stdOut, string stdErr)
    {
        var details = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
        if (string.IsNullOrWhiteSpace(details))
        {
            return $"Ghostscript exited with code {exitCode}.";
        }

        var normalized = details.Trim();
        if (normalized.Length > 1024)
        {
            normalized = normalized[..1024];
        }

        return $"Ghostscript exited with code {exitCode}: {normalized}";
    }

    private static bool IsExecutableNotFound(Exception ex)
    {
        return ex is Win32Exception { NativeErrorCode: 2 }
            or FileNotFoundException
            or DirectoryNotFoundException;
    }

    private static void TryTerminate(Process process)
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
            // best effort termination.
        }
    }

    private static void ValidateSourceAndTarget(string sourcePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Target path is required.", nameof(targetPath));
        }
    }

    private sealed class ExecutableNotFoundException : Exception
    {
        public ExecutableNotFoundException(string executable, Exception innerException)
            : base($"Executable '{executable}' was not found.", innerException)
        {
        }
    }
}
