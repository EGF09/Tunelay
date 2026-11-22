using Tunelay.Core.Moods;
using Tunelay.Core.Settings;
using Tunelay.Core.Spotify;

namespace Tunelay.Core.Tests;

public class SettingsStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "spw-" + Guid.NewGuid(), "settings.json");

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var store = new SettingsStore(TempPath());
        Assert.Equal(ScreenCorner.BottomRight, store.Current.Corner);
        Assert.Equal("Ctrl+Alt+Space", store.Current.Hotkeys[HotkeyAction.ToggleMenu]);
    }

    [Fact]
    public void Round_trips_settings()
    {
        var path = TempPath();
        var store = new SettingsStore(path);
        store.Update(s =>
        {
            s.ClientId = "cid";
            s.Corner = ScreenCorner.TopLeft;
            s.ShowMoodDock = false;
            s.DockLeft = 120.5;
            s.ActiveMood = "Focused";
            s.CardLeft = -1500;
            s.IdleOpacity = 0.25;
            s.GameAnimations = GameAnimationLevel.Off;
            s.Moods.Add(new MoodDefinition("rain", "Yağmurlu", "#336699"));
            s.MoodPlaylists["Focused"] = new PlaylistRef("p1", "Lo-fi");
            s.Hotkeys[HotkeyAction.Next] = "Ctrl+Shift+N";
        });

        var reloaded = new SettingsStore(path).Current;
        Assert.Equal("cid", reloaded.ClientId);
        Assert.Equal(ScreenCorner.TopLeft, reloaded.Corner);
        Assert.False(reloaded.ShowMoodDock);
        Assert.Equal(120.5, reloaded.DockLeft);
        Assert.Null(reloaded.DockTop);
        Assert.Equal("Focused", reloaded.ActiveMood);
        Assert.Equal(-1500, reloaded.CardLeft);
        Assert.Equal(0.25, reloaded.IdleOpacity);
        Assert.Equal(GameAnimationLevel.Off, reloaded.GameAnimations);
        Assert.Contains("\"GameAnimations\": \"Off\"", File.ReadAllText(path));
        Assert.Equal(new MoodDefinition("rain", "Yağmurlu", "#336699"), reloaded.Moods[^1]);
        Assert.Equal(6, reloaded.Moods.Count);
        Assert.Equal(new PlaylistRef("p1", "Lo-fi"), reloaded.MoodPlaylists["Focused"]);
        Assert.Equal("Ctrl+Shift+N", reloaded.Hotkeys[HotkeyAction.Next]);
        Assert.Contains("\"Focused\"", File.ReadAllText(path));
    }

    [Fact]
    public void Older_settings_file_still_loads_and_gains_new_hotkeys()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"ClientId":"cid","SimpleMode":true,"HotCorner":false,"Hotkeys":{"ToggleMenu":"Ctrl+Alt+Space"}}""");

        var loaded = new SettingsStore(path).Current;

        Assert.Equal("cid", loaded.ClientId);
        Assert.True(loaded.ShowMoodDock);
        Assert.True(loaded.Animations); // new setting defaults to on for older files
        Assert.Equal(GameAnimationLevel.Light, loaded.GameAnimations);
        Assert.Equal("Ctrl+Alt+K", loaded.Hotkeys[HotkeyAction.Shuffle]);
        Assert.Equal("Ctrl+Alt+R", loaded.Hotkeys[HotkeyAction.Repeat]);
        Assert.Equal("Ctrl+Alt+L", loaded.Hotkeys[HotkeyAction.Like]);
    }

    [Fact]
    public void Enum_era_settings_keep_active_mood_and_playlists()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {"ActiveMood":"Melancholic","IdleOpacity":7,
             "MoodPlaylists":{"Melancholic":{"Id":"p9","Name":"Rainy"}}}
            """);

        var loaded = new SettingsStore(path).Current;

        Assert.Equal(5, loaded.Moods.Count);
        Assert.Equal("Melankolik", loaded.FindMood(loaded.ActiveMood)!.Name);
        Assert.Equal("p9", loaded.MoodPlaylists["Melancholic"].Id);
        Assert.Equal(1, loaded.IdleOpacity); // clamped
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Equal("", new SettingsStore(path).Current.ClientId);
    }
}

public class MoodServiceTests
{
    private sealed class FakeApi : ISpotifyApi
    {
        public List<string> Calls { get; } = [];

        public Task<PlaybackState?> GetPlaybackAsync(CancellationToken ct = default) => Task.FromResult<PlaybackState?>(null);

