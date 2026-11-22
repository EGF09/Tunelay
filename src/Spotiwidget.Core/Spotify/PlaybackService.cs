namespace Spotiwidget.Core.Spotify;

public enum PollMode
{
    /// <summary>Overlay visible: poll every 3 s.</summary>
    Visible,

    /// <summary>Card hidden or a fullscreen app in front: poll every 15 s.</summary>
    Background,
}

/// <summary>Polls playback state and runs player commands. Events fire on a thread-pool thread.</summary>
public sealed class PlaybackService(ISpotifyApi api, Func<bool> isSignedIn) : IAsyncDisposable
{
    private static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BackgroundInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(350);

    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private string? _lastError;
    private PollMode _mode = PollMode.Visible;

    private string? _likedTrackId;

    public PlaybackState? Current { get; private set; }

    /// <summary>Whether the current track is in Liked Songs; null while unknown.</summary>
    public bool? IsLiked { get; private set; }

    public event Action<PlaybackState?>? StateChanged;

    public event Action<bool?>? LikedChanged;

    public event Action<string>? Error;

    /// <summary>Short confirmation messages (e.g. "added to Liked Songs").</summary>
    public event Action<string>? Info;

    public PollMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            if (value == PollMode.Visible)
            {
                RefreshSoon();
            }
        }
    }

    public void Start() => _loop ??= Task.Run(() => LoopAsync(_cts.Token));

    public void RefreshSoon()
    {
        if (_wake.CurrentCount == 0)
        {
            try
            {
                _wake.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public Task TogglePlayPauseAsync() => RunAsync(async () =>
    {
        var playing = Current?.IsPlaying == true;
        if (playing)
        {
            await api.PauseAsync().ConfigureAwait(false);
        }
        else
        {
            await api.PlayAsync().ConfigureAwait(false);
        }

        if (Current is { } c)
        {
            Publish(c with { IsPlaying = !playing });
        }
    });

    public Task NextAsync() => RunAsync(() => api.NextAsync());

    public Task PreviousAsync() => RunAsync(() => api.PreviousAsync());

    public Task ChangeVolumeAsync(int delta) => RunAsync(async () =>
    {
        var now = Current?.Device?.VolumePercent ?? 50;
        var target = Math.Clamp(now + delta, 0, 100);
        await api.SetVolumeAsync(target).ConfigureAwait(false);
        if (Current is { Device: { } d } c)
        {
            Publish(c with { Device = d with { VolumePercent = target } });
        }
    });

    public Task SetVolumeAsync(int percent) => RunAsync(async () =>
    {
        var target = Math.Clamp(percent, 0, 100);
        await api.SetVolumeAsync(target).ConfigureAwait(false);
        if (Current is { Device: { } d } c)
        {
            Publish(c with { Device = d with { VolumePercent = target } });
        }
    });

    /// <summary>Turns shuffle on or off for the current playback.</summary>
    public Task ToggleShuffleAsync() => RunAsync(async () =>
    {
        var on = Current?.ShuffleState != true;
        await api.SetShuffleAsync(on).ConfigureAwait(false);
        if (Current is { } c)
        {
            Publish(c with { ShuffleState = on });
        }

        Info?.Invoke(on ? "Karışık çalma açık" : "Karışık çalma kapalı");
    });

    /// <summary>Cycles repeat: off → playlist → song → off.</summary>
    public Task CycleRepeatAsync() => RunAsync(async () =>
    {
        var next = RepeatMode.Next(Current?.RepeatState);
        await api.SetRepeatAsync(next).ConfigureAwait(false);
        if (Current is { } c)
        {
            Publish(c with { RepeatState = next });
        }

        Info?.Invoke(next switch
        {
            RepeatMode.Context => "Tekrarlama: liste",
            RepeatMode.Track => "Tekrarlama: bu şarkı",
            _ => "Tekrarlama kapalı",
        });
    });

    /// <summary>Adds the current track to Liked Songs, or removes it if it is already there.</summary>
    public Task ToggleLikeAsync() => RunAsync(async () =>
    {
        if (Current?.Item is not { } track)
        {
            Error?.Invoke("Şu an çalan bir şarkı yok.");
            return;
        }

        var liked = IsLiked == true;
        if (liked)
        {
            await api.RemoveFromLibraryAsync(track).ConfigureAwait(false);
        }
        else
        {
            await api.SaveToLibraryAsync(track).ConfigureAwait(false);
        }

        SetLiked(track.Id, !liked);
        Info?.Invoke(liked ? $"“{track.Name}” beğenilenlerden çıkarıldı" : $"♥ “{track.Name}” beğenilenlere eklendi");
    });

    private async Task CheckLikedAsync(Track track)
    {
        try
        {
            SetLiked(track.Id, await api.IsInLibraryAsync(track).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is SpotifyApiException or HttpRequestException or TaskCanceledException)
        {
            // Unknown (e.g. an older login without library scope); the heart stays neutral.
        }
    }

    private void SetLiked(string trackId, bool? liked)
    {
        if (trackId != _likedTrackId)
        {
            return; // the track changed meanwhile
        }

        IsLiked = liked;
        LikedChanged?.Invoke(liked);
    }

    private async Task RunAsync(Func<Task> command)
    {
        try
        {
            await command().ConfigureAwait(false);
        }
        catch (SpotifyApiException ex)
        {
            Error?.Invoke(ex.Message);
        }
        catch (HttpRequestException)
        {
            Error?.Invoke("Spotify'a ulaşılamadı.");
        }

        // Spotify takes a moment to reflect a change.
        _ = Task.Delay(SettleDelay).ContinueWith(_ => RefreshSoon(), TaskScheduler.Default);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (isSignedIn())
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }

            try
            {
                var interval = _mode == PollMode.Visible ? VisibleInterval : BackgroundInterval;
                await _wake.WaitAsync(NextPollDelay(Current, interval), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static readonly TimeSpan MinPollDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SongEndSlack = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Waits the normal interval, but wakes just after the current song should end, so a
    /// new song is noticed within about a second even on the slow (in-game) 15 s interval.
    /// </summary>
    internal static TimeSpan NextPollDelay(PlaybackState? state, TimeSpan interval)
    {
        if (state is not { IsPlaying: true, Item: { } track, ProgressMs: { } progress })
        {
            return interval;
        }

        var untilEnd = TimeSpan.FromMilliseconds(Math.Max(0, track.DurationMs - progress)) + SongEndSlack;
        var delay = untilEnd < interval ? untilEnd : interval;
        return delay < MinPollDelay ? MinPollDelay : delay;
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        try
        {
            var state = await api.GetPlaybackAsync(ct).ConfigureAwait(false);
            _lastError = null;
            if (!Equivalent(state, Current))
            {
                Publish(state);
            }
            else
            {
                Current = state;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is SpotifyApiException or HttpRequestException or TaskCanceledException)
        {
            var message = ex is SpotifyApiException api ? api.Message : "Spotify'a ulaşılamadı.";
            if (message != _lastError)
            {
                _lastError = message;
                Error?.Invoke(message);
            }
        }
    }

    private void Publish(PlaybackState? state)
    {
        Current = state;
        StateChanged?.Invoke(state);

        var track = state?.Item;
        if (track?.Id != _likedTrackId)
        {
            _likedTrackId = track?.Id;
            IsLiked = null;
            LikedChanged?.Invoke(null);
            if (track is not null)
            {
                _ = CheckLikedAsync(track);
            }
        }
    }

    /// <summary>Progress alone changing should not redraw the UI.</summary>
    private static bool Equivalent(PlaybackState? a, PlaybackState? b) =>
        a?.Item?.Id == b?.Item?.Id
        && a?.IsPlaying == b?.IsPlaying
        && a?.Device?.VolumePercent == b?.Device?.VolumePercent
        && a?.ShuffleState == b?.ShuffleState
        && a?.RepeatState == b?.RepeatState;

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _cts.Dispose();
    }
}
