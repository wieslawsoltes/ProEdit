using System.Text.Json;

namespace Vibe.Word.Avalonia;

internal sealed class RibbonQuickAccessStore
{
    private const string FileName = "ribbon-qat.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public RibbonQuickAccessStore()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _filePath = Path.Combine(root, "VibeOffice", "VibeWord", FileName);
    }

    public async Task<RibbonQuickAccessLayoutResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return RibbonQuickAccessLayoutResult.Empty;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var layout = await JsonSerializer.DeserializeAsync<RibbonQuickAccessLayout>(stream, SerializerOptions, cancellationToken);
            if (layout?.Items is null)
            {
                return new RibbonQuickAccessLayoutResult(true, Array.Empty<string>());
            }

            return new RibbonQuickAccessLayoutResult(true, layout.Items);
        }
        catch
        {
            return RibbonQuickAccessLayoutResult.Empty;
        }
    }

    public async Task SaveAsync(IEnumerable<string> controlIds, CancellationToken cancellationToken = default)
    {
        var items = controlIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        var layout = new RibbonQuickAccessLayout { Items = items };
        await JsonSerializer.SerializeAsync(stream, layout, SerializerOptions, cancellationToken);
    }

    private sealed class RibbonQuickAccessLayout
    {
        public int Version { get; set; } = 1;
        public List<string> Items { get; set; } = new();
    }
}

internal readonly record struct RibbonQuickAccessLayoutResult(bool HasValue, IReadOnlyList<string> ControlIds)
{
    public static RibbonQuickAccessLayoutResult Empty => new(false, Array.Empty<string>());
}
