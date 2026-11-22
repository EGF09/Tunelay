using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Tunelay.App.ViewModels;
using Tunelay.Core.Settings;
using Tunelay.Core.Spotify;
using Tunelay.Native;

namespace Tunelay.App.Views;

/// <summary>Unsaved look settings, sent while the user edits so the card can preview them.</summary>
public sealed record SettingsPreview(
    ScreenCorner Corner,
    double IdleOpacity,
    double ActiveOpacity,
    bool Animations,
    GameAnimationLevel GameAnimations,
    bool ShowActive);

public partial class SettingsWindow : Window
{
    private const string DashboardUrl = "https://developer.spotify.com/dashboard";
    private const string PressKeyText = "Tuşa basın…";
    private const string UnassignedText = "Atanmadı";

    private static readonly Dictionary<HotkeyAction, string> ActionLabels = new()
    {
        [HotkeyAction.ToggleMenu] = "Etkileşim tuşu (kartı aç)",
        [HotkeyAction.MoveMode] = "Taşıma modu (kartı/paneli sürükle)",
        [HotkeyAction.PlayPause] = "Oynat / duraklat",
        [HotkeyAction.Like] = "Beğen / beğenmekten vazgeç",
        [HotkeyAction.Next] = "Sonraki şarkı",
        [HotkeyAction.Previous] = "Önceki şarkı",
        [HotkeyAction.Shuffle] = "Karışık çalmayı aç / kapat",
        [HotkeyAction.Repeat] = "Tekrarlama (kapalı / liste / şarkı)",
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

    private static readonly Dictionary<GameAnimationLevel, string> GameAnimationLabels = new()
    {
        [GameAnimationLevel.Off] = "Kapalı",
        [GameAnimationLevel.Light] = "Hafif (30 fps, önerilen)",
        [GameAnimationLevel.Full] = "Tam (60 fps)",
    };

    private readonly SettingsStore _store;
    private readonly SpotifyAuthSession _session;
    private readonly ISpotifyApi _api;
    private readonly Action<string> _openBrowser;
    private readonly Action<SettingsPreview>? _preview;
    private readonly List<MoodRow> _moodRows = new();
    private readonly Dictionary<HotkeyAction, TextBox> _hotkeyBoxes = new();
    private readonly Dictionary<HotkeyAction, string> _hotkeyValues = new();
    private List<PlaylistRef> _playlists = new();
    private CancellationTokenSource? _loginCts;
    private bool _resetPositions;
    private bool _ready;

    /// <summary>One editable mood line: stripe, color, name, playlist, reorder, delete.</summary>
    private sealed record MoodRow(string Id, FrameworkElement Root, ComboBox Color, TextBox Name, ComboBox Playlist);

    public SettingsWindow(SettingsStore store, SpotifyAuthSession session, ISpotifyApi api, Action<string> openBrowser,
        Action<SettingsPreview>? preview = null)
    {
        InitializeComponent();
        _store = store;
        _session = session;
        _api = api;
        _openBrowser = openBrowser;
        _preview = preview;

        var s = store.Current;
        ClientIdBox.Text = s.ClientId;
        RedirectBox.Text = PkceAuth.RedirectUri;
        ShowMoodDockBox.IsChecked = s.ShowMoodDock;
        AnimationsBox.IsChecked = s.Animations;
        GameAnimationsBox.ItemsSource = GameAnimationLabels;
        GameAnimationsBox.DisplayMemberPath = "Value";
        GameAnimationsBox.SelectedValuePath = "Key";
        GameAnimationsBox.SelectedValue = s.GameAnimations;
        CornerBox.ItemsSource = CornerLabels;
        CornerBox.DisplayMemberPath = "Value";
        CornerBox.SelectedValuePath = "Key";
        CornerBox.SelectedValue = s.Corner;
        if (s.CardLeft is not null)
        {
            ResetPositionsButton.ToolTip = "Kart taşınmış bir konumda; köşe seçimi ancak sıfırlayınca geçerli olur.";
        }

        BindPercent(IdleOpacitySlider, IdleOpacityText, s.IdleOpacity, showActive: false);
        BindPercent(ActiveOpacitySlider, ActiveOpacityText, s.ActiveOpacity, showActive: true);

        // Playlists already chosen, so the boxes show them before the full list loads.
        _playlists = s.MoodPlaylists.Values.DistinctBy(p => p.Id).ToList();
        foreach (var mood in s.Moods)
        {
            AddMoodRow(mood, s.MoodPlaylists.GetValueOrDefault(mood.Id), animate: false);
        }

        BuildHotkeyRows(s);
        UpdateAuthUi();
        NavList.SelectedIndex = _session.IsSignedIn ? 1 : 0;

        Loaded += async (_, _) =>
        {
            _ready = true;
            await LoadPlaylistsAsync();
        };
        Closed += (_, _) => _loginCts?.Cancel();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    // ---------- Navigation ----------

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem { Tag: string name } || FindName(name) is not UIElement target)
        {
            return;
        }

        foreach (var section in new UIElement[] { SpotifySection, MoodsSection, LookSection, KeysSection })
        {
            section.Visibility = section == target ? Visibility.Visible : Visibility.Collapsed;
        }

        Scroller.ScrollToTop();
        Anim.Enter(target, 8, 200);
    }

