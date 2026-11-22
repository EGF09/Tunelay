using System.Net;
using Spotiwidget.Core.Spotify;

namespace Spotiwidget.Core.Tests;

public class SpotifyApiHandlerTests
{
    private sealed class FakeTokens : IAccessTokenProvider
    {
        public string Token { get; set; } = "t1";

        public int Refreshes { get; private set; }

        public Task<string?> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult<string?>(Token);

        public Task<string?> RefreshAccessTokenAsync(CancellationToken ct)
        {
            Refreshes++;
            Token = "t2";
            return Task.FromResult<string?>(Token);
        }
    }

    private static (HttpClient Http, StubHandler Stub, FakeTokens Tokens, List<TimeSpan> Waits) Build(
        Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var tokens = new FakeTokens();
        var count = 0;
        var stub = new StubHandler(r => respond(r, count++));
        var waits = new List<TimeSpan>();
        var handler = new SpotifyApiHandler(tokens, (t, _) =>
        {
            waits.Add(t);
            return Task.CompletedTask;
        }) { InnerHandler = stub };
        return (new HttpClient(handler) { BaseAddress = SpotifyClient.BaseAddress }, stub, tokens, waits);
    }

    [Fact]
    public async Task Retries_once_with_refreshed_token_on_401()
    {
        var (http, stub, tokens, _) = Build((_, n) => n == 0
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.NoContent));

        await new SpotifyClient(http).PlayContextAsync("spotify:playlist:x");

