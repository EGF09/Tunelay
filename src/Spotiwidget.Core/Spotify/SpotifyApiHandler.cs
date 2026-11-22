using System.Net;
using System.Net.Http.Headers;

namespace Spotiwidget.Core.Spotify;

/// <summary>
/// Sends Web API requests one at a time, adds the bearer token,
/// refreshes once on 401 and honors Retry-After on 429.
/// </summary>
public sealed class SpotifyApiHandler(IAccessTokenProvider tokens, Func<TimeSpan, CancellationToken, Task>? delay = null)
    : DelegatingHandler
{
    public const int MaxRateLimitRetries = 3;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await _queue.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var contentType = request.Content?.Headers.ContentType;

            var token = await tokens.GetAccessTokenAsync(ct).ConfigureAwait(false)
                        ?? throw new SpotifyApiException(HttpStatusCode.Unauthorized, "NOT_SIGNED_IN", "Spotify'a bağlı değil.");

            var refreshed = false;
            for (var attempt = 0; ; attempt++)
            {
                using var copy = Clone(request, body, contentType, token);
                var response = await base.SendAsync(copy, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                {
                    response.Dispose();
                    refreshed = true;
                    token = await tokens.RefreshAccessTokenAsync(ct).ConfigureAwait(false)
                            ?? throw new SpotifyApiException(HttpStatusCode.Unauthorized, "NOT_SIGNED_IN", "Oturum süresi doldu, tekrar bağlanın.");
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRateLimitRetries)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1 << attempt);
                    response.Dispose();
                    await _delay(wait > MaxRetryAfter ? MaxRetryAfter : wait, ct).ConfigureAwait(false);
                    continue;
                }

                return response;
            }
        }
        finally
        {
            _queue.Release();
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body, MediaTypeHeaderValue? contentType, string token)
    {
        var copy = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        copy.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            copy.Content = new ByteArrayContent(body);
            copy.Content.Headers.ContentType = contentType;
        }

        return copy;
    }
}

public sealed class SpotifyApiException(HttpStatusCode status, string? reason, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>Spotify's player error reason, e.g. NO_ACTIVE_DEVICE or PREMIUM_REQUIRED.</summary>
    public string? Reason { get; } = reason;
}
