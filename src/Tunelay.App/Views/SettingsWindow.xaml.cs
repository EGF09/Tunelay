using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Tunelay.App.ViewModels;
using Tunelay.Core;
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

    private readonly Action _connect;
    private readonly Func<Task<bool>> _disconnect;

    /// <param name="connect">Opens the connect wizard.</param>
    /// <param name="disconnect">Signs out and deletes the Spotify data; false when a file couldn't be cleaned.</param>
    /// <param name="startup">The "start with Windows" switch; it applies without Kaydet.</param>
    public SettingsWindow(SettingsStore store, SpotifyAuthSession session, ISpotifyApi api, Action<string> openBrowser,
        Action connect, Func<Task<bool>> disconnect, StartupSwitch startup, Action<SettingsPreview>? preview = null)
    {
        InitializeComponent();
        StartupBox.Bind(this, StartupToggle, StartupHint, startup);
        _store = store;
        _session = session;
        _api = api;
        _openBrowser = openBrowser;
        _connect = connect;
        _disconnect = disconnect;
        _preview = preview;

        var s = store.Current;
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
        _session.SignedInChanged += OnSignedInChanged;
        Closed += (_, _) =>
        {
            _session.SignedInChanged -= OnSignedInChanged;
            _loginCts?.Cancel();
        };
    }

    // The wizard, or a revoked grant, can change the account while this window is open.
    private void OnSignedInChanged(bool signedIn) => Dispatcher.BeginInvoke(async () =>
    {
        UpdateAuthUi();
        await LoadPlaylistsAsync();
    });

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
                SetRecording(box);
                box.Text = PressKeyText;
                box.Foreground = (Brush)FindResource("AccentBrush");
            };
            box.LostKeyboardFocus += (_, _) =>
            {
                ShowHotkey(action, box);
                SetRecording(null);
            };
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
                        StopRecording();
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

                StopRecording();
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

    /// <summary>True while a shortcut box waits for keys, so the app can release its global shortcuts.</summary>
    public event Action<bool>? RecordingHotkeyChanged;

    private TextBox? _recordingBox;

    private void SetRecording(TextBox? box)
    {
        var was = _recordingBox is not null;
        _recordingBox = box;
        if (was != (box is not null))
        {
            RecordingHotkeyChanged?.Invoke(box is not null);
        }
    }

    private void StopRecording()
    {
        // Clear the remembered focus too, or coming back to the window would start recording again.
        FocusManager.SetFocusedElement(this, null);
        Keyboard.ClearFocus();
        SetRecording(null);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_recordingBox is not null)
        {
            StopRecording();
        }
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        // A click anywhere else ends recording; empty areas don't take focus by themselves.
        if (_recordingBox is { IsMouseOver: false })
        {
            StopRecording();
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
        ConnectButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.Visibility = DisconnectButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;

        var (dot, text, bg) = error is not null
            ? ("DangerBrush", "Sorun var", "#22F0616D")
            : signedIn ? ("AccentBrush", "Bağlı", "#221ED760") : ("SubtleTextBrush", "Bağlı değil", "#22FFFFFF");
        StatusDot.Fill = (Brush)FindResource(dot);
        StatusText.Text = text;
        StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        AuthStatus.Text = error ?? (signedIn && !_session.HasScope("user-library-modify")
            ? "♡ için İzinleri yenile'ye basın."
            : "");
        Anim.Enter(StatusBadge, 0, 180, 0.3);
    }

    private void OnConnect(object sender, RoutedEventArgs e) => _connect();

    /// <summary>Signs in again over the current session, so nothing is lost if the browser step is abandoned.</summary>
    private async void OnRefreshPermissions(object sender, RoutedEventArgs e)
    {
        using var login = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _loginCts = login;
        RefreshButton.IsEnabled = DisconnectButton.IsEnabled = false;
        AuthStatus.Text = "Tarayıcıda onaylayın…";
        try
        {
            var error = await AccountSetup.ConnectAsync(_session, _api, _openBrowser, login.Token);
            // Queued, so it lands after the refresh that SignedInChanged queued.
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateAuthUi(error);
                if (error is null)
                {
                    AuthStatus.Text = "İzinler yenilendi ✓";
                }
            }, DispatcherPriority.Background);
        }
        finally
        {
            _loginCts = null;
            RefreshButton.IsEnabled = DisconnectButton.IsEnabled = true;
        }
    }

    private async void OnDisconnect(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Bu bilgisayardaki Spotify verileri silinecek: oturum ve ruh hallerine seçtiğiniz çalma listeleri "
            + "(eski Spotiwidget klasöründeki kopyaları dahil). Geri alınamaz.\n\n"
            + "Ruh halleriniz, görünüm, kısayollar ve Client ID kalır. Spotify'daki listelerinize dokunulmaz.\n\n"
            + "Bağlantı kesilsin mi?",
            "Bağlantıyı kes", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        // Cleared first, or Kaydet would write the deleted mappings back.
        _playlists = new();
        foreach (var row in _moodRows)
        {
            row.Playlist.SelectedItem = null;
            row.Playlist.ItemsSource = _playlists;
        }

        var clean = await _disconnect();
        await Dispatcher.InvokeAsync(() => UpdateAuthUi(clean
            ? null
            : "Bağlantı kesildi ama bazı dosyalar temizlenemedi: %AppData% içindeki Tunelay ve Spotiwidget klasörlerini denetleyin."),
            DispatcherPriority.Background);
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
            if (!_session.IsSignedIn)
            {
                return; // disconnected while loading
            }

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
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
