using System.Net;
using Tunelay.Core.Moods;
using Tunelay.Core.Settings;
using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

public class AddToPlaylistTests
{
    private static async Task<(MoodResult Result, bool Posted)> AddAsync(string? playlistJson)
    {
        var posted = false;
        var stub = new StubHandler(r =>
        {
            if (r.Method == HttpMethod.Post)
            {
                // The worst case: Spotify itself would not complain.
                posted = true;
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (r.RequestUri!.AbsolutePath.EndsWith("/me"))
            {
                return TokenTests.Json("""{"id":"user"}""");
            }

            return playlistJson is null
                ? TokenTests.Json("""{"error":{"status":404,"message":"Resource not found"}}""", HttpStatusCode.NotFound)
                : TokenTests.Json(playlistJson);
        });

        var folder = Path.Combine(Path.GetTempPath(), "tunelay-test-" + Guid.NewGuid());
        try
        {
            var settings = new SettingsStore(Path.Combine(folder, "settings.json"));
            settings.Update(s =>
            {
                s.MoodPlaylists["Calm"] = new PlaylistRef("p", "Quiet");
                s.ActiveMood = "Calm";
            });

            var moods = new MoodService(new SpotifyClient(new HttpClient(stub)), settings);
            var result = await moods.SaveTrackToActiveAsync(new Track("t", "spotify:track:t", "Song", 1000, [], null));
            return (result, posted);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("""{"owner":{"id":"someone"},"collaborative":false}""")]
    [InlineData("""{"owner":{"id":"someone"},"collaborative":false,"items":null}""")]
    public async Task Someone_elses_playlist_is_refused_with_an_error(string playlist)
    {
        var (result, posted) = await AddAsync(playlist);

        Assert.False(result.Ok);
        Assert.False(posted);
        Assert.Contains("size ait değil", result.Message);
        Assert.Contains("Quiet", result.Message);
    }

    [Theory]
    [InlineData("""{"owner":{"id":"user"},"collaborative":false,"items":{"total":3}}""")] // own
    [InlineData("""{"owner":{"id":"user"},"collaborative":false}""")] // own, contents not returned
    [InlineData("""{"owner":{"id":"someone"},"collaborative":true}""")] // collaborative
    [InlineData("""{"owner":{"id":"someone"},"collaborative":false,"items":{"total":3}}""")] // invited: Spotify shows contents only to owner and collaborators
    public async Task Editable_playlist_gets_the_song(string playlist)
    {
        var (result, posted) = await AddAsync(playlist);

        Assert.True(result.Ok);
        Assert.True(posted);
    }

    [Fact]
    public async Task Unreachable_playlist_is_refused_with_an_error()
    {
        var (result, posted) = await AddAsync(null);

        Assert.False(result.Ok);
        Assert.False(posted);
        Assert.Contains("erişilemiyor", result.Message);
    }
}
