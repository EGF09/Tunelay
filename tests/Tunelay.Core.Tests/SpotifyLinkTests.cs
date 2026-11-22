using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

public class SpotifyLinkTests
{
    [Theory]
    [InlineData("spotify:track:4uLU6hMCjMI75M1A2tKUQC", "https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC")]
    [InlineData("spotify:episode:512ojhOuo1ktJprKbVcKyQ", "https://open.spotify.com/episode/512ojhOuo1ktJprKbVcKyQ")]
    public void Links_to_the_song_in_the_app_or_on_the_web(string uri, string web)
    {
        Assert.Equal(uri, SpotifyLink.For(uri, appInstalled: true));
        Assert.Equal(web, SpotifyLink.For(uri, appInstalled: false));
    }

    // The target is handed to the Windows shell, so nothing else may get through.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("spotify:local:Artist:Album:Song:200")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("spotify:track:abc\" --flag")]
    [InlineData("spotify:track:abc\n")]
    [InlineData("xspotify:track:abc")]
    public void Anything_else_only_opens_spotify(string? uri)
    {
        Assert.Equal("spotify:", SpotifyLink.For(uri, appInstalled: true));
        Assert.Equal("https://open.spotify.com/", SpotifyLink.For(uri, appInstalled: false));
    }
}