        public Task PlayAsync(CancellationToken ct = default) => Log("play");

        public Task PauseAsync(CancellationToken ct = default) => Log("pause");

        public Task NextAsync(CancellationToken ct = default) => Log("next");

        public Task PreviousAsync(CancellationToken ct = default) => Log("prev");

        public Task SetVolumeAsync(int percent, CancellationToken ct = default) => Log($"vol {percent}");

        public Task SetShuffleAsync(bool on, CancellationToken ct = default) => Log($"shuffle {on}");

        public Task SetRepeatAsync(string state, CancellationToken ct = default) => Log($"repeat {state}");

        public Task PlayContextAsync(string contextUri, CancellationToken ct = default) => Log($"ctx {contextUri}");

        public Task<IReadOnlyList<SimplePlaylist>> GetMyPlaylistsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SimplePlaylist>>([]);

        public Task AddToPlaylistAsync(string playlistId, string trackUri, CancellationToken ct = default) => Log($"add {playlistId} {trackUri}");

        public Task<bool> IsInLibraryAsync(Track track, CancellationToken ct = default) => Task.FromResult(false);

        public Task SaveToLibraryAsync(Track track, CancellationToken ct = default) => Log($"like {track.Id}");

        public Task RemoveFromLibraryAsync(Track track, CancellationToken ct = default) => Log($"unlike {track.Id}");

        private Task Log(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }

    private static (MoodService Moods, FakeApi Api, SettingsStore Store) Build()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "spw-" + Guid.NewGuid(), "settings.json"));
        store.Update(s =>
        {
            s.MoodPlaylists["Energetic"] = new PlaylistRef("e1", "Gym");
            s.MoodPlaylists["Night"] = new PlaylistRef("n1", "Night");
        });
        var api = new FakeApi();
        return (new MoodService(api, store), api, store);
    }

    [Fact]
    public async Task Switch_plays_mapped_playlist_with_shuffle_and_saves_mood()
    {
        var (moods, api, store) = Build();
        string? changed = null;
        moods.ActiveMoodChanged += m => changed = m;

        var result = await moods.SwitchAsync("Night");

        Assert.True(result.Ok);
        Assert.Equal(["ctx spotify:playlist:n1", "shuffle True"], api.Calls);
        Assert.Equal("Night", store.Current.ActiveMood);
        Assert.Equal("Night", changed);
    }

    [Fact]
    public async Task Switch_to_unmapped_mood_fails_without_calling_spotify()
    {
        var (moods, api, _) = Build();
        var result = await moods.SwitchAsync("Calm");
        Assert.False(result.Ok);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task Cycle_walks_only_mapped_moods_and_wraps()
    {
        var (moods, _, _) = Build();
        await moods.CycleAsync();
        Assert.Equal("Energetic", moods.Active);
        await moods.CycleAsync();
        Assert.Equal("Night", moods.Active);
        await moods.CycleAsync();
        Assert.Equal("Energetic", moods.Active);
    }

    [Fact]
    public async Task Save_adds_current_track_to_active_mood_playlist()
    {
        var (moods, api, _) = Build();
        var track = new Track("t", "spotify:track:t", "Song", 1000, [], null);

        Assert.False((await moods.SaveTrackToActiveAsync(track)).Ok);

        await moods.SwitchAsync("Energetic");
        var result = await moods.SaveTrackToActiveAsync(track);

        Assert.True(result.Ok);
        Assert.Contains("add e1 spotify:track:t", api.Calls);
    }

    [Fact]
    public async Task Custom_mood_plays_and_cycles_in_user_order()
    {
        var (moods, api, store) = Build();
        store.Update(s =>
        {
            s.Moods.Insert(0, new MoodDefinition("rain", "Yağmurlu", "#336699"));
            s.MoodPlaylists["rain"] = new PlaylistRef("r1", "Rain");
        });

        var result = await moods.SwitchAsync("rain");

        Assert.Equal("Yağmurlu · Rain", result.Message);
        Assert.Contains("ctx spotify:playlist:r1", api.Calls);
        await moods.CycleAsync();
        Assert.Equal("Energetic", moods.Active);
    }

    [Fact]
    public async Task Deleted_active_mood_is_treated_as_none()
    {
        var (moods, _, store) = Build();
        await moods.SwitchAsync("Night");
        store.Update(s => s.Moods.RemoveAll(m => m.Id == "Night"));

        Assert.Null(moods.Active);
        Assert.False((await moods.SwitchAsync("Night")).Ok);
    }
}
