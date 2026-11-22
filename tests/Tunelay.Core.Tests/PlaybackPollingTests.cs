using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

public class PlaybackPollingTests
{
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(15);

    private static PlaybackState Playing(int durationMs, int progressMs, bool isPlaying = true) =>
        new(isPlaying, false, progressMs, null, new Track("t", "spotify:track:t", "Song", durationMs, [], null));

    [Fact]
    public void Wakes_just_after_the_song_ends()
    {
        var delay = PlaybackService.NextPollDelay(Playing(200_000, 196_000), Slow);
        Assert.Equal(TimeSpan.FromSeconds(4.5), delay);
    }

    [Fact]
    public void Uses_the_normal_interval_when_the_song_has_long_to_go()
    {
        Assert.Equal(Slow, PlaybackService.NextPollDelay(Playing(200_000, 10_000), Slow));
    }

    [Fact]
    public void Never_polls_faster_than_once_a_second()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), PlaybackService.NextPollDelay(Playing(200_000, 199_900), Slow));
        Assert.Equal(TimeSpan.FromSeconds(1), PlaybackService.NextPollDelay(Playing(200_000, 250_000), Slow));
    }

    [Fact]
    public void Paused_or_idle_uses_the_normal_interval()
    {
        Assert.Equal(Slow, PlaybackService.NextPollDelay(Playing(200_000, 199_000, isPlaying: false), Slow));
        Assert.Equal(Slow, PlaybackService.NextPollDelay(null, Slow));
    }
}
