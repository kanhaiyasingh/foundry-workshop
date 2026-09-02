using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure.Core;

namespace FoundryWorkshop.Shared;

public sealed class FoundryRestClient : IDisposable
{
    public const string FoundryScope = "https://ai.azure.com/.default";
    public const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";
    public const string ArmScope = "https://management.azure.com/.default";

    private readonly WorkshopConfig _config;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public FoundryRestClient(
        WorkshopConfig config,
        TokenCredential credential,
        HttpClient httpClient)
    {
        _config = config;
        _credential = credential;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    public Task<JsonDocument> CreateResponseAsync(object body, CancellationToken cancellationToken = default) =>
        SendJsonAsync(
            HttpMethod.Post,
            new Uri($"{_config.ProjectEndpoint.TrimEnd('/')}/openai/v1/responses"),
            body,
            FoundryScope,
            cancellationToken);

    public Task<JsonDocument> SendProjectJsonAsync(
        HttpMethod method,
        string relativePath,
        object? body = null,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(
            method,
            new Uri($"{_config.ProjectEndpoint.TrimEnd('/')}/{relativePath.TrimStart('/')}"),
            body,
            FoundryScope,
            cancellationToken);

    public Task<JsonDocument> SendProjectJsonAsync(
        HttpMethod method,
        string relativePath,
        object? body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(
            method,
            new Uri($"{_config.ProjectEndpoint.TrimEnd('/')}/{relativePath.TrimStart('/')}"),
            body,
            FoundryScope,
            cancellationToken,
            headers);

    public Task<JsonDocument> SendArmJsonAsync(
        HttpMethod method,
        Uri uri,
        object? body = null,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync(method, uri, body, ArmScope, cancellationToken);

    public async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        Uri uri,
        object? body,
        string scope,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = await CreateRequestAsync(method, uri, scope, cancellationToken);
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonHelpers.Web),
                Encoding.UTF8,
                "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{method} {uri} returned {(int)response.StatusCode} ({response.ReasonPhrase}). {payload}",
                null,
                response.StatusCode);
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
    }

    public async IAsyncEnumerable<string> StreamResponseTextAsync(
        object body,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var uri = new Uri($"{_config.ProjectEndpoint.TrimEnd('/')}/openai/v1/responses");
        using var request = await CreateRequestAsync(HttpMethod.Post, uri, FoundryScope, cancellationToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonHelpers.Web),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Streaming response returned {(int)response.StatusCode}. {error}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[6..];
            if (data == "[DONE]")
            {
                yield break;
            }

            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            if (root.TryGetProperty("type", out var type) &&
                type.GetString() == "response.output_text.delta" &&
                root.TryGetProperty("delta", out var delta))
            {
                yield return delta.GetString() ?? string.Empty;
            }
        }
    }

    public async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        Uri uri,
        string scope,
        CancellationToken cancellationToken = default)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            cancellationToken);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
