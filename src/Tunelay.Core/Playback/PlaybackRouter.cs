using Tunelay.Core.Spotify;

namespace Tunelay.Core.Playback;

/// <summary>With an account the Web API drives the card; without one, Windows does. Only that source is heard.</summary>
public sealed class PlaybackRouter : IPlaybackSource
{
    private readonly IPlaybackSource _account;
    private readonly IPlaybackSource _local;
    private readonly Func<bool> _signedIn;

    public PlaybackRouter(IPlaybackSource account, IPlaybackSource local, Func<bool> signedIn)
    {
        (_account, _local, _signedIn) = (account, local, signedIn);
        Forward(account);
        Forward(local);
    }

    private IPlaybackSource Active => _signedIn() ? _account : _local;

    public SourceCapabilities Capabilities => Active.Capabilities;

    public PlaybackState? Current => Active.Current;

    public bool? IsLiked => Active.IsLiked;

    public PollMode Mode
    {
        get => Active.Mode;
        set
        {
            _account.Mode = value;
            _local.Mode = value;
        }
    }

    public event Action<PlaybackState?>? StateChanged;

    public event Action<bool?>? LikedChanged;

    public event Action<string>? Error;

    public event Action<string>? Info;

    public void Start()
    {
        _account.Start();
        _local.Start();
    }

    /// <summary>Call after signing in or out.</summary>
    public void AccountChanged()
    {
        StateChanged?.Invoke(Active.Current);
        LikedChanged?.Invoke(Active.IsLiked);
        _account.RefreshSoon(); // signed out, this makes it forget the account's data
        _local.RefreshSoon();
    }

    public void RefreshSoon() => Active.RefreshSoon();

    public Task TogglePlayPauseAsync() => Active.TogglePlayPauseAsync();

    public Task NextAsync() => Active.NextAsync();

    public Task PreviousAsync() => Active.PreviousAsync();

    public Task ChangeVolumeAsync(int delta) => Active.ChangeVolumeAsync(delta);

    public Task SetVolumeAsync(int percent) => Active.SetVolumeAsync(percent);

    public Task ToggleShuffleAsync() => Active.ToggleShuffleAsync();

    public Task CycleRepeatAsync() => Active.CycleRepeatAsync();

    public Task ToggleLikeAsync()
    {
        if (Active.Capabilities.Account)
        {
            return Active.ToggleLikeAsync();
        }

        Error?.Invoke("Beğenmek için Spotify hesabınızı bağlayın: Ayarlar → Spotify.");
        return Task.CompletedTask;
    }

    private void Forward(IPlaybackSource source)
    {
        source.StateChanged += state => { if (source == Active) StateChanged?.Invoke(state); };
        source.LikedChanged += liked => { if (source == Active) LikedChanged?.Invoke(liked); };
        source.Error += message => { if (source == Active) Error?.Invoke(message); };
        source.Info += message => { if (source == Active) Info?.Invoke(message); };
    }

    public async ValueTask DisposeAsync()
    {
        await _account.DisposeAsync().ConfigureAwait(false);
        await _local.DisposeAsync().ConfigureAwait(false);
    }
}
