using Tunelay.Core.Playback;
using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

public class PlaybackRouterTests
{
    private sealed class FakeSource(SourceCapabilities capabilities) : IPlaybackSource
    {
        public List<string> Calls { get; } = [];

        public SourceCapabilities Capabilities => capabilities;

        public PlaybackState? Current { get; private set; }

        public bool? IsLiked => null;

        public PollMode Mode { get; set; }

        public event Action<PlaybackState?>? StateChanged;

        public event Action<bool?>? LikedChanged { add { } remove { } }

        public event Action<string>? Error { add { } remove { } }

        public event Action<string>? Info { add { } remove { } }

        public void Publish(PlaybackState state)
        {
            Current = state;
            StateChanged?.Invoke(state);
        }

        public void Start() => Calls.Add("start");

        public void RefreshSoon() => Calls.Add("refresh");

        public Task TogglePlayPauseAsync() => Log("toggle");

        public Task NextAsync() => Log("next");

        public Task PreviousAsync() => Log("previous");

        public Task ChangeVolumeAsync(int delta) => Log("volume");

        public Task SetVolumeAsync(int percent) => Log("volume");

        public Task ToggleShuffleAsync() => Log("shuffle");

        public Task CycleRepeatAsync() => Log("repeat");

        public Task ToggleLikeAsync() => Log("like");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Task Log(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }

    private bool _signedIn;
    private readonly FakeSource _account = new(new SourceCapabilities(Account: true, Volume: true));
    private readonly FakeSource _local = new(new SourceCapabilities(Account: false, Volume: true));

    private PlaybackRouter Router() => new(_account, _local, () => _signedIn);

    private static PlaybackState State(string id) => new(true, false, 0, null, new Track(id, "", id, 1000, [], null));

    [Fact]
    public async Task Without_an_account_controls_go_to_windows()
    {
        await Router().NextAsync();

        Assert.Contains("next", _local.Calls);
        Assert.DoesNotContain("next", _account.Calls);
    }

    [Fact]
    public async Task With_an_account_controls_go_to_the_web_api()
    {
        _signedIn = true;

        await Router().NextAsync();

        Assert.Contains("next", _account.Calls);
        Assert.DoesNotContain("next", _local.Calls);
    }

    [Fact]
    public void Only_the_active_source_reaches_the_card()
    {
        var router = Router();
        var seen = new List<string>();
        router.StateChanged += s => seen.Add(s?.Item?.Id ?? "none");

        _account.Publish(State("account"));
        _local.Publish(State("local"));

        Assert.Equal(["local"], seen);
    }

    [Fact]
    public void Signing_in_switches_state_and_capabilities()
    {
        var router = Router();
        _account.Publish(State("account"));
        PlaybackState? shown = null;
        router.StateChanged += s => shown = s;
        Assert.False(router.Capabilities.Account);

        _signedIn = true;
        router.AccountChanged();

        Assert.True(router.Capabilities.Account);
        Assert.Equal("account", shown?.Item?.Id);
        Assert.Contains("refresh", _account.Calls);
    }

    [Fact]
    public async Task Like_without_an_account_points_to_connecting()
    {
        var router = Router();
        string? message = null;
        router.Error += m => message = m;

        await router.ToggleLikeAsync();

        Assert.Contains("hesab", message);
        Assert.DoesNotContain("like", _local.Calls);
    }
}
