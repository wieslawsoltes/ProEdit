using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vibe.Office.Editing;

namespace Vibe.Office.Proofing.GrammarApi;

public sealed class GrammarApiHttpHost : IProofingEngineHost, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _client;
    private readonly Uri _checkUri;
    private readonly string? _apiKey;

    public string EngineId => "grammarapi";

    public GrammarApiHttpHost(GrammarApiEngineOptions options, HttpClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _checkUri = options.Endpoint;
        _apiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey;
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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _checkUri);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        var payload = new GrammarApiRequest
        {
            Text = request.Text
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProofingHostResponse(Array.Empty<ProofingHostMatch>(), EngineId);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<GrammarApiResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (result?.Matches is null || result.Matches.Count == 0)
        {
            return new ProofingHostResponse(Array.Empty<ProofingHostMatch>(), EngineId);
        }

        var matches = new List<ProofingHostMatch>(result.Matches.Count);
        foreach (var match in result.Matches)
        {
            var replacements = match.Replacements?
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .ToArray();

            var category = match.Rule?.Category;
            matches.Add(new ProofingHostMatch(
                match.Offset,
                match.Length,
                match.Message,
                match.Rule?.Id,
                category,
                category,
                replacements));
        }

        return new ProofingHostResponse(matches, EngineId);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private sealed class GrammarApiRequest
    {
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    }

    private sealed class GrammarApiResponse
    {
        [JsonPropertyName("matches")] public List<GrammarApiMatch>? Matches { get; set; }
    }

    private sealed class GrammarApiMatch
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("offset")] public int Offset { get; set; }
        [JsonPropertyName("length")] public int Length { get; set; }
        [JsonPropertyName("replacements")] public List<string>? Replacements { get; set; }
        [JsonPropertyName("rule")] public GrammarApiRule? Rule { get; set; }
    }

    private sealed class GrammarApiRule
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
    }
}
