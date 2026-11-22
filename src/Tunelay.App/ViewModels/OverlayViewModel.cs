using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Tunelay.Core.Settings;
using Tunelay.Core.Spotify;

namespace Tunelay.App.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Spotify'a bağlı değil";

    [ObservableProperty]
    private string _artist = "Tepsi simgesi → Ayarlar";

    [ObservableProperty]
    private string? _imageUrl;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int? _volume;

    [ObservableProperty]
    private string _moodLabel = "";

    [ObservableProperty]
    private Brush _moodBrush = Brushes.Transparent;

    [ObservableProperty]
    private string? _toast;

    [ObservableProperty]
    private bool? _isLiked;

    [ObservableProperty]
    private bool _isShuffle;

    /// <summary>Accent green while shuffle is on.</summary>
    public Brush ShuffleBrush => IsShuffle ? LikedBrush : Brushes.White;

    public string ShuffleTip => IsShuffle ? "Karışık çalma: açık (kapatmak için tıklayın)" : "Karışık çalma: kapalı (açmak için tıklayın)";

    partial void OnIsShuffleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShuffleBrush));
        OnPropertyChanged(nameof(ShuffleTip));
    }

    /// <summary>"off", "context" or "track".</summary>
    [ObservableProperty]
    private string _repeatState = RepeatMode.Off;

    /// <summary>Repeat-one icon for a single song, repeat-all otherwise.</summary>
    public string RepeatGlyph => RepeatState == RepeatMode.Track ? "" : "";

    public Brush RepeatBrush => RepeatState == RepeatMode.Off ? Brushes.White : LikedBrush;

    public string RepeatTip => RepeatState switch
    {
        RepeatMode.Context => "Tekrarlama: liste (tıklayınca: bu şarkı)",
        RepeatMode.Track => "Tekrarlama: bu şarkı (tıklayınca: kapalı)",
        _ => "Tekrarlama: kapalı (tıklayınca: liste)",
    };

    partial void OnRepeatStateChanged(string value)
    {
        OnPropertyChanged(nameof(RepeatGlyph));
        OnPropertyChanged(nameof(RepeatBrush));
        OnPropertyChanged(nameof(RepeatTip));
    }

    /// <summary>Spotify id of the current track; null when nothing plays. Changes only on a real song change.</summary>
    [ObservableProperty]
    private string? _trackId;

    public string PlayGlyph => IsPlaying ? "" : "";

    /// <summary>Filled heart when the track is in Liked Songs.</summary>
    public string LikeGlyph => IsLiked == true ? "" : "";

    public Brush LikeBrush => IsLiked == true ? LikedBrush : Brushes.White;

    private static readonly Brush LikedBrush = MakeFrozen(Color.FromRgb(0x1E, 0xD7, 0x60));

    public string VolumeText => Volume is { } v ? $"{v}%" : "";

    public string MoodSuffix => MoodLabel.Length == 0 ? "" : "  ·  " + MoodLabel;

    partial void OnMoodLabelChanged(string value) => OnPropertyChanged(nameof(MoodSuffix));

    partial void OnIsLikedChanged(bool? value)
    {
        OnPropertyChanged(nameof(LikeGlyph));
        OnPropertyChanged(nameof(LikeBrush));
    }

    private static Brush MakeFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayGlyph));

    partial void OnVolumeChanged(int? value) => OnPropertyChanged(nameof(VolumeText));

    public void ApplyPlayback(PlaybackState? state, bool signedIn)
    {
        if (!signedIn)
        {
            TrackId = null;
            Title = "Spotify'a bağlı değil";
            Artist = "Tepsi simgesi → Ayarlar";
            ImageUrl = null;
            IsPlaying = false;
            Volume = null;
            return;
        }

        if (state?.Item is not { } track)
        {
            TrackId = null;
            Title = "Şu an bir şey çalmıyor";
            Artist = "Bir mod seçin veya Spotify'ı açın";
            ImageUrl = null;
            IsPlaying = false;
            IsShuffle = state?.ShuffleState == true;
            RepeatState = state?.RepeatState ?? RepeatMode.Off;
            Volume = state?.Device?.VolumePercent;
            return;
        }

        // Text and art first, so the change notification for TrackId sees the new song.
        Title = track.Name;
        Artist = track.ArtistNames;
        ImageUrl = track.ImageUrl;
        TrackId = track.Id;
        IsPlaying = state.IsPlaying;
        IsShuffle = state.ShuffleState;
        RepeatState = state.RepeatState ?? RepeatMode.Off;
        Volume = state.Device?.VolumePercent;
    }

    public void ApplyMood(MoodDefinition? mood)
    {
        MoodLabel = mood?.Name ?? "";
        MoodBrush = mood is null ? Brushes.Transparent : MoodPalette.Brush(mood.Color);
    }
}

public static class MoodPalette
{
    /// <summary>Colors offered when creating or editing a mood (all readable with white text on the dark card).</summary>
    public static readonly string[] Presets =
    [
        "#FF6B35", "#E84A5F", "#F2A541", "#2EC4B6", "#3FA34D",
        "#4C7DF0", "#5D6FB0", "#8E6CC9", "#C45AB3", "#7A8794",
    ];

    public static Color Color(string? hex)
    {
        try
        {
            return hex is null ? Colors.Gray : (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }

    public static SolidColorBrush Brush(string? hex, byte alpha = 0xFF)
    {
        var c = Color(hex);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}
