using System;
using System.IO;
using System.Text.Json;

namespace Vibe.Office.Editing;

public sealed class ProofingOptionsStore : IProofingOptionsStore
{
    private const string FileName = "proofing-options.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public ProofingOptionsStore(string? filePath = null)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _filePath = filePath;
            return;
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _filePath = Path.Combine(root, "VibeOffice", "VibeWord", FileName);
    }

    public ProofingOptions Load()
    {
        if (!File.Exists(_filePath))
        {
            return ProofingOptions.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var options = JsonSerializer.Deserialize<ProofingOptions>(json, SerializerOptions);
            return options ?? ProofingOptions.CreateDefault();
        }
        catch
        {
            return ProofingOptions.CreateDefault();
        }
    }

    public void Save(ProofingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(options, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}
