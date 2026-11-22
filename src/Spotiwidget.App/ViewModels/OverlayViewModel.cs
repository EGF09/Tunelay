using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Spotiwidget.Core.Settings;
using Spotiwidget.Core.Spotify;

namespace Spotiwidget.App.ViewModels;

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
            Title = "Spotify'a bağlı değil";
            Artist = "Tepsi simgesi → Ayarlar";
            ImageUrl = null;
            IsPlaying = false;
            Volume = null;
            return;
        }

        if (state?.Item is not { } track)
        {
            Title = "Şu an bir şey çalmıyor";
            Artist = "Bir mod seçin veya Spotify'ı açın";
            ImageUrl = null;
            IsPlaying = false;
            Volume = state?.Device?.VolumePercent;
            return;
        }

        Title = track.Name;
        Artist = track.ArtistNames;
        ImageUrl = track.ImageUrl;
        IsPlaying = state.IsPlaying;
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
