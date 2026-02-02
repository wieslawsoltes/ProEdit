using Vibe.Office.Editing;

namespace Vibe.Office.Proofing.GrammarApi;

public sealed class GrammarApiEngineFactory : IProofingEngineFactory
{
    public string EngineId => "grammarapi";
    public string DisplayName => "GrammarApi";
    public ProofingEngineKind Kind => ProofingEngineKind.Spell | ProofingEngineKind.Grammar;

    public object? Create(ProofingEngineContext context)
    {
        var options = GrammarApiEngineOptions.FromSettings(context.Settings);
        var endpoint = ResolveEndpoint(context, options);
        if (endpoint is null)
        {
            return null;
        }

        options.Endpoint = endpoint;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.ApiKey = ResolveApiKey(context);
        }

        var host = new GrammarApiHttpHost(options);
        return new GrammarApiEngine(host, options);
    }

    private static Uri? ResolveEndpoint(ProofingEngineContext context, GrammarApiEngineOptions options)
    {
        if (context.Settings.TryGetValue("endpoint", out var endpointText)
            && Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint))
        {
            return NormalizeEndpoint(endpoint);
        }

        var env = Environment.GetEnvironmentVariable("VIBE_GRAMMAR_API_URL");
        if (!string.IsNullOrWhiteSpace(env) && Uri.TryCreate(env, UriKind.Absolute, out var envEndpoint))
        {
            return NormalizeEndpoint(envEndpoint);
        }

        return NormalizeEndpoint(options.Endpoint);
    }

    private static Uri NormalizeEndpoint(Uri endpoint)
    {
        var path = endpoint.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrEmpty(path) || string.Equals(path, "/", StringComparison.Ordinal))
        {
            return new Uri(endpoint, "/v1/check");
        }

        if (string.Equals(path, "/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(endpoint, "/v1/check");
        }

        return endpoint;
    }

    private static string? ResolveApiKey(ProofingEngineContext context)
    {
        if (context.Settings.TryGetValue("apiKey", out var key) && !string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        var env = Environment.GetEnvironmentVariable("VIBE_GRAMMAR_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }
}
