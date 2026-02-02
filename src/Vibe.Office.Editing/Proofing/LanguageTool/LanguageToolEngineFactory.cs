using System;
using System.Globalization;

namespace Vibe.Office.Editing;

public sealed class LanguageToolEngineFactory : IProofingEngineFactory
{
    public string EngineId => "languagetool";
    public string DisplayName => "LanguageTool";
    public ProofingEngineKind Kind => ProofingEngineKind.Grammar | ProofingEngineKind.Style;

    public object? Create(ProofingEngineContext context)
    {
        var endpoint = ResolveEndpoint(context);
        if (endpoint is null)
        {
            return null;
        }

        var hostOptions = new LanguageToolHttpHostOptions(endpoint);
        var host = new LanguageToolHttpHost(hostOptions);
        var options = new LanguageToolEngineOptions
        {
            IncludeSpelling = GetBoolSetting(context, "includeSpelling", defaultValue: false),
            IncludeGrammar = GetBoolSetting(context, "includeGrammar", defaultValue: true),
            IncludeStyle = GetBoolSetting(context, "includeStyle", defaultValue: true)
        };

        return new LanguageToolEngine(host, options);
    }

    private static Uri? ResolveEndpoint(ProofingEngineContext context)
    {
        if (context.Settings.TryGetValue("endpoint", out var endpointText)
            && Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint))
        {
            return endpoint;
        }

        var env = Environment.GetEnvironmentVariable("VIBE_LANGUAGETOOL_URL");
        if (string.IsNullOrWhiteSpace(env))
        {
            return null;
        }

        return Uri.TryCreate(env, UriKind.Absolute, out var envEndpoint) ? envEndpoint : null;
    }

    private static bool GetBoolSetting(ProofingEngineContext context, string key, bool defaultValue)
    {
        if (!context.Settings.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric != 0;
        }

        return defaultValue;
    }
}
