using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vibe.Office.Editing;

public sealed class LanguageToolHttpHost : IProofingEngineHost, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _client;
    private readonly Uri _checkUri;

    public string EngineId => "LanguageTool";

    public LanguageToolHttpHost(LanguageToolHttpHostOptions options, HttpClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _checkUri = new Uri(options.Endpoint, "/v2/check");
        _client = client ?? new HttpClient();
        _client.Timeout = options.Timeout;
        _client.DefaultRequestHeaders.Accept.Clear();
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ProofingHostResponse> CheckAsync(ProofingHostRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return new ProofingHostResponse(Array.Empty<ProofingHostMatch>(), EngineId);
        }

        var form = new Dictionary<string, string>
        {
            ["text"] = request.Text,
            ["language"] = request.Language
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await _client.PostAsync(_checkUri, content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProofingHostResponse(Array.Empty<ProofingHostMatch>(), EngineId);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<LanguageToolResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (result?.Matches is null || result.Matches.Count == 0)
        {
            return new ProofingHostResponse(Array.Empty<ProofingHostMatch>(), EngineId);
        }

        var matches = new List<ProofingHostMatch>(result.Matches.Count);
        foreach (var match in result.Matches)
        {
            var replacements = match.Replacements?
                .Select(static item => item.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .ToArray();
            matches.Add(new ProofingHostMatch(
                match.Offset,
                match.Length,
                match.Message,
                match.Rule?.Id,
                match.Rule?.IssueType,
                match.Rule?.Category?.Id,
                replacements));
        }

        return new ProofingHostResponse(matches, EngineId);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private sealed class LanguageToolResponse
    {
        [JsonPropertyName("matches")] public List<LanguageToolMatch>? Matches { get; set; }
    }

    private sealed class LanguageToolMatch
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("offset")] public int Offset { get; set; }
        [JsonPropertyName("length")] public int Length { get; set; }
        [JsonPropertyName("replacements")] public List<LanguageToolReplacement>? Replacements { get; set; }
        [JsonPropertyName("rule")] public LanguageToolRule? Rule { get; set; }
    }

    private sealed class LanguageToolReplacement
    {
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    private sealed class LanguageToolRule
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("issueType")] public string? IssueType { get; set; }
        [JsonPropertyName("category")] public LanguageToolCategory? Category { get; set; }
    }

    private sealed class LanguageToolCategory
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }
}
