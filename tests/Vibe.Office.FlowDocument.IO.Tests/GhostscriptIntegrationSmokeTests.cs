using System.Diagnostics;
using System.Runtime.InteropServices;
using Vibe.Office.FlowDocument;
using Vibe.Office.FlowDocument.IO;
using Xunit;

namespace Vibe.Office.FlowDocument.IO.Tests;

public sealed class GhostscriptIntegrationSmokeTests
{
    [Fact]
    public async Task SaveAndLoadPs_WithGhostscript_Succeeds()
    {
        if (!TryResolveGhostscriptExecutable(out var executable))
        {
            return;
        }

        using var fixture = new TempDirectoryFixture();
        var options = new FlowDocumentFileConversionOptions();
        options.PostScriptOptions.GhostscriptPath = executable;
        options.PostScriptOptions.ProcessTimeout = TimeSpan.FromSeconds(120);
        var service = new FlowDocumentFileConversionService(options);
        var path = Path.Combine(fixture.Path, "smoke.ps");

        await service.SaveAsync(CreateSimpleDocument("Ghostscript PS smoke"), path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    [Fact]
    public async Task SaveAndLoadEps_WithGhostscript_Succeeds()
    {
        if (!TryResolveGhostscriptExecutable(out var executable))
        {
            return;
        }

        using var fixture = new TempDirectoryFixture();
        var options = new FlowDocumentFileConversionOptions();
        options.PostScriptOptions.GhostscriptPath = executable;
        options.PostScriptOptions.ProcessTimeout = TimeSpan.FromSeconds(120);
        var service = new FlowDocumentFileConversionService(options);
        var path = Path.Combine(fixture.Path, "smoke.eps");

        await service.SaveAsync(CreateSimpleDocument("Ghostscript EPS smoke"), path);
        var loaded = await service.LoadAsync(path);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        Assert.NotNull(loaded);
        Assert.True(loaded.Blocks.Count > 0);
    }

    private static FlowDocument CreateSimpleDocument(string text)
    {
        var document = new FlowDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new FlowThickness(72, 72, 72, 72)
        };

        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run(text));
        document.Blocks.Add(paragraph);
        return document;
    }

    private static bool TryResolveGhostscriptExecutable(out string executable)
    {
        executable = string.Empty;

        var fromEnv = Environment.GetEnvironmentVariable(PostScriptRuntimeOptions.GhostscriptPathVariable);
        if (!string.IsNullOrWhiteSpace(fromEnv) && CanStartGhostscript(fromEnv.Trim()))
        {
            executable = fromEnv.Trim();
            return true;
        }

        var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "gswin64c.exe", "gswin32c.exe", "gs.exe", "gs" }
            : new[] { "gs" };

        for (var i = 0; i < candidates.Length; i++)
        {
            if (TryResolveOnPath(candidates[i], out var path) && CanStartGhostscript(path))
            {
                executable = path;
                return true;
            }
        }

        return false;
    }

    private static bool CanStartGhostscript(string executable)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best effort.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveOnPath(string candidate, out string resolvedPath)
    {
        resolvedPath = string.Empty;

        if (Path.IsPathRooted(candidate))
        {
            if (File.Exists(candidate))
            {
                resolvedPath = candidate;
                return true;
            }

            return false;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        var directories = pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ResolveWindowsExecutableExtensions(candidate)
            : new[] { string.Empty };

        for (var directoryIndex = 0; directoryIndex < directories.Length; directoryIndex++)
        {
            var directory = directories[directoryIndex];
            for (var extensionIndex = 0; extensionIndex < extensions.Length; extensionIndex++)
            {
                var extension = extensions[extensionIndex];
                var fileName = extension.Length == 0 ? candidate : candidate + extension;
                var fullPath = Path.Combine(directory, fileName);
                if (File.Exists(fullPath))
                {
                    resolvedPath = fullPath;
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] ResolveWindowsExecutableExtensions(string candidate)
    {
        if (Path.HasExtension(candidate))
        {
            return [string.Empty];
        }

        var pathExtValue = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExtValue))
        {
            return [".exe", ".cmd", ".bat"];
        }

        var extensions = pathExtValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => value.StartsWith(".", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return extensions.Length == 0 ? [".exe", ".cmd", ".bat"] : extensions;
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
