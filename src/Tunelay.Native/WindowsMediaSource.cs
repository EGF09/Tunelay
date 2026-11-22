using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Tunelay.Core.Playback;
using Tunelay.Core.Spotify;
using Windows.Media;
using Windows.Media.Control;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace Tunelay.Native;

/// <summary>
/// Spotify without an account, through the Windows media session. Event-driven, and only ever
/// Spotify's session: a browser's video is never shown or controlled.
/// </summary>
public sealed class WindowsMediaSource(ISpotifyLauncher launcher, Func<bool> enabled) : IPlaybackSource
{
    private const string ProcessName = "Spotify";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _refresh = new(1, 1);
    private SessionManager? _manager;
    private Session? _session;
    private volatile bool _disposed;
    private int _starting;

    private static string ArtFolder => Path.Combine(Path.GetTempPath(), "Tunelay-art");

    public event Action? NotInstalled;

    public SourceCapabilities Capabilities => new(Account: false, Volume: Current?.Device?.VolumePercent is not null);

    public PlaybackState? Current { get; private set; }

    public bool? IsLiked => null;

    public PollMode Mode { get; set; }

    public event Action<PlaybackState?>? StateChanged;

    public event Action<bool?>? LikedChanged { add { } remove { } }

    public event Action<string>? Error;

    public event Action<string>? Info;

    public void Start() => _ = InitializeAsync();

    private async Task InitializeAsync()
    {
        try
        {
            _manager = await SessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => RefreshSoon();
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (enabled())
            {
                Error?.Invoke("Windows medya kontrolüne ulaşılamadı. Hesap bağlayarak kullanabilirsiniz.");
            }
        }
    }

    public void RefreshSoon()
    {
        if (!_disposed && enabled())
        {
            _ = RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (!enabled())
        {
            return;
        }

        await _refresh.WaitAsync().ConfigureAwait(false);
        try
        {
            var session = _manager?.GetSessions()
                .FirstOrDefault(s => s.SourceAppUserModelId.Contains(ProcessName, StringComparison.OrdinalIgnoreCase));
            Watch(session);
            Publish(session is null ? null : await ReadAsync(session).ConfigureAwait(false));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Spotify closed while being read; the next event reads again.
        }
        finally
        {
            _refresh.Release();
        }
    }

    private void Watch(Session? session)
    {
        if (ReferenceEquals(session, _session))
        {
            return;
        }

        if (_session is { } old)
        {
            try
            {
                old.MediaPropertiesChanged -= OnMediaChanged;
                old.PlaybackInfoChanged -= OnPlaybackChanged;
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                // Spotify quit and took the session with it; it must still be replaced below.
            }
        }

        _session = session;
        if (session is not null)
        {
            session.MediaPropertiesChanged += OnMediaChanged;
            session.PlaybackInfoChanged += OnPlaybackChanged;
        }
    }

    private void OnMediaChanged(Session sender, MediaPropertiesChangedEventArgs args) => RefreshSoon();

    private void OnPlaybackChanged(Session sender, PlaybackInfoChangedEventArgs args) => RefreshSoon();

    private async Task<PlaybackState> ReadAsync(Session session)
    {
        var info = session.GetPlaybackInfo();
        var media = await session.TryGetMediaPropertiesAsync();
        Track? track = null;
        if (!string.IsNullOrEmpty(media?.Title))
        {
            var art = await SaveArtAsync(media).ConfigureAwait(false);
            var id = "local:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(media.Title + "\n" + media.Artist)))[..16];
            track = new Track(id, "", media.Title, 0,
                string.IsNullOrEmpty(media.Artist) ? [] : [new Artist(media.Artist)],
                art is null ? null : new Album(media.AlbumTitle ?? "", [new Image(new Uri(art).AbsoluteUri, 300, 300)]));
        }

        var repeat = info.AutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.List => RepeatMode.Context,
            MediaPlaybackAutoRepeatMode.Track => RepeatMode.Track,
            _ => RepeatMode.Off,
        };
        return new PlaybackState(
            info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            info.IsShuffleActive == true,
            null,
            new Device(null, Environment.MachineName, true, AppVolume.Get(ProcessName), "Computer"),
            track,
            repeat);
    }

