using System.Diagnostics;
using System.Text;
using ProEdit.FlowDocument.IO;
using Xunit;

namespace ProEdit.FlowDocument.IO.Tests;

public sealed class GhostscriptPostScriptBridgeTests
{
    [Theory]
    [InlineData(PostScriptKind.Ps, false)]
    [InlineData(PostScriptKind.Eps, true)]
    public async Task ConvertPostScriptToPdfAsync_UsesExpectedArguments(PostScriptKind kind, bool expectEpsCrop)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TempDirectoryFixture();
        var argsPath = Path.Combine(fixture.Path, "ps-to-pdf.args");
        var executable = CreateCaptureScript(fixture.Path, argsPath);
        var bridge = new GhostscriptPostScriptBridge(new PostScriptConversionOptions
        {
            GhostscriptPath = executable
        });

        var sourcePath = Path.Combine(fixture.Path, kind == PostScriptKind.Eps ? "input.eps" : "input.ps");
        var targetPath = Path.Combine(fixture.Path, "output.pdf");
        await bridge.ConvertPostScriptToPdfAsync(sourcePath, targetPath, kind);

        var argsText = await File.ReadAllTextAsync(argsPath);
        Assert.Contains("-sDEVICE=pdfwrite", argsText, StringComparison.Ordinal);
        if (expectEpsCrop)
        {
            Assert.Contains("-dEPSCrop", argsText, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("-dEPSCrop", argsText, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(PostScriptKind.Ps, "ps2write")]
    [InlineData(PostScriptKind.Eps, "eps2write")]
    public async Task ConvertPdfToPostScriptAsync_UsesExpectedDevice(PostScriptKind kind, string expectedDevice)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TempDirectoryFixture();
        var argsPath = Path.Combine(fixture.Path, "pdf-to-ps.args");
        var executable = CreateCaptureScript(fixture.Path, argsPath);
        var bridge = new GhostscriptPostScriptBridge(new PostScriptConversionOptions
        {
            GhostscriptPath = executable
        });

        var sourcePath = Path.Combine(fixture.Path, "input.pdf");
        var targetPath = Path.Combine(fixture.Path, kind == PostScriptKind.Eps ? "output.eps" : "output.ps");
        await bridge.ConvertPdfToPostScriptAsync(sourcePath, targetPath, kind);

        var argsText = await File.ReadAllTextAsync(argsPath);
        Assert.Contains($"-sDEVICE={expectedDevice}", argsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertPdfToPostScriptAsync_WhenProcessTimesOut_ThrowsTimeoutException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TempDirectoryFixture();
        var executable = CreateCaptureScript(fixture.Path, argsPath: null, trailer: "sleep 5");
        var bridge = new GhostscriptPostScriptBridge(new PostScriptConversionOptions
        {
            GhostscriptPath = executable,
            ProcessTimeout = TimeSpan.FromMilliseconds(100)
        });

        var sourcePath = Path.Combine(fixture.Path, "input.pdf");
        var targetPath = Path.Combine(fixture.Path, "output.ps");

        await Assert.ThrowsAsync<TimeoutException>(() => bridge.ConvertPdfToPostScriptAsync(sourcePath, targetPath, PostScriptKind.Ps));
    }

    private static string CreateCaptureScript(string directory, string? argsPath, string? trailer = null)
    {
        var path = Path.Combine(directory, "gs-probe.sh");
        var builder = new StringBuilder();
        builder.AppendLine("#!/bin/sh");
        if (!string.IsNullOrWhiteSpace(argsPath))
        {
            builder.Append("printf \"%s\\n\" \"$@\" > ");
            builder.AppendLine(QuoteShell(argsPath));
        }
        else
        {
            builder.AppendLine(":");
        }

        if (!string.IsNullOrWhiteSpace(trailer))
        {
            builder.AppendLine(trailer);
        }

        builder.AppendLine("exit 0");
        File.WriteAllText(path, builder.ToString());
        EnsureExecutable(path);
        return path;
    }

    private static string QuoteShell(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static void EnsureExecutable(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "chmod",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("+x");
        startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo);

        process?.WaitForExit();
    }

    private sealed class TempDirectoryFixture : IDisposable
    {
        public TempDirectoryFixture()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // best effort cleanup for test temp directory.
            }
        }
    }
}