    private void ShowSection(UIElement section) =>
        NavList.SelectedItem = NavList.Items.OfType<ListBoxItem>().First(i => (string)i.Tag == ((FrameworkElement)section).Name);

    // ---------- Look + live preview ----------

    private void BindPercent(Slider slider, TextBlock label, double value, bool showActive)
    {
        slider.Value = Math.Round(value * 100);
        label.Text = $"%{slider.Value:0}";
        slider.ValueChanged += (_, _) =>
        {
            label.Text = $"%{slider.Value:0}";
            SendPreview(showActive);
        };
    }

    private void OnPreviewChanged(object sender, RoutedEventArgs e) => SendPreview(showActive: false);

    private void SendPreview(bool showActive)
    {
        if (!_ready || _preview is null)
        {
            return;
        }

        _preview(new SettingsPreview(
            CornerBox.SelectedValue is ScreenCorner c ? c : ScreenCorner.BottomRight,
            IdleOpacitySlider.Value / 100,
            ActiveOpacitySlider.Value / 100,
            AnimationsBox.IsChecked == true,
            SelectedGameAnimations(),
            showActive));
    }

    private GameAnimationLevel SelectedGameAnimations() =>
        GameAnimationsBox.SelectedValue is GameAnimationLevel level ? level : GameAnimationLevel.Light;

    private void OnResetPositions(object sender, RoutedEventArgs e)
    {
        _resetPositions = true;
        ResetPositionsButton.Content = "Kaydedince sıfırlanacak ✓";
        ResetPositionsButton.IsEnabled = false;
    }

    // ---------- Moods ----------

