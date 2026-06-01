using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace ReControl.Desktop.Services;

/// <summary>
/// Fetches ephemeral ICE server credentials from the backend's
/// /turn_credentials endpoint. Falls back to STUN-only when the backend is
/// unreachable so same-LAN peers still connect.
/// </summary>
public sealed class TurnCredentialsService
{
    private static readonly List<RTCIceServer> FallbackIceServers = new()
    {
        new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
    };

    private readonly ApiClient _apiClient;
    private readonly LogService _log;

    public TurnCredentialsService(ApiClient apiClient, LogService log)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<List<RTCIceServer>> FetchAsync()
    {
        try
        {
            var response = await _apiClient.GetAsync("/turn-credentials");
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning($"[TurnCredentials] backend returned {response.StatusCode}, using STUN-only fallback");
                return FallbackIceServers;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!EnvelopeReader.TryGetData(doc.RootElement, out var data)
                || !data.TryGetProperty("ice_servers", out var iceServersEl)
                || iceServersEl.ValueKind != JsonValueKind.Array)
            {
                _log.Warning("[TurnCredentials] response missing data.ice_servers array, using STUN-only fallback");
                return FallbackIceServers;
            }

            var result = new List<RTCIceServer>();
            foreach (var entry in iceServersEl.EnumerateArray())
            {
                if (!entry.TryGetProperty("urls", out var urlsEl)) continue;

                string? username = entry.TryGetProperty("username", out var u) ? u.GetString() : null;
                string? credential = entry.TryGetProperty("credential", out var c) ? c.GetString() : null;

                // SIPSorcery's RTCIceServer.urls is a single string, but Cloudflare
                // returns an array. Expand into one RTCIceServer per URL, sharing
                // username/credential across them.
                if (urlsEl.ValueKind == JsonValueKind.String)
                {
                    AddIceServer(result, urlsEl.GetString(), username, credential);
                }
                else if (urlsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var urlEl in urlsEl.EnumerateArray())
                    {
                        AddIceServer(result, urlEl.GetString(), username, credential);
                    }
                }
            }

            if (result.Count == 0)
            {
                _log.Warning("[TurnCredentials] no usable ICE servers in response, using STUN-only fallback");
                return FallbackIceServers;
            }

            _log.Info($"[TurnCredentials] fetched {result.Count} ICE servers");
            return result;
        }
        catch (Exception ex)
        {
            _log.Warning($"[TurnCredentials] fetch failed: {ex.GetType().Name}: {ex.Message}, using STUN-only fallback");
            return FallbackIceServers;
        }
    }

    private static void AddIceServer(List<RTCIceServer> list, string? url, string? username, string? credential)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var server = new RTCIceServer { urls = url };
        if (!string.IsNullOrEmpty(username)) server.username = username;
        if (!string.IsNullOrEmpty(credential)) server.credential = credential;
        list.Add(server);
    }
}
