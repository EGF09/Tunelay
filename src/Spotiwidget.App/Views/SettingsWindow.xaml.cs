using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Spotiwidget.App.ViewModels;
using Spotiwidget.Core.Settings;
using Spotiwidget.Core.Spotify;
using Spotiwidget.Native;

namespace Spotiwidget.App.Views;

public partial class SettingsWindow : Window
{
    private static readonly Dictionary<HotkeyAction, string> ActionLabels = new()
    {
        [HotkeyAction.ToggleMenu] = "Etkileşim tuşu (kartı aç)",
        [HotkeyAction.MoveMode] = "Taşıma modu (kartı/paneli sürükle)",
        [HotkeyAction.PlayPause] = "Oynat / duraklat",
        [HotkeyAction.Like] = "Beğen / beğenmekten vazgeç",
        [HotkeyAction.Next] = "Sonraki şarkı",
        [HotkeyAction.Previous] = "Önceki şarkı",
        [HotkeyAction.VolumeUp] = "Sesi artır",
        [HotkeyAction.VolumeDown] = "Sesi azalt",
        [HotkeyAction.SaveToMood] = "Şarkıyı ruh hali listesine ekle",
        [HotkeyAction.CycleMood] = "Sonraki ruh haline geç",
        [HotkeyAction.Borderless] = "Kenarlıksız tam ekran (aç / kapat)",
    };

    private static readonly Dictionary<ScreenCorner, string> CornerLabels = new()
    {
        [ScreenCorner.BottomRight] = "Sağ alt",
        [ScreenCorner.BottomLeft] = "Sol alt",
        [ScreenCorner.TopRight] = "Sağ üst",
        [ScreenCorner.TopLeft] = "Sol üst",
    };

    private readonly SettingsStore _store;
    private readonly SpotifyAuthSession _session;
    private readonly ISpotifyApi _api;
    private readonly Action<string> _openBrowser;
    private readonly List<MoodRow> _moodRows = new();
    private readonly Dictionary<HotkeyAction, TextBox> _hotkeyBoxes = new();
    private List<PlaylistRef> _playlists = new();
    private CancellationTokenSource? _loginCts;
    private bool _resetPositions;

    /// <summary>One editable mood line: color, name, playlist, delete.</summary>
    private sealed record MoodRow(string Id, FrameworkElement Root, ComboBox Color, TextBox Name, ComboBox Playlist);

    public SettingsWindow(SettingsStore store, SpotifyAuthSession session, ISpotifyApi api, Action<string> openBrowser)
    {
        InitializeComponent();
        _store = store;
        _session = session;
        _api = api;
        _openBrowser = openBrowser;

        var s = store.Current;
        ClientIdBox.Text = s.ClientId;
        RedirectBox.Text = PkceAuth.RedirectUri;
        ShowMoodDockBox.IsChecked = s.ShowMoodDock;
        CornerBox.ItemsSource = CornerLabels;
        CornerBox.DisplayMemberPath = "Value";
        CornerBox.SelectedValuePath = "Key";
        CornerBox.SelectedValue = s.Corner;

        BindPercent(IdleOpacitySlider, IdleOpacityText, s.IdleOpacity);
        BindPercent(ActiveOpacitySlider, ActiveOpacityText, s.ActiveOpacity);

        // Playlists already chosen, so the boxes show them before the full list loads.
        _playlists = s.MoodPlaylists.Values.DistinctBy(p => p.Id).ToList();
        foreach (var mood in s.Moods)
        {
            AddMoodRow(mood, s.MoodPlaylists.GetValueOrDefault(mood.Id));
        }

        BuildHotkeyRows(s);
        UpdateAuthUi();

        Loaded += async (_, _) => await LoadPlaylistsAsync();
        Closed += (_, _) => _loginCts?.Cancel();
    }

    private static void BindPercent(Slider slider, TextBlock label, double value)
    {
        slider.ValueChanged += (_, _) => label.Text = $"%{slider.Value:0}";
        slider.Value = Math.Round(value * 100);
        label.Text = $"%{slider.Value:0}";
    }