        Assert.Equal(1, tokens.Refreshes);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal("t1", stub.Requests[0].Headers.Authorization!.Parameter);
        Assert.Equal("t2", stub.Requests[1].Headers.Authorization!.Parameter);
        // Body survives the retry.
        Assert.Contains("spotify:playlist:x", stub.Bodies[1]);
    }

    [Fact]
    public async Task Honors_retry_after_on_429()
    {
        var (http, stub, _, waits) = Build((_, n) =>
        {
            if (n == 0)
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
                return r;
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        await new SpotifyClient(http).NextAsync();

        Assert.Equal(2, stub.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task Gives_up_after_max_rate_limit_retries()
    {
        var (http, stub, _, _) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var ex = await Assert.ThrowsAsync<SpotifyApiException>(() => new SpotifyClient(http).NextAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.Status);
        Assert.Equal(SpotifyApiHandler.MaxRateLimitRetries + 1, stub.Requests.Count);
    }

    [Fact]
    public async Task Playback_204_means_nothing_playing()
    {
        var (http, _, _, _) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        Assert.Null(await new SpotifyClient(http).GetPlaybackAsync());
    }

    [Fact]
    public async Task Parses_playback_state()
    {
        var (http, _, _, _) = Build((_, _) => TokenTests.Json("""
            {"is_playing":true,"shuffle_state":false,"progress_ms":1000,
             "device":{"id":"d1","name":"PC","is_active":true,"volume_percent":40},
             "item":{"id":"t","uri":"spotify:track:t","name":"Song","duration_ms":200000,
                     "artists":[{"name":"A"},{"name":"B"}],
                     "album":{"name":"Al","images":[{"url":"big","width":640,"height":640},{"url":"mid","width":300,"height":300}]}}}
            """));

        var state = await new SpotifyClient(http).GetPlaybackAsync();

        Assert.True(state!.IsPlaying);
        Assert.Equal(40, state.Device!.VolumePercent);
        Assert.Equal("A, B", state.Item!.ArtistNames);
        Assert.Equal("mid", state.Item.ImageUrl);
    }

    [Fact]
    public async Task Falls_back_to_first_device_when_none_active()
    {
        var (http, stub, _, _) = Build((r, _) =>
        {
            var path = r.RequestUri!.PathAndQuery;
            if (path.EndsWith("/me/player/devices"))
            {
                return TokenTests.Json("""{"devices":[{"id":"dev9","name":"PC","is_active":false,"volume_percent":50}]}""");
            }

            return path.Contains("device_id=dev9")
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : TokenTests.Json("""{"error":{"status":404,"message":"No active device","reason":"NO_ACTIVE_DEVICE"}}""", HttpStatusCode.NotFound);
        });

        await new SpotifyClient(http).PlayContextAsync("spotify:playlist:p");

        Assert.Contains(stub.Requests, r => r.RequestUri!.Query.Contains("device_id=dev9"));
    }

    [Fact]
    public async Task Maps_premium_required_to_friendly_message()
    {
        var (http, _, _, _) = Build((_, _) => TokenTests.Json(
            """{"error":{"status":403,"message":"Player command failed","reason":"PREMIUM_REQUIRED"}}""", HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<SpotifyApiException>(() => new SpotifyClient(http).PauseAsync());

        Assert.Equal("PREMIUM_REQUIRED", ex.Reason);
        Assert.Contains("Premium", ex.Message);
    }

    private static readonly Track Song = new("abc", "spotify:track:abc", "Song", 1000, [], null);

    [Fact]
    public async Task Like_uses_generic_library_endpoint()
    {
        var (http, stub, _, _) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        await new SpotifyClient(http).SaveToLibraryAsync(Song);

        var r = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Put, r.Method);
        Assert.Equal("/v1/me/library", r.RequestUri!.AbsolutePath);
        Assert.Contains("uris=spotify%3Atrack%3Aabc", r.RequestUri.Query);
    }

    [Fact]
    public async Task Like_falls_back_to_legacy_tracks_endpoint()
    {
        var (http, stub, _, _) = Build((r, _) => r.RequestUri!.AbsolutePath.EndsWith("/me/library")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK));

        await new SpotifyClient(http).RemoveFromLibraryAsync(Song);

        Assert.Equal(HttpMethod.Delete, stub.Requests[1].Method);
        Assert.Equal("/v1/me/tracks", stub.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("?ids=abc", stub.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Reads_liked_state()
    {
        var (http, _, _, _) = Build((_, _) => TokenTests.Json("[true]"));
        Assert.True(await new SpotifyClient(http).IsInLibraryAsync(Song));
    }

    [Fact]
    public async Task Missing_scope_gets_reconnect_hint()
    {
        var (http, _, _, _) = Build((_, _) => TokenTests.Json(
            """{"error":{"status":403,"message":"Insufficient client scope"}}""", HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<SpotifyApiException>(() => new SpotifyClient(http).SaveToLibraryAsync(Song));

        Assert.Contains("tekrar bağlanın", ex.Message);
    }

    [Theory]
    [InlineData(null, "context")]
    [InlineData("off", "context")]
    [InlineData("context", "track")]
    [InlineData("track", "off")]
    public void Repeat_cycles_like_spotify(string? current, string expected)
    {
        Assert.Equal(expected, RepeatMode.Next(current));
    }

    [Fact]
    public async Task Sets_repeat_state()
    {
        var (http, stub, _, _) = Build((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

        await new SpotifyClient(http).SetRepeatAsync(RepeatMode.Track);

        var r = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Put, r.Method);
        Assert.Equal("/v1/me/player/repeat?state=track", r.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Reads_repeat_state()
    {
        var (http, _, _, _) = Build((_, _) => TokenTests.Json("""{"is_playing":true,"shuffle_state":true,"repeat_state":"context","progress_ms":1,"device":null,"item":null}"""));

        var state = await new SpotifyClient(http).GetPlaybackAsync();

        Assert.Equal("context", state!.RepeatState);
        Assert.True(state.ShuffleState);
    }

    [Fact]
    public async Task Pages_through_playlists()
    {
        var (http, _, _, _) = Build((r, _) => r.RequestUri!.Query.Contains("offset=50")
            ? TokenTests.Json("""{"items":[{"id":"3","name":"C"}],"next":null}""")
            : TokenTests.Json("""{"items":[{"id":"1","name":"A"},null,{"id":"2","name":"B"}],"next":"https://api.spotify.com/v1/me/playlists?offset=50&limit=50"}"""));

        var lists = await new SpotifyClient(http).GetMyPlaylistsAsync();

        Assert.Equal(["A", "B", "C"], lists.Select(p => p.Name));
    }
}
