using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tunelay.Core.Spotify;

public interface ISpotifyApi
{
    Task<PlaybackState?> GetPlaybackAsync(CancellationToken ct = default);

    Task PlayAsync(CancellationToken ct = default);

    Task PauseAsync(CancellationToken ct = default);

    Task NextAsync(CancellationToken ct = default);

    Task PreviousAsync(CancellationToken ct = default);

    Task SetVolumeAsync(int percent, CancellationToken ct = default);

    Task SetShuffleAsync(bool on, CancellationToken ct = default);

    /// <summary>Sets repeat to <see cref="RepeatMode.Off"/>, <see cref="RepeatMode.Context"/> or <see cref="RepeatMode.Track"/>.</summary>
    Task SetRepeatAsync(string state, CancellationToken ct = default);

    Task PlayContextAsync(string contextUri, CancellationToken ct = default);

    Task<IReadOnlyList<SimplePlaylist>> GetMyPlaylistsAsync(CancellationToken ct = default);

    Task AddToPlaylistAsync(string playlistId, string trackUri, CancellationToken ct = default);

    /// <summary>Whether the track is in the user's Liked Songs.</summary>
    Task<bool> IsInLibraryAsync(Track track, CancellationToken ct = default);

    Task SaveToLibraryAsync(Track track, CancellationToken ct = default);

    Task RemoveFromLibraryAsync(Track track, CancellationToken ct = default);
}

/// <summary>Thin typed wrapper over the Spotify Web API. Expects an HttpClient built on <see cref="SpotifyApiHandler"/>.</summary>
public sealed class SpotifyClient : ISpotifyApi
{
    public static readonly Uri BaseAddress = new("https://api.spotify.com/v1/");

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;
    private readonly SpotifyStarter? _starter;

    /// <param name="starter">When given, a play request may open Spotify on this PC.</param>
    public SpotifyClient(HttpClient http, SpotifyStarter? starter = null)
    {
        _starter = starter;
        _http = http;
        _http.BaseAddress ??= BaseAddress;
    }

    public async Task<PlaybackState?> GetPlaybackAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("me/player", ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureOkAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<PlaybackState>(Json, ct).ConfigureAwait(false);
    }

    public Task PlayAsync(CancellationToken ct = default) => WithDeviceFallbackAsync(id => Send(HttpMethod.Put, "me/player/play" + DeviceQuery(id), null, ct), choosesMusic: false, ct);

    public Task PauseAsync(CancellationToken ct = default) => Send(HttpMethod.Put, "me/player/pause", null, ct);

    public Task NextAsync(CancellationToken ct = default) => Send(HttpMethod.Post, "me/player/next", null, ct);

    public Task PreviousAsync(CancellationToken ct = default) => Send(HttpMethod.Post, "me/player/previous", null, ct);

    public Task SetVolumeAsync(int percent, CancellationToken ct = default) =>
        Send(HttpMethod.Put, $"me/player/volume?volume_percent={Math.Clamp(percent, 0, 100)}", null, ct);

    public async Task SetShuffleAsync(bool on, CancellationToken ct = default)
    {
        var path = $"me/player/shuffle?state={(on ? "true" : "false")}";
        try
        {
            await Send(HttpMethod.Put, path, null, ct).ConfigureAwait(false);
        }
        catch (SpotifyApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            // Right after a start this PC may not count as active yet. Never opens Spotify.
            var device = await FindDeviceAsync(ct).ConfigureAwait(false);
            if (device is null)
            {
                throw;
            }

            await CallOnDeviceAsync(id => Send(HttpMethod.Put, $"{path}&device_id={Uri.EscapeDataString(id!)}", null, ct), device, ct).ConfigureAwait(false);
        }
    }

    public Task SetRepeatAsync(string state, CancellationToken ct = default) =>
        Send(HttpMethod.Put, $"me/player/repeat?state={Uri.EscapeDataString(state)}", null, ct);

    public Task PlayContextAsync(string contextUri, CancellationToken ct = default) =>
        WithDeviceFallbackAsync(id => Send(HttpMethod.Put, "me/player/play" + DeviceQuery(id), new { context_uri = contextUri }, ct), choosesMusic: true, ct);

    public async Task<IReadOnlyList<SimplePlaylist>> GetMyPlaylistsAsync(CancellationToken ct = default)
    {
        var all = new List<SimplePlaylist>();
        string? next = "me/playlists?limit=50";
        while (next is not null)
        {
            using var response = await _http.GetAsync(next, ct).ConfigureAwait(false);
            await EnsureOkAsync(response, ct).ConfigureAwait(false);
            var page = await response.Content.ReadFromJsonAsync<Paging<SimplePlaylist?>>(Json, ct).ConfigureAwait(false);
            all.AddRange(page?.Items.OfType<SimplePlaylist>() ?? []);
            next = page?.Next;
        }

        return all;
    }

    public async Task AddToPlaylistAsync(string playlistId, string trackUri, CancellationToken ct = default)
    {
        var body = new { uris = new[] { trackUri } };
        try
        {
            await Send(HttpMethod.Post, $"playlists/{playlistId}/items", body, ct).ConfigureAwait(false);
        }
        catch (SpotifyApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            // Older API surface.
            await Send(HttpMethod.Post, $"playlists/{playlistId}/tracks", body, ct).ConfigureAwait(false);
        }
    }

    // Library: generic /me/library (Feb 2026 API) first, legacy /me/tracks when the new one is unavailable.

