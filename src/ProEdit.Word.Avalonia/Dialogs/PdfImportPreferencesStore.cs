using System.Text.Json;
using ProEdit.Pdf;

namespace ProEdit.Word.Avalonia;

internal sealed class PdfImportPreferencesStore
{
    private const string FileName = "pdf-import.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public PdfImportPreferencesStore()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _filePath = Path.Combine(root, "ProEdit", "Word", FileName);
    }

    public async Task<PdfImportPreferencesResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return PdfImportPreferencesResult.Empty;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var preferences = await JsonSerializer.DeserializeAsync<PdfImportPreferences>(stream, SerializerOptions, cancellationToken);
            if (preferences is null)
            {
                return PdfImportPreferencesResult.Empty;
            }

            return new PdfImportPreferencesResult(true, preferences);
        }
        catch
        {
            return PdfImportPreferencesResult.Empty;
        }
    }

    public async Task SaveAsync(PdfImportPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, preferences, SerializerOptions, cancellationToken);
    }
}

internal sealed class PdfImportPreferences
{
    public int Version { get; set; } = 1;
    public PdfImportMode ImportMode { get; set; } = PdfImportMode.Reflow;
    public PdfPreservationMode PreservationMode { get; set; } = PdfPreservationMode.None;
    public bool SkipDialog { get; set; }
}

internal readonly record struct PdfImportPreferencesResult(bool HasValue, PdfImportPreferences? Preferences)
{
    public static PdfImportPreferencesResult Empty => new(false, null);
}
