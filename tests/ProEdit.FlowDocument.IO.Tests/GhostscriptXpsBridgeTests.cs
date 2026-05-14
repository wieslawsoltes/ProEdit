using System.Diagnostics;
using System.Text;
using ProEdit.FlowDocument.IO;
using Xunit;

namespace ProEdit.FlowDocument.IO.Tests;

public sealed class GhostscriptXpsBridgeTests
{
    [Theory]
    [InlineData(XpsFlavor.Xps)]
    [InlineData(XpsFlavor.Oxps)]
    public async Task ConvertXpsToPdfAsync_UsesExpectedArguments(XpsFlavor flavor)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TempDirectoryFixture();
        var argsPath = Path.Combine(fixture.Path, "xps-to-pdf.args");
        var executable = CreateCaptureScript(fixture.Path, argsPath);
        var bridge = new GhostscriptXpsBridge(new XpsConversionOptions
        {
            GhostscriptPath = executable
        });

        var sourcePath = Path.Combine(fixture.Path, flavor == XpsFlavor.Oxps ? "input.oxps" : "input.xps");
        var targetPath = Path.Combine(fixture.Path, "output.pdf");
        await bridge.ConvertXpsToPdfAsync(sourcePath, targetPath, flavor);

        var argsText = await File.ReadAllTextAsync(argsPath);
        Assert.Contains("-sDEVICE=pdfwrite", argsText, StringComparison.Ordinal);
        Assert.Contains(sourcePath, argsText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(XpsFlavor.Xps, ".xps")]
    [InlineData(XpsFlavor.Oxps, ".oxps")]
    public async Task ConvertPdfToXpsAsync_UsesExpectedDevice(XpsFlavor flavor, string extension)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TempDirectoryFixture();
        var argsPath = Path.Combine(fixture.Path, "pdf-to-xps.args");
        var executable = CreateCaptureScript(fixture.Path, argsPath);
        var bridge = new GhostscriptXpsBridge(new XpsConversionOptions
        {
            GhostscriptPath = executable
        });

        var sourcePath = Path.Combine(fixture.Path, "input.pdf");
        var targetPath = Path.Combine(fixture.Path, "output" + extension);
        await bridge.ConvertPdfToXpsAsync(sourcePath, targetPath, flavor);

        var argsText = await File.ReadAllTextAsync(argsPath);
        Assert.Contains("-sDEVICE=xpswrite", argsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertPdfToXpsAsync_WhenProcessTimesOut_ThrowsTimeoutException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TempDirectoryFixture();
        var executable = CreateCaptureScript(fixture.Path, argsPath: null, trailer: "sleep 5");
        var bridge = new GhostscriptXpsBridge(new XpsConversionOptions
        {
            GhostscriptPath = executable,
            ProcessTimeout = TimeSpan.FromMilliseconds(100)
        });

        var sourcePath = Path.Combine(fixture.Path, "input.pdf");
        var targetPath = Path.Combine(fixture.Path, "output.xps");

        await Assert.ThrowsAsync<TimeoutException>(() => bridge.ConvertPdfToXpsAsync(sourcePath, targetPath, XpsFlavor.Xps));
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