    private void AddMoodRow(MoodDefinition mood, PlaylistRef? playlist, bool animate = true)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(150), new GridLength(1, GridUnitType.Star), GridLength.Auto })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }

        var stripe = new Border { Width = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 10, 2), Background = MoodPalette.Brush(mood.Color) };

        // Color: a swatch dropdown of presets (plus the mood's own color if it isn't one of them).
        var colors = MoodPalette.Presets.Contains(mood.Color, StringComparer.OrdinalIgnoreCase)
            ? MoodPalette.Presets
            : MoodPalette.Presets.Prepend(mood.Color).ToArray();
        var colorBox = new ComboBox
        {
            Width = 58,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Renk",
            ItemTemplate = (DataTemplate)FindResource("SwatchTemplate"),
            ItemsSource = colors,
        };
        colorBox.SelectedIndex = Array.FindIndex(colors, c => string.Equals(c, mood.Color, StringComparison.OrdinalIgnoreCase));
        colorBox.SelectionChanged += (_, _) => stripe.Background = MoodPalette.Brush(colorBox.SelectedItem as string);

        var nameBox = new TextBox { Text = mood.Name, Margin = new Thickness(0, 0, 8, 0), ToolTip = "İsim" };
        var listBox = new ComboBox { DisplayMemberPath = nameof(PlaylistRef.Name), ItemsSource = _playlists, ToolTip = "Çalma listesi" };
        listBox.SelectedItem = playlist is null ? null : _playlists.FirstOrDefault(p => p.Id == playlist.Id);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
        var up = new Button { Content = "", Style = (Style)FindResource("SmallButton"), ToolTip = "Yukarı taşı" };
        var down = new Button { Content = "", Style = (Style)FindResource("SmallButton"), ToolTip = "Aşağı taşı" };
        var delete = new Button { Content = "", Style = (Style)FindResource("DangerButton"), ToolTip = "Bu ruh halini sil" };
        actions.Children.Add(up);
        actions.Children.Add(down);
        actions.Children.Add(delete);

        Grid.SetColumn(colorBox, 1);
        Grid.SetColumn(nameBox, 2);
        Grid.SetColumn(listBox, 3);
        Grid.SetColumn(actions, 4);
        grid.Children.Add(stripe);
        grid.Children.Add(colorBox);
        grid.Children.Add(nameBox);
        grid.Children.Add(listBox);
        grid.Children.Add(actions);

        var row = new MoodRow(mood.Id, grid, colorBox, nameBox, listBox);
        up.Click += (_, _) => MoveMood(row, -1);
        down.Click += (_, _) => MoveMood(row, +1);
        delete.Click += (_, _) =>
        {
            _moodRows.Remove(row);
            Anim.Fade(grid, 0, 140, () => MoodList.Children.Remove(grid));
        };
        _moodRows.Add(row);
        MoodList.Children.Add(grid);
        if (animate)
        {
            Anim.Enter(grid, 6, 200);
        }
    }

    private void MoveMood(MoodRow row, int delta)
    {
        var index = _moodRows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _moodRows.Count)
        {
            return;
        }

        _moodRows.RemoveAt(index);
        _moodRows.Insert(target, row);
        MoodList.Children.Remove(row.Root);
        MoodList.Children.Insert(target, row.Root);
        Anim.Enter(row.Root, delta * -6, 160, 0.4);
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

    // ---------- Hotkeys ----------

    private void BuildHotkeyRows(AppSettings s)
    {
        var row = 0;
        foreach (var (action, label) in ActionLabels)
        {
            HotkeyGrid.RowDefinitions.Add(new RowDefinition());
            _hotkeyValues[action] = s.Hotkeys.GetValueOrDefault(action, "");
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var box = new TextBox
            {
                Margin = new Thickness(0, 4, 0, 4),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Cursor = Cursors.Hand,
            };
            ShowHotkey(action, box);

            box.GotKeyboardFocus += (_, _) =>
            {
                box.Text = PressKeyText;
                box.Foreground = (Brush)FindResource("AccentBrush");
            };
            box.LostKeyboardFocus += (_, _) => ShowHotkey(action, box);
            box.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Tab)
                {
                    return; // keep keyboard navigation
                }

                e.Handled = true;
                switch (key)
                {
                    case Key.Escape:
                        Keyboard.ClearFocus();
                        return;
                    case Key.Back or Key.Delete:
                        _hotkeyValues[action] = "";
                        break;
                    default:
                        if (HotkeyGesture.Format(Keyboard.Modifiers, key) is not { } combo)
                        {
                            return; // a lone modifier: keep waiting
                        }

                        _hotkeyValues[action] = combo;
                        break;
                }

                Keyboard.ClearFocus();
                ValidateHotkeys();
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

    private void ShowHotkey(HotkeyAction action, TextBox box)
    {
        var value = _hotkeyValues[action];
        box.Text = value.Length == 0 ? UnassignedText : value;
        box.Foreground = (Brush)FindResource(value.Length == 0 ? "SubtleTextBrush" : "TextBrush");
    }

    /// <summary>Marks duplicate or invalid shortcuts in red. Returns an error message, or null.</summary>
    private string? ValidateHotkeys()
    {
        var duplicates = _hotkeyValues
            .Where(kv => kv.Value.Length > 0)
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(kv => kv.Key))
            .ToHashSet();
        var invalid = _hotkeyValues.Where(kv => kv.Value.Length > 0 && !HotkeyGesture.TryParse(kv.Value, out _)).Select(kv => kv.Key).ToHashSet();

        foreach (var (action, box) in _hotkeyBoxes)
        {
            var bad = duplicates.Contains(action) || invalid.Contains(action);
            box.BorderBrush = (Brush)FindResource(bad ? "DangerBrush" : "LineBrush");
            box.ToolTip = duplicates.Contains(action) ? "Bu kısayol başka bir işleve de atanmış" : invalid.Contains(action) ? "Geçersiz kısayol" : null;
        }

        var message = duplicates.Count > 0 ? "Aynı kısayol birden fazla işleve atanmış (kırmızı kutular)."
            : invalid.Count > 0 ? "Geçersiz kısayol var (kırmızı kutular)."
            : null;
        FooterStatus.Text = message ?? "";
        return message;
    }

    // ---------- Spotify ----------

    private void UpdateAuthUi(string? error = null)
    {
        var signedIn = _session.IsSignedIn;
        ConnectButton.Content = signedIn ? "Bağlantıyı kes" : "Spotify'a bağlan";
        ConnectButton.Style = (Style)FindResource(signedIn ? typeof(Button) : "PrimaryButton");

        var (dot, text, bg) = error is not null
            ? ("DangerBrush", "Sorun var", "#22F0616D")
            : signedIn ? ("AccentBrush", "Bağlı", "#221ED760") : ("SubtleTextBrush", "Bağlı değil", "#22FFFFFF");
        StatusDot.Fill = (Brush)FindResource(dot);
        StatusText.Text = text;
        StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        AuthStatus.Text = error ?? (signedIn && !_session.HasScope("user-library-modify")
            ? "♡ için bir kez bağlantıyı kesip tekrar bağlanın."
            : "");
        Anim.Enter(StatusBadge, 0, 180, 0.3);
    }

    private void OnOpenDashboard(object sender, RoutedEventArgs e) => _openBrowser(DashboardUrl);

    private void OnCopyRedirect(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(PkceAuth.RedirectUri);
        var button = (Button)sender;
        button.Content = "Kopyalandı ✓";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = "Kopyala";
        };
        timer.Start();
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
            UpdateAuthUi("Önce Client ID girin.");
            ClientIdBox.Focus();
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
            UpdateAuthUi("Giriş zaman aşımına uğradı.");
        }
        catch (Exception ex) when (ex is SpotifyAuthException or System.Net.HttpListenerException or HttpRequestException)
        {
            UpdateAuthUi(ex.Message);
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
            UpdateAuthUi(ex.Message);
        }
        catch (HttpRequestException)
        {
            UpdateAuthUi("Spotify'a ulaşılamadı.");
        }
    }

    // ---------- Save / cancel ----------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (ValidateHotkeys() is not null)
        {
            ShowSection(KeysSection);
            return;
        }

        var moods = new List<MoodDefinition>();
        var playlists = new Dictionary<string, PlaylistRef>();
        foreach (var row in _moodRows)
        {
            var name = row.Name.Text.Trim();
            if (name.Length == 0)
            {
                FooterStatus.Text = "Her ruh halinin bir ismi olmalı.";
                ShowSection(MoodsSection);
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
            s.Animations = AnimationsBox.IsChecked == true;
            s.GameAnimations = SelectedGameAnimations();
            s.IdleOpacity = IdleOpacitySlider.Value / 100;
            s.ActiveOpacity = ActiveOpacitySlider.Value / 100;
            s.Hotkeys = new Dictionary<HotkeyAction, string>(_hotkeyValues);
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
        Saved = true;
        // Shown modeless (Show, not ShowDialog), so DialogResult can't be set; just close.
        Close();
    }

    /// <summary>True when the window closed through Save (the caller can confirm it).</summary>
    public bool Saved { get; private set; }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