    private async Task<string?> SaveArtAsync(GlobalSystemMediaTransportControlsSessionMediaProperties media)
    {
        if (media.Thumbnail is null)
        {
            return null;
        }

        try
        {
            using var source = await media.Thumbnail.OpenReadAsync();
            using var stream = source.AsStreamForRead();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory).ConfigureAwait(false);
            var bytes = memory.ToArray();

            var folder = Directory.CreateDirectory(ArtFolder).FullName;
            var path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(bytes))[..20] + ".img");
            if (!File.Exists(path))
            {
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                DeleteCachedArt(keep: path);
            }

            return path;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Removes covers this class cached. One still shown is locked by WPF and goes on a later call.
    /// </summary>
    public static void DeleteCachedArt(string? keep = null)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(ArtFolder, "*.img").Where(f => f != keep))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No cover was ever cached.
        }
    }

    private void Publish(PlaybackState? state)
    {
        var before = Current;
        Current = state;
        if (before?.Item?.Id != state?.Item?.Id
            || before?.Item?.ImageUrl != state?.Item?.ImageUrl
            || before?.IsPlaying != state?.IsPlaying
            || before?.ShuffleState != state?.ShuffleState
            || before?.RepeatState != state?.RepeatState
            || before?.Device?.VolumePercent != state?.Device?.VolumePercent)
        {
            StateChanged?.Invoke(state);
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_session is { } session)
        {
            await RunAsync(() => session.TryTogglePlayPauseAsync().AsTask()).ConfigureAwait(false);
        }
        else
        {
            await StartSpotifyAndPlayAsync().ConfigureAwait(false);
        }
    }

    private async Task StartSpotifyAndPlayAsync()
    {
        if (Interlocked.Exchange(ref _starting, 1) == 1)
        {
            return;
        }

        try
        {
            // A running Spotify is only waited for: launching again pulls its window over the game.
            if (!launcher.IsRunning && !launcher.TryLaunch())
            {
                Error?.Invoke("Spotify bu bilgisayarda bulunamadı. Tepsi menüsü → Spotify'ı indir.");
                NotInstalled?.Invoke();
                return;
            }

            Info?.Invoke("Spotify açılıyor…");
            var until = DateTime.UtcNow + StartTimeout;
            while (!_disposed && DateTime.UtcNow < until)
            {
                await Task.Delay(500).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
                if (_session is { } session)
                {
                    await RunAsync(() => session.TryPlayAsync().AsTask()).ConfigureAwait(false);
                    return;
                }
            }

            Error?.Invoke("Spotify açıldı ama çalınacak şarkı yok. Spotify'da bir şarkı başlatın.");
        }
        finally
        {
            Interlocked.Exchange(ref _starting, 0);
        }
    }

    public Task NextAsync() => WithSessionAsync(s => s.TrySkipNextAsync().AsTask());

    public Task PreviousAsync() => WithSessionAsync(s => s.TrySkipPreviousAsync().AsTask());

    public Task ToggleShuffleAsync()
    {
        var on = Current?.ShuffleState != true;
        return WithSessionAsync(s => s.TryChangeShuffleActiveAsync(on).AsTask(), on ? "Karışık çalma açık" : "Karışık çalma kapalı");
    }

    public Task CycleRepeatAsync()
    {
        var (mode, message) = RepeatMode.Next(Current?.RepeatState) switch
        {
            RepeatMode.Context => (MediaPlaybackAutoRepeatMode.List, "Tekrarlama: liste"),
            RepeatMode.Track => (MediaPlaybackAutoRepeatMode.Track, "Tekrarlama: bu şarkı"),
            _ => (MediaPlaybackAutoRepeatMode.None, "Tekrarlama kapalı"),
        };
        return WithSessionAsync(s => s.TryChangeAutoRepeatModeAsync(mode).AsTask(), message);
    }

    public Task ChangeVolumeAsync(int delta) => SetVolumeAsync((AppVolume.Get(ProcessName) ?? 100) + delta);

    public Task SetVolumeAsync(int percent)
    {
        var target = Math.Clamp(percent, 0, 100);
        if (!AppVolume.Set(ProcessName, target))
        {
            Error?.Invoke("Ses, Spotify çalmaya başlayınca ayarlanabilir.");
        }
        else if (Current is { Device: { } device } state)
        {
            Publish(state with { Device = device with { VolumePercent = target } });
        }

        return Task.CompletedTask;
    }

    public Task ToggleLikeAsync() => Task.CompletedTask; // needs an account; the router says so

    private Task WithSessionAsync(Func<Session, Task<bool>> command, string? done = null)
    {
        if (_session is { } session)
        {
            return RunAsync(() => command(session), done);
        }

        Error?.Invoke("Spotify açık değil. Oynat'a basınca açılır.");
        return Task.CompletedTask;
    }

    private async Task RunAsync(Func<Task<bool>> command, string? done = null)
    {
        try
        {
            if (!await command().ConfigureAwait(false))
            {
                Error?.Invoke("Spotify bu komutu kabul etmedi.");
                return;
            }

            if (done is not null)
            {
                Info?.Invoke(done);
            }

            await Task.Delay(400).ConfigureAwait(false); // the mixer session appears just after playback starts
            RefreshSoon();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Error?.Invoke("Spotify'a ulaşılamadı.");
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is COMException or IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException;

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        Watch(null);
        return ValueTask.CompletedTask;
    }
}
