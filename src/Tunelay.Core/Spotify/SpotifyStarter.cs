using System.Diagnostics;
using System.Net;

namespace Tunelay.Core.Spotify;

/// <summary>
/// Gets Spotify ready on this PC after the user asked for music: one launch at a time, then waits
/// for the device to be listed. When several requests arrive meanwhile, the last choice wins.
/// </summary>
public sealed class SpotifyStarter(ISpotifyLauncher launcher, TimeSpan? timeout = null, TimeSpan? interval = null)
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(20); // 6-7 s is typical
    private readonly object _gate = new();
    private Task<Device>? _run;
    private long _latest;
    private bool _musicChosen;

    internal TimeSpan Interval { get; } = interval ?? TimeSpan.FromSeconds(1.5);

    public event Action? Starting;

    public event Action? Ready;

    public event Action? NotInstalled;

    internal bool LocalAppRunning => launcher.IsRunning;

    internal bool IsStarting
    {
        get
        {
            lock (_gate)
            {
                return _run is not null;
            }
        }
    }

    /// <param name="findDevice">Returns the device to play on, or null while there is none.</param>
    /// <param name="choosesMusic">True for a playlist; a plain "play" never replaces such a choice.</param>
    /// <returns>The device, or null when a later choice replaced this one.</returns>
    internal async Task<Device?> StartAsync(Func<CancellationToken, Task<Device?>> findDevice, bool choosesMusic, CancellationToken ct)
    {
        Task<Device> run;
        long ticket = -1;
        lock (_gate)
        {
            if (_run is null)
            {
                _musicChosen = false;
                _run = RunAsync(findDevice);
            }

            if (choosesMusic || !_musicChosen)
            {
                _musicChosen |= choosesMusic;
                ticket = ++_latest;
            }

            run = _run;
        }

        var device = await run.WaitAsync(ct).ConfigureAwait(false);
        lock (_gate)
        {
            return ticket == _latest ? device : null;
        }
    }

    private async Task<Device> RunAsync(Func<CancellationToken, Task<Device?>> findDevice)
    {
        await Task.Yield(); // so _run is assigned before this can finish
        try
        {
            var wasRunning = launcher.IsRunning;
            // Launching again would pull a running Spotify's window over the game.
            if (!wasRunning && !launcher.TryLaunch())
            {
                NotInstalled?.Invoke();
                throw new SpotifyApiException(HttpStatusCode.NotFound, "SPOTIFY_NOT_INSTALLED",
                    "Spotify bu bilgisayarda bulunamadı. Tepsi menüsü → Spotify'ı indir.");
            }

            Starting?.Invoke();
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < _timeout)
            {
                await Task.Delay(Interval).ConfigureAwait(false);
                try
                {
                    if (await findDevice(CancellationToken.None).ConfigureAwait(false) is { } device)
                    {
                        Ready?.Invoke();
                        return device;
                    }
                }
                catch (Exception ex) when (SpotifyFailure.IsExpected(ex))
                {
                    // A failed look doesn't end the wait; the timeout does.
                }
            }

            throw new SpotifyApiException(HttpStatusCode.NotFound, "SPOTIFY_START_TIMEOUT", wasRunning
                ? "Spotify açık ama bu bilgisayar görünmüyor. Spotify'da aynı hesapla giriş yapıldığını kontrol edin."
                : "Spotify açıldı ama hazır olmadı. Birkaç saniye sonra tekrar deneyin.");
        }
        finally
        {
            lock (_gate)
            {
                _run = null;
            }
        }
    }
}
