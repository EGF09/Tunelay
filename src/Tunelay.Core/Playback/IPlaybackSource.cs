using Tunelay.Core.Spotify;

namespace Tunelay.Core.Playback;

/// <param name="Account">Moods, liking and adding to playlists work.</param>
/// <param name="Volume">The volume row works.</param>
public sealed record SourceCapabilities(bool Account, bool Volume);

/// <summary>Where the card gets playback from: the Spotify Web API or the Windows media session.</summary>
public interface IPlaybackSource : IAsyncDisposable
{
    SourceCapabilities Capabilities { get; }

    PlaybackState? Current { get; }

    bool? IsLiked { get; }

    PollMode Mode { get; set; }

    event Action<PlaybackState?>? StateChanged;

    event Action<bool?>? LikedChanged;

    event Action<string>? Error;

    event Action<string>? Info;

    void Start();

    void RefreshSoon();

    Task TogglePlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();

    Task ChangeVolumeAsync(int delta);

    Task SetVolumeAsync(int percent);

    Task ToggleShuffleAsync();

    Task CycleRepeatAsync();

    Task ToggleLikeAsync();
}
