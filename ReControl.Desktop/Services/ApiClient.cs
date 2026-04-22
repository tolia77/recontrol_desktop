using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ReControl.Desktop.Services;

/// <summary>
/// HTTP client with automatic 401 retry via token refresh.
/// Ported from WPF ApiClient.
/// </summary>
public class ApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly Func<string?>? _getAccessToken;
    private readonly Func<Task<bool>>? _refreshTokens;
    private readonly LogService _log;

    public ApiClient(string baseUrl, LogService log, Func<string?>? getAccessToken = null, Func<Task<bool>>? refreshTokens = null)
    {
        _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _getAccessToken = getAccessToken;
        _refreshTokens = refreshTokens;
        _httpClient = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public async Task<HttpResponseMessage> GetAsync(string endpoint, IDictionary<string, string>? headers = null)
    {
        return await SendAsync(() => CreateRequestMessage(HttpMethod.Get, endpoint, null, headers));
    }

    public async Task<HttpResponseMessage> PostAsync(string endpoint, HttpContent content, IDictionary<string, string>? headers = null)
    {
        return await SendAsync(() => CreateRequestMessage(HttpMethod.Post, endpoint, content, headers));
    }

    public async Task<HttpResponseMessage> PutAsync(string endpoint, HttpContent content, IDictionary<string, string>? headers = null)
    {
        return await SendAsync(() => CreateRequestMessage(HttpMethod.Put, endpoint, content, headers));
    }

    public async Task<HttpResponseMessage> DeleteAsync(string endpoint, IDictionary<string, string>? headers = null)
    {
        return await SendAsync(() => CreateRequestMessage(HttpMethod.Delete, endpoint, null, headers));
    }

    private HttpRequestMessage CreateRequestMessage(HttpMethod method, string endpoint, HttpContent? content = null, IDictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(method, endpoint);
        if (content != null)
            request.Content = content;

        var accessToken = _getAccessToken?.Invoke();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        }

        if (headers != null)
        {
            foreach (var kv in headers)
            {
                if (!request.Headers.TryAddWithoutValidation(kv.Key, kv.Value) && request.Content != null)
                    request.Content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory)
    {
        var request = requestFactory();
        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized && _refreshTokens != null && !IsRefreshEndpoint(request))
        {
            _log.Info("[ApiClient] 401 Unauthorized, attempting token refresh...");
            response.Dispose();

            var refreshed = await _refreshTokens();
            if (refreshed)
            {
                _log.Info("[ApiClient] Token refresh succeeded, retrying request...");
                response = await _httpClient.SendAsync(requestFactory());
            }
            else
            {
                _log.Warning("[ApiClient] Token refresh failed.");
            }
        }

        return response;
    }

    private static bool IsRefreshEndpoint(HttpRequestMessage request)
    {
        var path = request.RequestUri?.OriginalString ?? string.Empty;
        return path.Contains("/auth/refresh", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _httpClient.Dispose();
}
