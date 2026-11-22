using System.Net;
using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

public class SpotifyStartTests
{
    private sealed class FakeLauncher(bool running = false, bool installed = true) : ISpotifyLauncher
    {
        public int Launches;

        public bool IsRunning => running;

        public bool TryLaunch()
        {
            Interlocked.Increment(ref Launches);
            return installed;
        }
    }

    /// <summary>A tiny Spotify: this PC appears after some device polls.</summary>
    private sealed class FakeSpotify : HttpMessageHandler
    {
        private readonly object _gate = new();
        private int _polls;

        public int PcAppearsAfter { get; init; } = 3;

        public bool PhoneListed { get; init; }

        public bool PhoneActive { get; init; }

        public int FailuresOnNewDevice { get; set; }

        public List<(string Path, string Body)> Accepted { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            await Task.Yield();
            lock (_gate)
            {
                var path = request.RequestUri!.PathAndQuery;
                if (path.EndsWith("/devices"))
                {
                    var devices = new List<string>();
                    if (PhoneListed)
                    {
                        devices.Add("{\"id\":\"phone\",\"name\":\"Phone\",\"type\":\"Smartphone\",\"is_active\":" + (PhoneActive ? "true" : "false") + "}");
                    }

                    if (++_polls > PcAppearsAfter)
                    {
                        devices.Add("{\"id\":\"pc\",\"name\":\"" + Environment.MachineName + "\",\"type\":\"Computer\",\"is_active\":false}");
                    }

                    return TokenTests.Json("{\"devices\":[" + string.Join(',', devices) + "]}");
                }

                if (!path.Contains("device_id="))
                {
                    return TokenTests.Json("""{"error":{"status":404,"reason":"NO_ACTIVE_DEVICE"}}""", HttpStatusCode.NotFound);
                }

                if (FailuresOnNewDevice > 0)
                {
                    FailuresOnNewDevice--;
                    return TokenTests.Json("""{"error":{"status":404,"message":"Device not found"}}""", HttpStatusCode.NotFound);
                }

                Accepted.Add((path, body));
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        }
    }

    private static SpotifyClient Client(FakeSpotify spotify, FakeLauncher launcher, int timeoutMs = 3000) =>
        new(new HttpClient(spotify), new SpotifyStarter(launcher, TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMilliseconds(30)));

    [Fact]
    public async Task Closed_spotify_is_launched_and_plays_on_this_pc()
    {
        var (spotify, launcher) = (new FakeSpotify(), new FakeLauncher());

        await Client(spotify, launcher).PlayContextAsync("spotify:playlist:a");

        Assert.Equal(1, launcher.Launches);
        var sent = Assert.Single(spotify.Accepted);
        Assert.Contains("device_id=pc", sent.Path);
        Assert.Contains("playlist:a", sent.Body);
    }

    [Fact]
    public async Task Running_spotify_is_waited_for_not_launched_again()
    {
        var (spotify, launcher) = (new FakeSpotify(), new FakeLauncher(running: true));

        await Client(spotify, launcher).PlayAsync();

        Assert.Equal(0, launcher.Launches); // a second launch would pull its window over the game
        Assert.Single(spotify.Accepted);
    }

    [Fact]
    public async Task A_stale_listing_of_this_pc_still_launches_spotify()
    {
        // Just after quitting, Spotify keeps listing the PC for a while.
        var (spotify, launcher) = (new FakeSpotify { PcAppearsAfter = 0 }, new FakeLauncher(running: false));

        await Client(spotify, launcher).PlayAsync();

        Assert.Equal(1, launcher.Launches);
    }

    [Fact]
    public async Task An_active_phone_is_respected()
    {
        var (spotify, launcher) = (new FakeSpotify { PhoneListed = true, PhoneActive = true }, new FakeLauncher());

        await Client(spotify, launcher).PlayAsync();

        Assert.Equal(0, launcher.Launches);
        Assert.Contains("device_id=phone", Assert.Single(spotify.Accepted).Path);
    }

    [Fact]
    public async Task Timeout_never_falls_back_to_an_idle_phone_and_allows_a_retry()
    {
        var (spotify, launcher) = (new FakeSpotify { PhoneListed = true, PcAppearsAfter = 100_000 }, new FakeLauncher());
        var client = Client(spotify, launcher, timeoutMs: 200);

        Assert.Equal("SPOTIFY_START_TIMEOUT", (await Assert.ThrowsAsync<SpotifyApiException>(() => client.PlayAsync())).Reason);
        await Assert.ThrowsAsync<SpotifyApiException>(() => client.PlayAsync());

        Assert.Equal(2, launcher.Launches);
        Assert.Empty(spotify.Accepted);
    }

    [Fact]
    public async Task Missing_spotify_fails_at_once()
    {
        var client = Client(new FakeSpotify(), new FakeLauncher(installed: false));

        var error = await Assert.ThrowsAsync<SpotifyApiException>(() => client.PlayAsync());

        Assert.Equal("SPOTIFY_NOT_INSTALLED", error.Reason);
    }

    [Fact]
    public async Task Choices_made_while_starting_launch_once_and_the_last_wins()
    {
        var (spotify, launcher) = (new FakeSpotify { PcAppearsAfter = 8 }, new FakeLauncher());
        var client = Client(spotify, launcher);

        var first = client.PlayContextAsync("spotify:playlist:a");
        await Task.Delay(40);
        var second = client.PlayContextAsync("spotify:playlist:b");

        Assert.Equal("SUPERSEDED", (await Assert.ThrowsAsync<SpotifyApiException>(() => first)).Reason);
        await second;
        Assert.Equal(1, launcher.Launches);
        Assert.Contains("playlist:b", Assert.Single(spotify.Accepted).Body);
    }

    [Fact]
    public async Task Pressing_play_while_a_mood_starts_does_not_replace_the_mood()
    {
        var (spotify, launcher) = (new FakeSpotify { PcAppearsAfter = 8 }, new FakeLauncher());
        var client = Client(spotify, launcher);

        var mood = client.PlayContextAsync("spotify:playlist:a");
        await Task.Delay(40);
        await Assert.ThrowsAsync<SpotifyApiException>(() => client.PlayAsync());
        await mood;

        Assert.Contains("playlist:a", Assert.Single(spotify.Accepted).Body);
    }

    [Fact]
    public async Task Only_play_requests_may_launch_spotify()
    {
        var launcher = new FakeLauncher();
        var client = Client(new FakeSpotify(), launcher);

        await Record.ExceptionAsync(() => client.GetPlaybackAsync());
        await Record.ExceptionAsync(() => client.PauseAsync());
        await Record.ExceptionAsync(() => client.NextAsync());
        await Record.ExceptionAsync(() => client.SetShuffleAsync(true));

        Assert.Equal(0, launcher.Launches);
    }

    [Fact]
    public async Task Shuffle_right_after_start_targets_this_pc()
    {
        var spotify = new FakeSpotify { PcAppearsAfter = 0 };

        await Client(spotify, new FakeLauncher()).SetShuffleAsync(true);

        Assert.Contains("state=true&device_id=pc", Assert.Single(spotify.Accepted).Path);
    }

    [Fact]
    public async Task A_device_that_just_appeared_gets_a_moment_to_become_ready()
    {
        var spotify = new FakeSpotify { FailuresOnNewDevice = 2 };

        await Client(spotify, new FakeLauncher()).PlayAsync();

        Assert.Single(spotify.Accepted);
    }
}
