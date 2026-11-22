using Spotiwidget.Core.Settings;
using Spotiwidget.Core.Spotify;

namespace Spotiwidget.Core.Moods;

public sealed record MoodResult(bool Ok, string Message);

/// <summary>Switches moods by starting the playlist the user mapped to each one.</summary>
public sealed class MoodService(ISpotifyApi api, SettingsStore settings)
{
    public string? Active => settings.Current.FindMood(settings.Current.ActiveMood)?.Id;

    public event Action<string?>? ActiveMoodChanged;

    public async Task<MoodResult> SwitchAsync(string moodId, CancellationToken ct = default)
    {
        if (settings.Current.FindMood(moodId) is not { } mood)
        {
            return new MoodResult(false, "Bu ruh hali artık yok.");
        }

        if (!settings.Current.MoodPlaylists.TryGetValue(mood.Id, out var playlist))
        {
            return new MoodResult(false, $"{mood.Name} için çalma listesi seçilmedi. Ayarlardan seçin.");
        }

        try
        {
            await api.PlayContextAsync(playlist.Uri, ct).ConfigureAwait(false);
            await api.SetShuffleAsync(true, ct).ConfigureAwait(false);
        }
        catch (SpotifyApiException ex)
        {
            return new MoodResult(false, ex.Message);
        }

        settings.Update(s => s.ActiveMood = mood.Id);
        ActiveMoodChanged?.Invoke(mood.Id);
        return new MoodResult(true, $"{mood.Name} · {playlist.Name}");
    }

    /// <summary>Moves to the next mood (in the user's order) that has a playlist.</summary>
    public Task<MoodResult> CycleAsync(CancellationToken ct = default)
    {
        var mapped = settings.Current.Moods.Where(m => settings.Current.MoodPlaylists.ContainsKey(m.Id)).ToList();
        if (mapped.Count == 0)
        {
            return Task.FromResult(new MoodResult(false, "Hiçbir ruh haline çalma listesi atanmadı."));
        }

        var index = mapped.FindIndex(m => m.Id == Active);
        return SwitchAsync(mapped[(index + 1) % mapped.Count].Id, ct);
    }

    public async Task<MoodResult> SaveTrackToActiveAsync(Track? track, CancellationToken ct = default)
    {
        if (track is null)
        {
            return new MoodResult(false, "Şu an çalan bir şarkı yok.");
        }

        if (Active is not { } id || !settings.Current.MoodPlaylists.TryGetValue(id, out var playlist))
        {
            return new MoodResult(false, "Önce bir ruh hali seçin.");
        }

        try
        {
            await api.AddToPlaylistAsync(playlist.Id, track.Uri, ct).ConfigureAwait(false);
            return new MoodResult(true, $"“{track.Name}” → {playlist.Name}");
        }
        catch (SpotifyApiException ex)
        {
            return new MoodResult(false, ex.Message);
        }
    }
}
