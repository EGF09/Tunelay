using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spotiwidget.Core.Spotify;

/// <summary>OAuth 2.0 Authorization Code + PKCE for Spotify (no client secret).</summary>
public static class PkceAuth
{
    public const string RedirectUri = "http://127.0.0.1:5543/callback";
    public const string AuthorizeEndpoint = "https://accounts.spotify.com/authorize";
    public const string TokenEndpoint = "https://accounts.spotify.com/api/token";

    public static readonly string[] Scopes =
    [
        "user-read-playback-state",
        "user-modify-playback-state",
        "user-read-currently-playing",
        "playlist-read-private",
        "playlist-modify-private",
        "playlist-modify-public",
        "user-library-read",
        "user-library-modify",
    ];

    /// <summary>A 64-char verifier from the RFC 7636 unreserved alphabet.</summary>
    public static string GenerateVerifier() => Base64Url.Encode(RandomNumberGenerator.GetBytes(48));

    public static string ComputeChallenge(string verifier) =>
        Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string GenerateState() => Base64Url.Encode(RandomNumberGenerator.GetBytes(16));

    public static string BuildAuthorizeUrl(string clientId, string challenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["state"] = state,
            ["scope"] = string.Join(' ', Scopes),
        };
        return AuthorizeEndpoint + "?" + string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }
}

/// <summary>Talks to the Spotify token endpoint.</summary>
public sealed class TokenEndpointClient(HttpClient http, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<TokenSet> ExchangeCodeAsync(string clientId, string code, string verifier, CancellationToken ct) =>
        PostAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = PkceAuth.RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }, previousRefreshToken: null, ct);

    public Task<TokenSet> RefreshAsync(string clientId, string refreshToken, CancellationToken ct) =>
        PostAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }, refreshToken, ct);

    private async Task<TokenSet> PostAsync(Dictionary<string, string> form, string? previousRefreshToken, CancellationToken ct)
    {
        using var response = await http.PostAsync(PkceAuth.TokenEndpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            if ((int)status >= 500 || status == HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException($"Spotify giriş sunucusu yanıt vermiyor ({(int)status}).", null, status);
            }

            var error = ReadError(body);
            var detail = error is null ? "" : $": {error}";
            throw new SpotifyAuthException($"Token isteği başarısız ({(int)status}){detail}", invalidGrant: error == "invalid_grant");
        }

        var dto = JsonSerializer.Deserialize<TokenResponse>(body)
                  ?? throw new SpotifyAuthException("Boş token yanıtı.");
        // Spotify may omit refresh_token on refresh; keep the previous one then.
        var refresh = dto.RefreshToken ?? previousRefreshToken
                      ?? throw new SpotifyAuthException("Yanıtta refresh token yok.");
        return new TokenSet(dto.AccessToken, refresh, _clock.GetUtcNow().AddSeconds(dto.ExpiresIn), dto.Scope);
    }

    /// <summary>The OAuth "error" code, or null when the body isn't an OAuth error (proxy or captive-portal page).</summary>
    private static string? ReadError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object
                   && json.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);
}

public sealed class SpotifyAuthException(string message, bool invalidGrant = false) : Exception(message)
{
    /// <summary>Spotify revoked or expired the grant. Only this ends a saved session.</summary>
    public bool InvalidGrant { get; } = invalidGrant;
}

/// <summary>Runs the browser login: opens the authorize URL and waits on the loopback redirect.</summary>
public static class LoopbackLogin
{
    public static async Task<(string Code, string Verifier)> RunAsync(string clientId, Action<string> openBrowser, CancellationToken ct)
    {
        var verifier = PkceAuth.GenerateVerifier();
        var state = PkceAuth.GenerateState();

        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:5543/callback/");
        listener.Start();

        openBrowser(PkceAuth.BuildAuthorizeUrl(clientId, PkceAuth.ComputeChallenge(verifier), state));

        using var reg = ct.Register(listener.Stop);
        while (true)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            var q = ctx.Request.QueryString;
            var ours = q["state"] == state;
            var ok = ours && !string.IsNullOrEmpty(q["code"]);
            await RespondAsync(ctx, ok
                ? "Spotiwidget bağlandı. Bu sekmeyi kapatabilirsiniz."
                : "Giriş başarısız: " + (q["error"] ?? "geçersiz yanıt")).ConfigureAwait(false);

            if (ok)
            {
                return (q["code"]!, verifier);
            }

            // Only the redirect we started may end the login; any local page can hit this port.
            if (ours && q["error"] is { } error)
            {
                throw new SpotifyAuthException("Spotify girişi reddedildi: " + error);
            }
        }
    }

    private static async Task RespondAsync(HttpListenerContext ctx, string message)
    {
        var html = $"<!doctype html><meta charset=utf-8><title>Spotiwidget</title><body style=\"font-family:sans-serif;background:#121212;color:#eee;display:grid;place-items:center;height:100vh\"><p>{WebUtility.HtmlEncode(message)}</p>";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        ctx.Response.Close();
    }
}