    public async Task<bool> IsInLibraryAsync(Track track, CancellationToken ct = default)
    {
        var path = $"me/library/contains?uris={Uri.EscapeDataString(track.Uri)}";
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            using var legacy = await _http.GetAsync($"me/tracks/contains?ids={Uri.EscapeDataString(track.Id)}", ct).ConfigureAwait(false);
            await EnsureOkAsync(legacy, ct).ConfigureAwait(false);
            return (await legacy.Content.ReadFromJsonAsync<bool[]>(Json, ct).ConfigureAwait(false))?.FirstOrDefault() == true;
        }

        await EnsureOkAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<bool[]>(Json, ct).ConfigureAwait(false))?.FirstOrDefault() == true;
    }

    public Task SaveToLibraryAsync(Track track, CancellationToken ct = default) => LibraryAsync(HttpMethod.Put, track, ct);

    public Task RemoveFromLibraryAsync(Track track, CancellationToken ct = default) => LibraryAsync(HttpMethod.Delete, track, ct);

    private async Task LibraryAsync(HttpMethod method, Track track, CancellationToken ct)
    {
        try
        {
            await Send(method, $"me/library?uris={Uri.EscapeDataString(track.Uri)}", null, ct).ConfigureAwait(false);
        }
        catch (SpotifyApiException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            await Send(method, $"me/tracks?ids={Uri.EscapeDataString(track.Id)}", null, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// When nothing is playing, Spotify answers NO_ACTIVE_DEVICE; retry on the active device if one
    /// appeared meanwhile, otherwise on this PC, opening Spotify here if needed.
    /// An idle phone or speaker is never started by itself.
    /// </summary>
    private async Task WithDeviceFallbackAsync(Func<string?, Task> call, bool choosesMusic, CancellationToken ct)
    {
        try
        {
            await call(null).ConfigureAwait(false);
            return;
        }
        catch (SpotifyApiException ex) when (ex.Reason == "NO_ACTIVE_DEVICE" || ex.Status == HttpStatusCode.NotFound)
        {
        }

        // While Spotify is starting, join that start, so an earlier choice can't play over this one.
        var device = _starter is { IsStarting: true } ? null : await FindDeviceAsync(ct).ConfigureAwait(false);
        if (_starter is { LocalAppRunning: false } && device is not null && IsThisPc(device))
        {
            device = null; // a stale listing: Spotify was just closed on this PC
        }

        if (device is not null)
        {
            await CallOnDeviceAsync(call, device, ct).ConfigureAwait(false);
            return;
        }

        if (_starter is null)
        {
            throw new SpotifyApiException(HttpStatusCode.NotFound, "NO_ACTIVE_DEVICE", "Bu bilgisayarda Spotify açık değil. Spotify'ı açıp tekrar deneyin.");
        }

        device = await _starter.StartAsync(FindDeviceAsync, choosesMusic, ct).ConfigureAwait(false)
                 ?? throw new SpotifyApiException(HttpStatusCode.Conflict, "SUPERSEDED", "Spotify açılıyor, son seçiminiz çalınacak.");
        await CallOnDeviceAsync(call, device, ct).ConfigureAwait(false);
    }

    /// <summary>The active device, otherwise this PC, otherwise null.</summary>
    private async Task<Device?> FindDeviceAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("me/player/devices", ct).ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);
        var list = await response.Content.ReadFromJsonAsync<DeviceList>(Json, ct).ConfigureAwait(false);
        var usable = list?.Devices.Where(d => d.Id is not null).ToList() ?? [];
        return usable.FirstOrDefault(d => d.IsActive) ?? usable.FirstOrDefault(IsThisPc);
    }

    /// <summary>A device that has just appeared can refuse the first command or two.</summary>
    private async Task CallOnDeviceAsync(Func<string?, Task> call, Device device, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await call(device.Id).ConfigureAwait(false);
                return;
            }
            catch (SpotifyApiException ex) when (
                _starter is not null && attempt < 6
                && (ex.Status == HttpStatusCode.NotFound || (int)ex.Status >= 500))
            {
                await Task.Delay(_starter.Interval, ct).ConfigureAwait(false);
            }
        }
    }

    // MachineName is cut to 15 characters on Windows, so the DNS host name is accepted too.
    private static bool IsThisPc(Device device) =>
        string.Equals(device.Type, "Computer", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(device.Name, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(device.Name, Dns.GetHostName(), StringComparison.OrdinalIgnoreCase));

    private static string DeviceQuery(string? deviceId) =>
        deviceId is null ? "" : "?device_id=" + Uri.EscapeDataString(deviceId);

    private async Task Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);
    }

    internal static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? reason = null;
        string? message = null;
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(Json, ct).ConfigureAwait(false);
            reason = envelope?.Error?.Reason;
            message = envelope?.Error?.Message;
        }
        catch (JsonException)
        {
        }

        var friendly = reason switch
        {
            "NO_ACTIVE_DEVICE" => "Bir cihazda Spotify'ı açın.",
            "PREMIUM_REQUIRED" => "Bu işlem Spotify Premium gerektirir.",
            _ when response.StatusCode == HttpStatusCode.Forbidden && message?.Contains("scope", StringComparison.OrdinalIgnoreCase) == true
                => "Bu özellik için izin yok. Ayarlar → Bağlantıyı kes → tekrar bağlanın.",
            _ when response.StatusCode == HttpStatusCode.Forbidden => "Spotify bu işleme izin vermedi" + (message is null ? "." : $": {message}"),
            _ => $"Spotify hatası ({(int)response.StatusCode})" + (message is null ? "." : $": {message}"),
        };
        throw new SpotifyApiException(response.StatusCode, reason, friendly);
    }
}
