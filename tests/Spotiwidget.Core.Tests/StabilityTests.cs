using System.Net;
using Spotiwidget.Core.Moods;
using Spotiwidget.Core.Settings;
using Spotiwidget.Core.Spotify;

namespace Spotiwidget.Core.Tests;

public class TokenRefreshFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>down</html>")]
    [InlineData(HttpStatusCode.TooManyRequests, "")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_client"}""")]
    [InlineData(HttpStatusCode.Forbidden, "<html>captive portal</html>")]
    public async Task Failure_other_than_invalid_grant_keeps_the_session(HttpStatusCode status, string body)
    {
        var store = new TokenStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin"), new XorProtector());
        store.Save(new TokenSet("stale", "still-good", DateTimeOffset.UtcNow.AddSeconds(-5)));
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }));
        var session = new SpotifyAuthSession(new TokenEndpointClient(http), store, () => "cid");

        var error = await Record.ExceptionAsync(() => session.GetAccessTokenAsync(default));

        Assert.True(session.IsSignedIn);
        Assert.Equal("still-good", store.Load()?.RefreshToken);
        Assert.True(error is HttpRequestException or SpotifyApiException, $"Callers cannot handle: {error}");
        store.Clear();
    }
}

public class FailureToleranceTests
{
    private static SpotifyClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)));

    public static TheoryData<string> Failures => new() { "offline", "timeout", "bad-json" };

    private static Exception Failure(string kind) => kind switch
    {
        "offline" => new HttpRequestException("offline"),
        "timeout" => new TaskCanceledException("timeout"),
        _ => new System.Text.Json.JsonException("bad json"),
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Player_command_reports_failure_instead_of_throwing(string kind)
    {
        await using var playback = new PlaybackService(Client(_ => throw Failure(kind)), () => true);
        string? reported = null;
        playback.Error += m => reported = m;

        Assert.Null(await Record.ExceptionAsync(playback.NextAsync));
        Assert.NotNull(reported);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Mood_switch_returns_failure_instead_of_throwing(string kind)
    {
        var dir = Directory.CreateTempSubdirectory("spw-");
        try
        {
            var settings = new SettingsStore(Path.Combine(dir.FullName, "settings.json"));
            settings.Update(s => s.MoodPlaylists["Calm"] = new PlaylistRef("p", "Calm"));
            var moods = new MoodService(Client(_ => throw Failure(kind)), settings);

            MoodResult? result = null;
            Assert.Null(await Record.ExceptionAsync(async () => result = await moods.SwitchAsync("Calm")));
            Assert.False(result!.Ok);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Poll_loop_survives_a_malformed_response()
    {
        var calls = 0;
        var client = Client(_ => Interlocked.Increment(ref calls) == 1
            ? TokenTests.Json("<html>not json</html>")
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        var playback = new PlaybackService(client, () => true);

        playback.Start();
        for (var i = 0; i < 100 && Volatile.Read(ref calls) < 2; i++)
        {
            playback.RefreshSoon();
            await Task.Delay(20);
        }

        Assert.True(Volatile.Read(ref calls) >= 2, "The loop stopped after the first bad response.");
        await playback.DisposeAsync();
    }
}

public class DeviceSelectionTests
{
    private static string Phone(bool active = false) =>
        $$"""{"id":"phone","name":"My Phone","type":"Smartphone","is_active":{{(active ? "true" : "false")}}}""";

    private static string ThisPc() =>
        $$"""{"id":"pc","name":"{{Environment.MachineName.ToLowerInvariant()}}","type":"Computer","is_active":false}""";

    private static (SpotifyClient Client, StubHandler Stub) Build(params string[] devices)
    {
        var stub = new StubHandler(r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/devices"))
            {
                return TokenTests.Json("{\"devices\":[" + string.Join(',', devices) + "]}");
            }

            return r.RequestUri.Query.Contains("device_id")
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : TokenTests.Json("""{"error":{"status":404,"reason":"NO_ACTIVE_DEVICE"}}""", HttpStatusCode.NotFound);
        });
        return (new SpotifyClient(new HttpClient(stub)), stub);
    }

    [Fact]
    public async Task Picks_this_pc_even_when_a_phone_is_listed_first()
    {
        var (client, stub) = Build(Phone(), ThisPc());

        await client.PlayAsync();

        Assert.Equal("?device_id=pc", stub.Requests[^1].RequestUri!.Query);
    }

    [Fact]
    public async Task Never_starts_an_idle_phone_by_itself()
    {
        var (client, stub) = Build(Phone());

        var error = await Record.ExceptionAsync(() => client.PlayAsync());

        Assert.Equal("NO_ACTIVE_DEVICE", Assert.IsType<SpotifyApiException>(error).Reason);
        Assert.DoesNotContain(stub.Requests, r => r.RequestUri!.Query.Contains("device_id"));
    }

    [Fact]
    public async Task Respects_a_device_that_became_active_meanwhile()
    {
        var (client, stub) = Build(ThisPc(), Phone(active: true));

        await client.PlayAsync();

        Assert.Equal("?device_id=phone", stub.Requests[^1].RequestUri!.Query);
    }
}

public class LoopbackLoginTests
{
    [Fact]
    public async Task A_request_with_the_wrong_state_cannot_cancel_the_login()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var opened = new TaskCompletionSource<string>();
        // The fake "browser" only captures the URL; nothing is opened and Spotify is never contacted.
        var login = LoopbackLogin.RunAsync("cid", url => opened.TrySetResult(url), timeout.Token);
        var state = System.Web.HttpUtility.ParseQueryString(new Uri(await opened.Task).Query)["state"]!;

        using var http = new HttpClient();
        await http.GetAsync(PkceAuth.RedirectUri + "/?error=access_denied&state=forged", timeout.Token);
        await Task.Delay(100, timeout.Token);
        Assert.False(login.IsCompleted, "A forged request ended the login.");

        await http.GetAsync(PkceAuth.RedirectUri + "/?code=abc&state=" + Uri.EscapeDataString(state), timeout.Token);
        Assert.Equal("abc", (await login).Code);
    }
}
