using System.Net.Http.Json;

namespace Vibe.Office.Reporting.Data;

internal sealed class RestJsonReportDataProvider : IReportDataProvider
{
    public string ProviderId => ReportProviderIds.RestJson;

    public ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        return ReportHttpDataProviderSupport.ExecuteRestLikeJsonAsync(
            dataSource,
            dataSet,
            context,
            defaultJsonPath: ReportDataProviderSupport.GetOption(dataSource, "jsonPath"),
            unwrapSinglePropertyContainer: false,
            defaultMethod: HttpMethod.Get,
            cancellationToken);
    }
}

internal sealed class ODataReportDataProvider : IReportDataProvider
{
    public string ProviderId => ReportProviderIds.OData;

    public ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        return ReportHttpDataProviderSupport.ExecuteRestLikeJsonAsync(
            dataSource,
            dataSet,
            context,
            defaultJsonPath: "$.value",
            unwrapSinglePropertyContainer: false,
            defaultMethod: HttpMethod.Get,
            cancellationToken);
    }
}

internal sealed class GraphQlReportDataProvider : IReportDataProvider
{
    public string ProviderId => ReportProviderIds.GraphQl;

    public async ValueTask<ReportDataTable> ExecuteAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = ReportHttpDataProviderSupport.ResolveConnectionDefinition(dataSource, context);
        var httpClient = ReportHttpDataProviderSupport.ResolveHttpClient(connection, context.HostDataRegistry);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ReportHttpDataProviderSupport.ResolveGraphQlRequestUri(
                connection,
                dataSource,
                httpClient,
                context.DataSetParameters));

        ReportHttpDataProviderSupport.ApplyHeaders(request, connection, dataSource);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["query"] = dataSet.Query,
            ["variables"] = context.DataSetParameters.Count == 0
                ? new Dictionary<string, object?>()
                : context.DataSetParameters.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase)
        };

        request.Content = JsonContent.Create(payload);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await ReportHttpDataProviderSupport.ReadJsonResponseAsync(response, cancellationToken);
        var dataPath = ReportDataProviderSupport.GetOption(dataSource, "dataPath") ?? "$.data";
        return ReportJsonDataHelpers.ParseTable(
            json,
            dataPath,
            dataSet.Id,
            unwrapSinglePropertyContainer: true);
    }
}

internal static class ReportHttpDataProviderSupport
{
    private static readonly HttpClient SharedDefaultHttpClient = new();

    public static async ValueTask<ReportDataTable> ExecuteRestLikeJsonAsync(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        ReportDataProviderContext context,
        string? defaultJsonPath,
        bool unwrapSinglePropertyContainer,
        HttpMethod defaultMethod,
        CancellationToken cancellationToken)
    {
        var connection = ResolveConnectionDefinition(dataSource, context);
        var httpClient = ResolveHttpClient(connection, context.HostDataRegistry);
        var method = ResolveHttpMethod(dataSource, defaultMethod);
        using var request = new HttpRequestMessage(
            method,
            ResolveRequestUri(connection, dataSource, dataSet, httpClient, context.DataSetParameters));
        ApplyHeaders(request, connection, dataSource);

        if (!IsHttpMethod(method, HttpMethod.Get) && !IsHttpMethod(method, HttpMethod.Head))
        {
            request.Content = JsonContent.Create(
                context.DataSetParameters.Count == 0
                    ? new Dictionary<string, object?>()
                    : context.DataSetParameters.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase));
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await ReadJsonResponseAsync(response, cancellationToken);
        var jsonPath = ReportDataProviderSupport.GetOption(dataSource, "jsonPath") ?? defaultJsonPath;
        return ReportJsonDataHelpers.ParseTable(json, jsonPath, dataSet.Id, unwrapSinglePropertyContainer);
    }

