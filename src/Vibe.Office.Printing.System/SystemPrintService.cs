using System.Diagnostics;
using System.Globalization;
using Vibe.Office.Printing;

namespace Vibe.Office.Printing.System;

public sealed class SystemPrintService : IPrinterDiscovery, IPrintTransport
{
    public ValueTask<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<IReadOnlyList<PrinterInfo>>(Task.Run(() => ResolvePrinters(cancellationToken), cancellationToken));
    }

    public ValueTask<PrinterInfo?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask<PrinterInfo?>(Task.Run(() => ResolveDefaultPrinter(cancellationToken), cancellationToken));
    }

    public ValueTask<PrintJobResult> SendToPrinterAsync(string pdfPath, PrintSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentNullException.ThrowIfNull(settings);
        return new ValueTask<PrintJobResult>(Task.Run(() => SendToPrinter(pdfPath, settings, cancellationToken), cancellationToken));
    }

    private static IReadOnlyList<PrinterInfo> ResolvePrinters(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            return ResolveWindowsPrinters();
        }

        return ResolveUnixPrinters();
    }

    private static PrinterInfo? ResolveDefaultPrinter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            return ResolveWindowsDefaultPrinter();
        }

        return ResolveUnixDefaultPrinter();
    }

    private static IReadOnlyList<PrinterInfo> ResolveWindowsPrinters()
    {
        var printers = new List<PrinterInfo>();
        var defaultPrinter = ResolveWindowsDefaultPrinter()?.Name;
        var output = RunProcess("powershell", "-NoProfile", "-Command",
            "Get-CimInstance -ClassName Win32_Printer | Select-Object -ExpandProperty Name");
        if (string.IsNullOrWhiteSpace(output))
        {
            return printers;
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            printers.Add(new PrinterInfo(line, string.Equals(line, defaultPrinter, StringComparison.OrdinalIgnoreCase)));
        }

        return printers;
    }

    private static PrinterInfo? ResolveWindowsDefaultPrinter()
    {
        var output = RunProcess("powershell", "-NoProfile", "-Command",
            "Get-CimInstance -ClassName Win32_Printer | Where-Object {$_.Default} | Select-Object -First 1 -ExpandProperty Name");
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var name = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(name) ? null : new PrinterInfo(name, true);
    }

    private static IReadOnlyList<PrinterInfo> ResolveUnixPrinters()
    {
        var printers = new List<PrinterInfo>();
        var defaultPrinter = ResolveUnixDefaultPrinter()?.Name;
        var output = RunProcess("lpstat", "-p");
        if (string.IsNullOrWhiteSpace(output))
        {
            return printers;
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("printer ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var name = parts[1];
            printers.Add(new PrinterInfo(name, string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase)));
        }

        return printers;
    }

    private static PrinterInfo? ResolveUnixDefaultPrinter()
    {
        var output = RunProcess("lpstat", "-d");
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var marker = "default destination:";
        var index = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var name = output[(index + marker.Length)..].Trim();
        return string.IsNullOrWhiteSpace(name) ? null : new PrinterInfo(name, true);
    }

    private static PrintJobResult SendToPrinter(string pdfPath, PrintSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            return SendToWindowsPrinter(pdfPath, settings);
        }

        return SendToUnixPrinter(pdfPath, settings, cancellationToken);
    }

    private static PrintJobResult SendToWindowsPrinter(string pdfPath, PrintSettings settings)
    {
        try
        {
            var printerName = settings.PrinterName;
            var verb = string.IsNullOrWhiteSpace(printerName) ? "print" : "printto";
            var arguments = string.IsNullOrWhiteSpace(printerName)
                ? string.Empty
                : $"\"{printerName}\"";

            var psi = new ProcessStartInfo
            {
                FileName = pdfPath,
                Verb = verb,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var copies = Math.Max(1, settings.Copies);
            for (var i = 0; i < copies; i++)
            {
                using var process = Process.Start(psi);
                process?.WaitForExit(10000);
            }

            return PrintJobResult.Success();
        }
        catch (Exception ex)
        {
            return PrintJobResult.Failed($"Failed to print: {ex.Message}");
        }
    }

    private static PrintJobResult SendToUnixPrinter(string pdfPath, PrintSettings settings, CancellationToken cancellationToken)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.PrinterName))
        {
            args.Add("-d");
            args.Add(settings.PrinterName!);
        }

        if (settings.Copies > 1)
        {
            args.Add("-n");
            args.Add(settings.Copies.ToString(CultureInfo.InvariantCulture));
        }

        switch (settings.Duplex)
        {
            case PrintDuplexMode.TwoSidedLongEdge:
                args.Add("-o");
                args.Add("sides=two-sided-long-edge");
                break;
            case PrintDuplexMode.TwoSidedShortEdge:
                args.Add("-o");
                args.Add("sides=two-sided-short-edge");
                break;
            case PrintDuplexMode.OneSided:
                args.Add("-o");
                args.Add("sides=one-sided");
                break;
        }

        if (settings.ColorMode == PrintColorMode.Grayscale)
        {
            args.Add("-o");
            args.Add("ColorModel=Gray");
        }

        args.Add(pdfPath);

        cancellationToken.ThrowIfCancellationRequested();
        var output = RunProcess("lp", args.ToArray());
        return string.IsNullOrWhiteSpace(output)
            ? PrintJobResult.Failed("No response from printing subsystem.")
            : PrintJobResult.Success();
    }

    private static string RunProcess(string fileName, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            if (!string.IsNullOrWhiteSpace(error))
            {
                output = string.Join(Environment.NewLine, output, error);
            }

            return output.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