    private void AddMoodRow(MoodDefinition mood, PlaylistRef? playlist)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Color: a swatch dropdown of presets (plus the mood's own color if it isn't one of them).
        var colors = MoodPalette.Presets.Contains(mood.Color, StringComparer.OrdinalIgnoreCase)
            ? MoodPalette.Presets
            : MoodPalette.Presets.Prepend(mood.Color).ToArray();
        var colorBox = new ComboBox
        {
            Width = 56,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Renk",
            ItemTemplate = (DataTemplate)FindResource("SwatchTemplate"),
            ItemsSource = colors,
        };
        colorBox.SelectedIndex = Array.FindIndex(colors, c => string.Equals(c, mood.Color, StringComparison.OrdinalIgnoreCase));

        var nameBox = new TextBox { Text = mood.Name, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "İsim" };
        var listBox = new ComboBox { DisplayMemberPath = nameof(PlaylistRef.Name), ItemsSource = _playlists, ToolTip = "Çalma listesi" };
        listBox.SelectedItem = playlist is null ? null : _playlists.FirstOrDefault(p => p.Id == playlist.Id);

        var delete = new Button { Content = "Sil", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), ToolTip = "Bu ruh halini sil" };

        Grid.SetColumn(nameBox, 1);
        Grid.SetColumn(listBox, 2);
        Grid.SetColumn(delete, 3);
        grid.Children.Add(colorBox);
        grid.Children.Add(nameBox);
        grid.Children.Add(listBox);
        grid.Children.Add(delete);

        var row = new MoodRow(mood.Id, grid, colorBox, nameBox, listBox);
        delete.Click += (_, _) =>
        {
            _moodRows.Remove(row);
            MoodList.Children.Remove(grid);
        };
        _moodRows.Add(row);
        MoodList.Children.Add(grid);
    }

    private void OnAddMood(object sender, RoutedEventArgs e)
    {
        var used = _moodRows.Select(r => r.Color.SelectedItem as string).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var color = MoodPalette.Presets.FirstOrDefault(c => !used.Contains(c)) ?? MoodPalette.Presets[0];
        AddMoodRow(new MoodDefinition("m-" + Guid.NewGuid().ToString("N")[..8], "Yeni ruh hali", color), null);
        var added = _moodRows[^1].Name;
        added.Focus();
        added.SelectAll();
    }

    private void OnResetPositions(object sender, RoutedEventArgs e)
    {
        _resetPositions = true;
        AuthStatus.Text = "";
        ((Button)sender).Content = "Kaydedince sıfırlanacak ✓";
    }

    private void BuildHotkeyRows(AppSettings s)
    {
        var row = 0;
        foreach (var (action, label) in ActionLabels)
        {
            HotkeyGrid.RowDefinitions.Add(new RowDefinition());
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox
            {
                Margin = new Thickness(0, 3, 0, 3),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Text = s.Hotkeys.GetValueOrDefault(action, ""),
            };
            box.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key is Key.Back or Key.Delete)
                {
                    box.Text = "";
                    return;
                }

                if (key == Key.Tab)
                {
                    e.Handled = false;
                    return;
                }

                if (HotkeyGesture.Format(Keyboard.Modifiers, key) is { } combo)
                {
                    box.Text = combo;
                }
            };
            Grid.SetRow(text, row);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            HotkeyGrid.Children.Add(text);
            HotkeyGrid.Children.Add(box);
            _hotkeyBoxes[action] = box;
            row++;
        }
    }

    private void UpdateAuthUi()
    {
        ConnectButton.Content = _session.IsSignedIn ? "Bağlantıyı kes" : "Spotify'a bağlan";
        AuthStatus.Text = _session.IsSignedIn ? "Bağlı ✓" : "Bağlı değil";
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        if (_session.IsSignedIn)
        {
            await _session.SignOutAsync();
            UpdateAuthUi();
            return;
        }

        var clientId = ClientIdBox.Text.Trim();
        if (clientId.Length == 0)
        {
            AuthStatus.Text = "Önce Client ID girin.";
            return;
        }

        // The session reads the client id from settings, so persist it first.
        _store.Update(s => s.ClientId = clientId);

        _loginCts?.Cancel();
        _loginCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        ConnectButton.IsEnabled = false;
        AuthStatus.Text = "Tarayıcıda girişi tamamlayın…";
        try
        {
            await _session.SignInAsync(_openBrowser, _loginCts.Token);
            UpdateAuthUi();
            await LoadPlaylistsAsync();
        }
        catch (OperationCanceledException)
        {
            AuthStatus.Text = "Giriş zaman aşımına uğradı.";
        }
        catch (Exception ex) when (ex is SpotifyAuthException or System.Net.HttpListenerException or HttpRequestException)
        {
            AuthStatus.Text = ex.Message;
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async void OnRefreshPlaylists(object sender, RoutedEventArgs e) => await LoadPlaylistsAsync();

    private async Task LoadPlaylistsAsync()
    {
        if (!_session.IsSignedIn)
        {
            return;
        }

        try
        {
            var lists = (await _api.GetMyPlaylistsAsync())
                .Select(p => new PlaylistRef(p.Id, p.Name))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _playlists = lists;
            foreach (var row in _moodRows)
            {
                var selected = row.Playlist.SelectedItem as PlaylistRef;
                row.Playlist.ItemsSource = lists;
                row.Playlist.SelectedItem = selected is null ? null : lists.FirstOrDefault(p => p.Id == selected.Id);
            }
        }
        catch (SpotifyApiException ex)
        {
            AuthStatus.Text = ex.Message;
        }
        catch (HttpRequestException)
        {
            AuthStatus.Text = "Spotify'a ulaşılamadı.";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var hotkeys = new Dictionary<HotkeyAction, string>();
        foreach (var (action, box) in _hotkeyBoxes)
        {
            if (box.Text.Length > 0 && !HotkeyGesture.TryParse(box.Text, out _))
            {
                MessageBox.Show(this, $"Geçersiz kısayol: {box.Text}", "Spotiwidget");
                return;
            }

            hotkeys[action] = box.Text;
        }

        var duplicate = hotkeys.Values.Where(v => v.Length > 0).GroupBy(v => v, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            MessageBox.Show(this, $"Aynı kısayol birden fazla işleve atanmış: {duplicate.Key}", "Spotiwidget");
            return;
        }

        var moods = new List<MoodDefinition>();
        var playlists = new Dictionary<string, PlaylistRef>();
        foreach (var row in _moodRows)
        {
            var name = row.Name.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "Her ruh halinin bir ismi olmalı.", "Spotiwidget");
                row.Name.Focus();
                return;
            }

            var color = row.Color.SelectedItem as string ?? MoodPalette.Presets[0];
            moods.Add(new MoodDefinition(row.Id, name, color));
            if (row.Playlist.SelectedItem is PlaylistRef p)
            {
                playlists[row.Id] = p;
            }
        }

        _store.Update(s =>
        {
            s.ClientId = ClientIdBox.Text.Trim();
            s.Corner = CornerBox.SelectedValue is ScreenCorner c ? c : ScreenCorner.BottomRight;
            s.ShowMoodDock = ShowMoodDockBox.IsChecked == true;
            s.IdleOpacity = IdleOpacitySlider.Value / 100;
            s.ActiveOpacity = ActiveOpacitySlider.Value / 100;
            s.Hotkeys = hotkeys;
            s.Moods = moods;
            s.MoodPlaylists = playlists;
            if (s.FindMood(s.ActiveMood) is null)
            {
                s.ActiveMood = null; // the active mood was deleted
            }

            if (_resetPositions)
            {
                s.CardLeft = s.CardAnchor = null;
                s.DockLeft = s.DockTop = null;
            }
        });
        // Shown modeless (Show, not ShowDialog), so DialogResult can't be set; just close.
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