    public static ReportConnectionDefinition ResolveConnectionDefinition(
        ReportDataSourceDefinition dataSource,
        ReportDataProviderContext context)
    {
        var connectionKey = ReportDataProviderSupport.ResolveConnectionKey(dataSource);
        if (context.HostDataRegistry.TryGetConnection(connectionKey, out var connection))
        {
            if (!string.IsNullOrWhiteSpace(connection.ProviderId)
                && !connection.ProviderId.Equals(dataSource.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Connection '{connectionKey}' is registered for provider '{connection.ProviderId}' but data source '{dataSource.Id}' requested '{dataSource.ProviderId}'.");
            }

            return connection;
        }

        var inlineConnection = new ReportConnectionDefinition
        {
            Name = connectionKey,
            ProviderId = dataSource.ProviderId,
            BaseAddress = ReportDataProviderSupport.GetOption(dataSource, "baseAddress")
        };

        foreach (var pair in dataSource.Options)
        {
            if (pair.Key.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
            {
                inlineConnection.Headers[pair.Key["header:".Length..]] = pair.Value;
            }
        }

        return inlineConnection;
    }

    public static HttpClient ResolveHttpClient(
        ReportConnectionDefinition connection,
        ReportHostDataRegistry hostDataRegistry)
    {
        if (hostDataRegistry.TryGetHttpClient(connection.Name, out var httpClient))
        {
            return httpClient;
        }

        return SharedDefaultHttpClient;
    }

    public static Uri ResolveRequestUri(
        ReportConnectionDefinition connection,
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet,
        HttpClient httpClient,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var resourcePath = ResolveResourcePath(dataSource, dataSet);
        return ResolveRequestUri(
            connection,
            dataSource,
            resourcePath,
            httpClient,
            parameters,
            appendGetQueryString: true);
    }

    public static Uri ResolveGraphQlRequestUri(
        ReportConnectionDefinition connection,
        ReportDataSourceDefinition dataSource,
        HttpClient httpClient,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var resourcePath = ReportDataProviderSupport.GetOption(dataSource, "resourcePath") ?? string.Empty;
        return ResolveRequestUri(
            connection,
            dataSource,
            resourcePath,
            httpClient,
            parameters,
            appendGetQueryString: false);
    }

    private static Uri ResolveRequestUri(
        ReportConnectionDefinition connection,
        ReportDataSourceDefinition dataSource,
        string resourcePath,
        HttpClient httpClient,
        IReadOnlyDictionary<string, object?> parameters,
        bool appendGetQueryString)
    {
        if (Uri.TryCreate(resourcePath, UriKind.Absolute, out var absoluteUri))
        {
            return appendGetQueryString
                ? AppendQueryString(absoluteUri, parameters, dataSource)
                : absoluteUri;
        }

        var baseAddress = ResolveBaseAddress(connection, httpClient);
        if (baseAddress is null)
        {
            throw new InvalidOperationException(
                $"Data source '{dataSource.Id}' requires an absolute endpoint or a connection/base address.");
        }

        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return appendGetQueryString
                ? AppendQueryString(baseAddress, parameters, dataSource)
                : baseAddress;
        }

        var combined = new Uri(baseAddress, resourcePath);
        return appendGetQueryString
            ? AppendQueryString(combined, parameters, dataSource)
            : combined;
    }

    public static void ApplyHeaders(
        HttpRequestMessage request,
        ReportConnectionDefinition connection,
        ReportDataSourceDefinition dataSource)
    {
        foreach (var pair in connection.Headers)
        {
            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }

        foreach (var pair in dataSource.Options)
        {
            if (pair.Key.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation(pair.Key["header:".Length..], pair.Value);
            }
        }
    }

    public static async ValueTask<string> ReadJsonResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"HTTP request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        return json;
    }

    private static string ResolveResourcePath(
        ReportDataSourceDefinition dataSource,
        ReportDataSetDefinition dataSet)
    {
        if (!string.IsNullOrWhiteSpace(dataSet.Query))
        {
            return dataSet.Query;
        }

        var resourcePath = ReportDataProviderSupport.GetOption(dataSource, "resourcePath");
        if (!string.IsNullOrWhiteSpace(resourcePath))
        {
            return resourcePath;
        }

        throw new InvalidOperationException(
            $"Data source '{dataSource.Id}' requires 'query' or 'resourcePath' for HTTP-backed providers.");
    }

    private static Uri? ResolveBaseAddress(ReportConnectionDefinition connection, HttpClient httpClient)
    {
        if (!string.IsNullOrWhiteSpace(connection.BaseAddress)
            && Uri.TryCreate(connection.BaseAddress, UriKind.Absolute, out var baseAddress))
        {
            return baseAddress;
        }

        return httpClient.BaseAddress;
    }

    private static HttpMethod ResolveHttpMethod(
        ReportDataSourceDefinition dataSource,
        HttpMethod defaultMethod)
    {
        var methodText = ReportDataProviderSupport.GetOption(dataSource, "method");
        if (string.IsNullOrWhiteSpace(methodText))
        {
            return defaultMethod;
        }

        return new HttpMethod(methodText);
    }

    private static Uri AppendQueryString(
        Uri uri,
        IReadOnlyDictionary<string, object?> parameters,
        ReportDataSourceDefinition dataSource)
    {
        if (parameters.Count == 0 || !IsHttpMethod(ResolveHttpMethod(dataSource, HttpMethod.Get), HttpMethod.Get))
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var queryBuilder = new List<string>();
        if (!string.IsNullOrWhiteSpace(builder.Query))
        {
            queryBuilder.Add(builder.Query.TrimStart('?'));
        }

        foreach (var pair in parameters)
        {
            queryBuilder.Add(
                Uri.EscapeDataString(pair.Key)
                + "="
                + Uri.EscapeDataString(Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
        }

        builder.Query = string.Join("&", queryBuilder.Where(static item => !string.IsNullOrWhiteSpace(item)));
        return builder.Uri;
    }

    private static bool IsHttpMethod(HttpMethod candidate, HttpMethod expected)
    {
        return string.Equals(candidate.Method, expected.Method, StringComparison.OrdinalIgnoreCase);
    }
}
