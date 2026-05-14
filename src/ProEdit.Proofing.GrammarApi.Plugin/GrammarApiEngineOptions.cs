using System.Globalization;

namespace ProEdit.Proofing.GrammarApi;

public sealed class GrammarApiEngineOptions
{
    public Uri Endpoint { get; set; } = new Uri("http://localhost:8080/v1/check");
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
    public string? ApiKey { get; set; }
    public bool IncludeSpelling { get; set; } = true;
    public bool IncludeGrammar { get; set; } = true;
    public int MaxSuggestions { get; set; } = 5;

    public static GrammarApiEngineOptions FromSettings(IReadOnlyDictionary<string, string>? settings)
    {
        var options = new GrammarApiEngineOptions();
        if (settings is null)
        {
            return options;
        }

        if (TryGetString(settings, "endpoint", out var endpointText)
            && Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint))
        {
            options.Endpoint = endpoint;
        }

        if (TryGetString(settings, "apiKey", out var apiKey))
        {
            options.ApiKey = apiKey;
        }

        if (TryGetInt(settings, "timeoutSeconds", out var timeoutSeconds) && timeoutSeconds > 0)
        {
            options.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        options.IncludeSpelling = GetBool(settings, "includeSpelling", options.IncludeSpelling);
        options.IncludeGrammar = GetBool(settings, "includeGrammar", options.IncludeGrammar);
        options.MaxSuggestions = GetInt(settings, "maxSuggestions", options.MaxSuggestions);

        return options;
    }

    private static bool TryGetString(IReadOnlyDictionary<string, string> settings, string key, out string value)
    {
        value = string.Empty;
        if (!settings.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> settings, string key, bool defaultValue)
    {
        if (!settings.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric != 0;
        }

        return defaultValue;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> settings, string key, int defaultValue)
    {
        if (!settings.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> settings, string key, out int value)
    {
        value = 0;
        if (!settings.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
