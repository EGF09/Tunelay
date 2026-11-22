namespace Tunelay.Core.Spotify;

/// <summary>Holds the current tokens, refreshes them silently and persists every change.</summary>
public sealed class SpotifyAuthSession(
    TokenEndpointClient tokens,
    TokenStore store,
    Func<string> clientId,
    TimeProvider? clock = null) : IAccessTokenProvider
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TokenSet? _current = store.Load();

    public bool IsSignedIn => _current is not null;

    /// <summary>True when the stored grant includes <paramref name="scope"/> (false for older logins).</summary>
    public bool HasScope(string scope) => _current?.HasScope(scope) == true;

    public event Action<bool>? SignedInChanged;

    public async Task SignInAsync(Action<string> openBrowser, CancellationToken ct)
    {
        var id = clientId();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new SpotifyAuthException("Önce Spotify Client ID girin.");
        }

        var (code, verifier) = await LoopbackLogin.RunAsync(id, openBrowser, ct).ConfigureAwait(false);
        var set = await tokens.ExchangeCodeAsync(id, code, verifier, ct).ConfigureAwait(false);
        await SetAsync(set).ConfigureAwait(false);
    }

    public async Task SignOutAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _current = null;
            store.Clear();
        }
        finally
        {
            _gate.Release();
            SignedInChanged?.Invoke(false); // signed out in memory even if the file couldn't be deleted
        }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        var current = _current;
        if (current is null)
        {
            return null;
        }

        if (!current.ExpiresWithin(RefreshMargin, _clock.GetUtcNow()))
        {
            return current.AccessToken;
        }

        return await RefreshCoreAsync(current, ct).ConfigureAwait(false);
    }

    public async Task<string?> RefreshAccessTokenAsync(CancellationToken ct)
    {
        var current = _current;
        return current is null ? null : await RefreshCoreAsync(current, ct).ConfigureAwait(false);
    }

    private async Task<string?> RefreshCoreAsync(TokenSet seen, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller already refreshed while we waited.
            if (_current is null)
            {
                return null;
            }

            if (!ReferenceEquals(_current, seen))
            {
                return _current.AccessToken;
            }

            try
            {
                var fresh = await tokens.RefreshAsync(clientId(), seen.RefreshToken, ct).ConfigureAwait(false);
                _current = fresh with { Scope = fresh.Scope ?? seen.Scope };
                store.Save(_current);
                return _current.AccessToken;
            }
            catch (SpotifyAuthException ex) when (ex.InvalidGrant)
            {
                // Refresh token revoked or expired: the user must sign in again.
                _current = null;
                store.Clear();
            }
            catch (SpotifyAuthException ex)
            {
                // Anything else may be fixable or temporary: keep the saved session.
                throw new SpotifyApiException(System.Net.HttpStatusCode.BadRequest, "TOKEN_REFRESH_FAILED", ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }

        SignedInChanged?.Invoke(false);
        return null;
    }

    private async Task SetAsync(TokenSet set)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _current = set;
            store.Save(set);
        }
        finally
        {
            _gate.Release();
        }

        SignedInChanged?.Invoke(true);
    }
}
