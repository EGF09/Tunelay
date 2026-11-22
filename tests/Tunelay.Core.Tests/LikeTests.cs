using System.Net;
using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

public class LikeTests
{
    private sealed class Library
    {
        public bool Saved { get; set; }

        public TaskCompletionSource? WriteGate { get; init; }

        public TaskCompletionSource? FirstCheckGate { get; init; }

        public bool WritesFail { get; init; }

        public bool ChecksFail { get; init; }

        public List<string> Writes { get; } = [];

        private int _checks;

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me/player"))
            {
                return TokenTests.Json("""
                    {"is_playing":true,"shuffle_state":false,"progress_ms":1000,
                     "item":{"id":"abc","uri":"spotify:track:abc","name":"Song","duration_ms":200000,"artists":[]}}
                    """);
            }

            if (path.EndsWith("/contains"))
            {
                var answer = Saved;
                if (Interlocked.Increment(ref _checks) == 1 && FirstCheckGate is { } first)
                {
                    await first.Task;
                }

                return ChecksFail
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : TokenTests.Json(answer ? "[true]" : "[false]");
            }

            Writes.Add(request.Method.Method);
            if (WriteGate is { } gate)
            {
                await gate.Task;
            }

            if (WritesFail)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            Saved = request.Method == HttpMethod.Put;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    // No SpotifyApiHandler here: it would queue the requests and hide the ordering problems.
    private sealed class Fake(Library library) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            library.RespondAsync(request);
    }

    private static async Task<PlaybackService> PlayingAsync(Library library, bool waitForHeart = true)
    {
        var playback = new PlaybackService(new SpotifyClient(new HttpClient(new Fake(library))), () => true);
        playback.Start();
        await UntilAsync(() => playback.Current is not null && (!waitForHeart || playback.IsLiked is not null));
        return playback;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "timed out");
    }

    [Fact]
    public async Task Second_click_unlikes()
    {
        var library = new Library();
        await using var playback = await PlayingAsync(library);

        await playback.ToggleLikeAsync();
        await playback.ToggleLikeAsync();

        Assert.Equal(["PUT", "DELETE"], library.Writes);
        Assert.False(playback.IsLiked);
    }

    [Fact]
    public async Task Second_click_before_spotify_answers_still_unlikes()
    {
        var library = new Library { WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var playback = await PlayingAsync(library);

        var first = playback.ToggleLikeAsync();
        var second = playback.ToggleLikeAsync();
        library.WriteGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(["PUT", "DELETE"], library.Writes);
        Assert.False(library.Saved);
        Assert.False(playback.IsLiked);
    }

    [Fact]
    public async Task Heart_changes_before_spotify_answers()
    {
        var library = new Library { WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var playback = await PlayingAsync(library);

        var click = playback.ToggleLikeAsync();
        var shownAtOnce = playback.IsLiked;
        library.WriteGate.SetResult();
        await click;

        Assert.True(shownAtOnce);
    }

    [Fact]
    public async Task Click_before_the_heart_is_known_unlikes_a_liked_song()
    {
        var library = new Library { Saved = true, FirstCheckGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var playback = await PlayingAsync(library, waitForHeart: false);

        await playback.ToggleLikeAsync();
        library.FirstCheckGate.SetResult();
        await Task.Delay(100); // the late answer must not turn the heart green again

        Assert.Equal(["DELETE"], library.Writes);
        Assert.False(playback.IsLiked);
    }

    [Fact]
    public async Task Failed_write_puts_the_heart_back_and_says_so()
    {
        var library = new Library { Saved = true, WritesFail = true };
        await using var playback = await PlayingAsync(library);
        string? error = null;
        playback.Error += message => error = message;

        await playback.ToggleLikeAsync();

        Assert.True(playback.IsLiked);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Unknown_heart_is_never_guessed()
    {
        var library = new Library { Saved = true, ChecksFail = true };
        await using var playback = await PlayingAsync(library, waitForHeart: false);
        string? error = null;
        playback.Error += message => error = message;

        await playback.ToggleLikeAsync();

        Assert.Empty(library.Writes);
        Assert.NotNull(error);
    }
}
