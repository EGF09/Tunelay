using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using H.NotifyIcon;
using Tunelay.App.ViewModels;
using Tunelay.App.Views;
using Tunelay.Core;
using Tunelay.Core.Moods;
using Tunelay.Core.Playback;
using Tunelay.Core.Settings;
using Tunelay.Core.Spotify;
using Tunelay.Native;

namespace Tunelay.App.Services;

/// <summary>Composition root: builds the services, owns the player card, mood dock, tray icon, hotkeys and fullscreen handling.</summary>
public sealed class AppController : IAsyncDisposable
{
    private readonly Application _app;
    private readonly SettingsStore _settings;
    private readonly SpotifyAuthSession _session;
    private readonly SpotifyClient _api;
    private readonly SpotifyStarter _starter;
    private MenuItem? _downloadItem;
    private readonly PlaybackRouter _playback;
    private readonly WindowsMediaSource _local;
    private readonly string _appData;
    private WelcomeWindow? _welcome;
    private readonly MoodService _moods;
    private readonly OverlayViewModel _vm = new();
    private readonly OverlayWindow _card;
    private readonly MoodDockWindow _dock = new();
    private readonly GlobalHotkeys _hotkeys = new();
    private readonly ForegroundMonitor _foreground = new();
    private readonly BorderlessWindow _borderless = new();
    private TaskbarIcon? _tray;
    private MenuItem? _toggleItem;
    private MenuItem? _moveItem;
    private SettingsWindow? _settingsWindow;
    private int _escId;
    private bool _userHidden;
    private bool _fullscreen;
    private bool _moveMode;
    private bool _quietSave; // position-only saves don't need a full re-apply

    private readonly bool _background;
    private readonly StartupSwitch _startup;
    private MenuItem? _startupItem;

    /// <param name="app">The running application.</param>
    /// <param name="background">Started with Windows: open no window and stay quiet while offline.</param>
    public AppController(Application app, bool background = false)
    {
        _app = app;
        _background = background;
        _startup = new StartupSwitch(new StartupRegistration(), Environment.ProcessPath ?? "");
        _appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = AppInfo.ResolveDataDirectory(_appData);
        _settings = new SettingsStore(Path.Combine(dir, "settings.json"));

        var tokenStore = new TokenStore(Path.Combine(dir, "tokens.bin"), new DpapiProtector());
        _session = new SpotifyAuthSession(new TokenEndpointClient(new HttpClient()), tokenStore, () => _settings.Current.ClientId);

        var handler = new SpotifyApiHandler(_session)
        {
            InnerHandler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
        };
        var launcher = new SpotifyLauncher();
        _starter = new SpotifyStarter(launcher);
        _api = new SpotifyClient(new HttpClient(handler) { BaseAddress = SpotifyClient.BaseAddress, Timeout = TimeSpan.FromSeconds(15) }, _starter);
        _local = new WindowsMediaSource(launcher, () => !_session.IsSignedIn);
        _playback = new PlaybackRouter(new PlaybackService(_api, () => _session.IsSignedIn, background), _local, () => _session.IsSignedIn);
        _moods = new MoodService(_api, _settings);

        _card = new OverlayWindow(_vm);
    }

    public void Start()
    {
        _card.Show();
        _card.Command += OnCardCommand;
        _card.MoodClicked += SwitchMood;
        _card.VolumeWheel += delta => _ = _playback.ChangeVolumeAsync(delta);
        _card.VolumeSet += percent =>
        {
            _vm.Volume = percent; // show it immediately; the next poll confirms
            _ = _playback.SetVolumeAsync(percent);
        };
        _card.Locked += ReleaseEsc;

        _card.Moved += (left, anchor, bottom) => SavePosition(s =>
        {
            s.CardLeft = left;
            s.CardAnchor = anchor;
            s.CardAnchorBottom = bottom;
        });

        _dock.MoodClicked += SwitchMood;
        _dock.Moved += (left, top) => SavePosition(s =>
        {
            s.DockLeft = left;
            s.DockTop = top;
        });

        _playback.StateChanged += state => Ui(() => _vm.ApplyPlayback(state, _playback.Capabilities));
        _playback.LikedChanged += liked => Ui(() => _vm.IsLiked = liked);
        _playback.Error += message => Ui(() => ShowToast(message));
        _playback.Info += message => Ui(() => ShowToast(message));
        _session.SignedInChanged += signedIn => Ui(() =>
        {
            _dock.SetAccount(signedIn);
            _playback.AccountChanged();
            ApplyMoods();
            if (!signedIn)
            {
                ShowToast("Hesap bağlı değil: girişsiz moddasınız. Bağlamak için Ayarlar → Spotify.");
            }
        });
        _local.NotInstalled += () => Ui(ShowDownload);
        _moods.ActiveMoodChanged += _ => Ui(ApplyMoods);

        // "Opening" stays up for the whole wait; the result replaces it.
        _starter.Starting += () => Ui(() => _card.ShowToast("Spotify açılıyor…", TimeSpan.FromSeconds(22)));
        _starter.Ready += () => Ui(() => ShowToast("Spotify hazır, çalınıyor…"));
        _starter.NotInstalled += () => Ui(ShowDownload);

        _foreground.Changed += OnForegroundChanged;
        _settings.Saved += _ => Ui(() =>
        {
            if (_settingsWindow is null && !_quietSave)
            {
                ApplySettings();
            }
        });

        CreateTray();
        ApplySettings();
        _vm.ApplyPlayback(null, _playback.Capabilities);
        _dock.SetAccount(_session.IsSignedIn);
        _foreground.Refresh();
        _playback.Start();

        if (_background)
        {
            return; // no windows or reminders at sign-in; they come at the next start by hand
        }

        if (AccountSetup.NeedsWelcome(_settings.Current, _session.IsSignedIn))
        {
            OpenWelcome(connect: false);
        }
        else if (_session.IsSignedIn && !_session.HasScope("user-library-modify"))
        {
            ShowToast("Beğenme (♡) için izin gerekli: Ayarlar → Spotify → İzinleri yenile.");
        }
    }

    public void ShowToast(string message) => _card.ShowToast(message);

    private void ShowDownload()
    {
        if (_downloadItem is not null)
        {
            _downloadItem.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// For what needs an account. A click on the desktop opens the wizard; a shortcut, or a click during
    /// a game, only says how to connect, so no window lands over the game.
    /// </summary>
    private bool RequireAccount(bool clicked)
    {
        if (_session.IsSignedIn)
        {
            return true;
        }

        if (clicked && !_fullscreen)
        {
            OpenWelcome(connect: true);
        }
        else
        {
            ShowToast("Bunun için hesap gerekli: Ayarlar → Spotify → Hesap bağla.");
        }

        return false;
    }

    private void SwitchMood(string mood)
    {
        if (RequireAccount(clicked: true))
        {
            _ = RunMoodAsync(() => _moods.SwitchAsync(mood));
        }
    }

    private void OpenWelcome(bool connect)
    {
        if (_welcome is not null)
        {
            if (connect)
            {
                _welcome.ShowWizard();
            }

            _welcome.Activate();
            return;
        }

        var window = new WelcomeWindow(_settings, _session, _api, OpenBrowser, _startup, connect);
        _welcome = window;
        window.Closed += (_, _) =>
        {
            _welcome = null;
            if (!_settings.Current.WelcomeSeen)
            {
                _settings.Update(s => s.WelcomeSeen = true);
            }

            if (window.Connected)
            {
                ShowToast("Spotify bağlandı ✓ Ruh hallerinize çalma listesi seçin.");
                OpenSettings();
            }
        };
        window.Show();
        window.Activate();
    }

    private async Task<bool> DisconnectAsync()
    {
        var clean = await AccountSetup.DisconnectAsync(_session, _settings, _appData);
        WindowsMediaSource.DeleteCachedArt();
        return clean;
    }

    private void ApplySettings()
    {
        var s = _settings.Current;
        UpdateAnimations(s.Animations);
        _card.Configure(
            new CardLayout(s.Corner, s.CardLeft, s.CardAnchor, s.CardAnchorBottom, s.ShowMoodDock, s.IdleOpacity, s.ActiveOpacity),
            s.Moods,
            s.ActiveMood);
        ApplyMoods();
        UpdateDock();
        RegisterHotkeys(s);
    }

    /// <summary>
    /// Animations run when the user wants them and Windows allows them. In a fullscreen app the
    /// "in-game" level decides: off, light (30 fps) or full (60 fps).
    /// </summary>
    private void UpdateAnimations(bool wanted, GameAnimationLevel? gameLevel = null)
    {
        var level = gameLevel ?? _settings.Current.GameAnimations;
        var allowed = wanted && SystemParameters.ClientAreaAnimation;
        Anim.Enabled = allowed && (!_fullscreen || level != GameAnimationLevel.Off);
        Anim.FrameRate = _fullscreen && level == GameAnimationLevel.Light ? 30 : 60;
    }

    /// <summary>Settings window live preview: apply unsaved look values to the card.</summary>
    private void OnSettingsPreview(SettingsPreview p)
    {
        var s = _settings.Current;
        UpdateAnimations(p.Animations, p.GameAnimations);
        _card.Configure(
            new CardLayout(p.Corner, s.CardLeft, s.CardAnchor, s.CardAnchorBottom, s.ShowMoodDock, p.IdleOpacity, p.ActiveOpacity),
            s.Moods,
            s.ActiveMood);
        if (p.ShowActive)
        {
            _card.PreviewOpacity(p.ActiveOpacity);
        }
    }

    private void ApplyMoods()
    {
        var s = _settings.Current;
        _vm.ApplyMood(s.FindMood(s.ActiveMood));
        _card.SetMoods(s.Moods, s.ActiveMood);
        _dock.SetMoods(s.Moods, s.ActiveMood);
    }

    private void SavePosition(Action<AppSettings> change)
    {
        _quietSave = true;
        try
        {
            _settings.Update(change);
        }
        finally
        {
            _quietSave = false;
        }
    }

    /// <summary>Move mode: the card and the mood dock get a dashed frame and can be dragged anywhere, on any monitor.</summary>
    private void SetMoveMode(bool on)
    {
        var was = _moveMode;
        _moveMode = on;
        _card.SetMoveMode(on);
        _dock.SetMoveMode(on);
        if (_moveItem is not null)
        {
            _moveItem.IsChecked = on;
        }

        ReleaseEsc();
        if (on)
        {
            GrabEsc();
            if (!was)
            {
                ShowToast("Taşıma modu: kartı ve paneli sürükleyin. Bitirmek için Esc veya Ctrl+Alt+D.");
            }
        }
        else if (was)
        {
            ShowToast("Konumlar kaydedildi.");
        }
    }

    private void UpdateDock()
    {
        var s = _settings.Current;
        if (s.ShowMoodDock && !_userHidden)
        {
            if (!_dock.IsVisible)
            {
                _dock.Show();
            }

            _dock.Place(s.DockLeft, s.DockTop);
        }
        else if (_dock.IsVisible)
        {
            _dock.Hide();
        }
    }

    private void RegisterHotkeys(AppSettings s)
    {
        SuspendHotkeys();
        var failed = new List<string>();
        foreach (var (action, combo) in s.Hotkeys)
        {
            if (string.IsNullOrWhiteSpace(combo))
            {
                continue;
            }

            if (!HotkeyGesture.TryParse(combo, out var gesture) || !_hotkeys.Register(gesture!, () => OnHotkey(action)))
            {
                failed.Add(combo);
            }
        }

        // Re-registering drops Esc, so take it again while something is open that Esc closes.
        if (_moveMode || _card.IsUnlocked)
        {
            GrabEsc();
        }

        if (failed.Count > 0)
        {
            ShowToast("Kısayol kullanılamıyor (başka bir uygulama kullanıyor olabilir): " + string.Join(", ", failed));
        }
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleMenu:
                ToggleInteraction();
                break;
            case HotkeyAction.PlayPause:
                _ = _playback.TogglePlayPauseAsync();
                break;
            case HotkeyAction.Next:
                _card.Feedback(CardCommand.Next);
                _ = _playback.NextAsync();
                break;
            case HotkeyAction.Previous:
                _card.Feedback(CardCommand.Previous);
                _ = _playback.PreviousAsync();
                break;
            case HotkeyAction.VolumeUp:
                _ = _playback.ChangeVolumeAsync(10);
                break;
            case HotkeyAction.VolumeDown:
                _ = _playback.ChangeVolumeAsync(-10);
                break;
            case HotkeyAction.Like:
                if (RequireAccount(clicked: false))
                {
                    _ = _playback.ToggleLikeAsync();
                }

                break;
            case HotkeyAction.SaveToMood:
                if (RequireAccount(clicked: false))
                {
                    _ = AddToMoodAsync();
                }

                break;
            case HotkeyAction.CycleMood:
                if (RequireAccount(clicked: false))
                {
                    _ = RunMoodAsync(() => _moods.CycleAsync());
                }

                break;
            case HotkeyAction.Borderless:
                ToggleBorderless();
                break;
            case HotkeyAction.MoveMode:
                SetMoveMode(!_moveMode);
                break;
            case HotkeyAction.Shuffle:
                _ = _playback.ToggleShuffleAsync();
                break;
            case HotkeyAction.Repeat:
                _ = _playback.CycleRepeatAsync();
                break;
        }
    }

    private void ToggleBorderless()
    {
        var message = _borderless.ToggleForeground() switch
        {
            BorderlessResult.MadeBorderless => "Kenarlıksız tam ekran açıldı. Geri almak için kısayola tekrar basın.",
            BorderlessResult.Restored => "Pencere eski hâline döndü.",
            BorderlessResult.AlreadyFrameless =>
                "Oyun kendi tam ekranında (F11). Önce oyunda F11 ile pencere moduna dönün, sonra bu kısayola basın.",
            _ => "Önce oyunun penceresine tıklayın, sonra kısayola basın.",
        };
        ShowToast(message);
        _foreground.Refresh();
        _card.KeepOnTop();
    }

    /// <summary>Interaction key: makes the card clickable (and, where the dock isn't reachable, shows the moods).</summary>
    private void ToggleInteraction()
    {
        if (_moveMode)
        {
            SetMoveMode(false);
        }

        if (_card.ToggleUnlock())
        {
            _playback.RefreshSoon();
            // Esc locks the card again; it is only grabbed while the card is unlocked.
            GrabEsc();
        }
    }

    /// <summary>Esc ends move mode, or locks the unlocked card.</summary>
    private void GrabEsc()
    {
        ReleaseEsc();
        _escId = _hotkeys.RegisterKey(Key.Escape, () =>
        {
            if (_moveMode)
            {
                SetMoveMode(false);
            }
            else
            {
                _card.Lock();
            }
        });
    }

    private void SuspendHotkeys()
    {
        _hotkeys.UnregisterAll();
        _escId = 0;
    }

    private void ReleaseEsc()
    {
        if (_escId != 0)
        {
            _hotkeys.Unregister(_escId);
            _escId = 0;
        }
    }

    private void OnCardCommand(CardCommand command)
    {
        switch (command)
        {
            case CardCommand.Previous:
                _ = _playback.PreviousAsync();
                break;
            case CardCommand.PlayPause:
                _ = _playback.TogglePlayPauseAsync();
                break;
            case CardCommand.Next:
                _ = _playback.NextAsync();
                break;
            case CardCommand.Like:
                if (RequireAccount(clicked: true))
                {
                    _ = _playback.ToggleLikeAsync();
                }

                break;
            case CardCommand.Shuffle:
                _ = _playback.ToggleShuffleAsync();
                break;
            case CardCommand.Repeat:
                _ = _playback.CycleRepeatAsync();
                break;
            case CardCommand.AddToMood:
                if (RequireAccount(clicked: true))
                {
                    _ = AddToMoodAsync();
                }

                break;
        }
    }

    private Task AddToMoodAsync() => RunMoodAsync(async () =>
    {
        var result = await _moods.SaveTrackToActiveAsync(_playback.Current?.Item);
        if (result.Ok)
        {
            Ui(() => _card.Feedback(CardCommand.AddToMood)); // the drop means "added"
        }

        return result;
    });

    private async Task RunMoodAsync(Func<Task<MoodResult>> action)
    {
        var result = await action();
        Ui(() => ShowToast(result.Message));
        _playback.RefreshSoon();
    }

    private void OnForegroundChanged(ForegroundInfo info)
    {
        _fullscreen = info.IsFullscreen;
        UpdateAnimations(_settings.Current.Animations);
        _card.SetFullscreen(_fullscreen);
        // Any focus change can push the card under a topmost game window; raise it right away.
        _card.KeepOnTop();
        UpdatePolling();
    }

    private void UpdatePolling() =>
        _playback.Mode = _fullscreen || _userHidden ? PollMode.Background : PollMode.Visible;

    private void SetUserHidden(bool hidden)
    {
        _userHidden = hidden;
        _card.SetUserHidden(hidden);
        UpdateDock();
        UpdatePolling();
        if (_toggleItem is not null)
        {
            _toggleItem.Header = hidden ? "Widget'ı göster" : "Widget'ı gizle";
        }
    }

    private void CreateTray()
    {
        _toggleItem = new MenuItem { Header = "Widget'ı gizle" };
        _toggleItem.Click += (_, _) => SetUserHidden(!_userHidden);
        _moveItem = new MenuItem { Header = "Taşıma modu", IsCheckable = true, InputGestureText = "Ctrl+Alt+D" };
        _moveItem.Click += (_, _) => SetMoveMode(_moveItem.IsChecked);
        var settingsItem = new MenuItem { Header = "Ayarlar…" };
        settingsItem.Click += (_, _) => OpenSettings();
        var exitItem = new MenuItem { Header = "Çıkış" };
        exitItem.Click += (_, _) => _app.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(_moveItem);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(settingsItem);
        _startupItem = new MenuItem();
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);
        menu.Opened += (_, _) => UpdateStartupItem();
        UpdateStartupItem();
        _downloadItem = new MenuItem { Header = "Spotify'ı indir…", Visibility = Visibility.Collapsed };
        _downloadItem.Click += (_, _) => OpenBrowser("https://www.spotify.com/download/windows/");
        menu.Items.Add(_downloadItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _tray = new TaskbarIcon
        {
            ToolTipText = "Tunelay",
            ContextMenu = menu,
            IconSource = new GeneratedIconSource
            {
                Text = "♪",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0xD7, 0x60)),
                FontSize = 48,
            },
        };
        _tray.TrayMouseDoubleClick += (_, _) => OpenSettings();
        ShowTrayIcon();
    }

    private void ShowTrayIcon()
    {
        try
        {
            _tray?.ForceCreate(enablesEfficiencyMode: false);
        }
        catch (InvalidOperationException)
        {
            // Right after sign-in the taskbar may not be ready yet.
            var retry = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            retry.Tick += (_, _) =>
            {
                retry.Stop();
                ShowTrayIcon();
            };
            retry.Start();
        }
    }

    private void UpdateStartupItem()
    {
        if (_startupItem is null)
        {
            return;
        }

        var state = _startup.State;
        _startupItem.IsChecked = state == StartupState.On;
        _startupItem.IsEnabled = state != StartupState.BlockedByWindows;
        _startupItem.Header = state == StartupState.BlockedByWindows
            ? "Windows ile başlat (Görev Yöneticisi'nde kapalı)"
            : "Windows ile başlat";
    }

    private void ToggleStartup()
    {
        var on = _startup.State != StartupState.On;
        var error = _startup.SetEnabled(on);
        UpdateStartupItem();
        var hint = on ? _startup.Hint : "";
        ShowToast(error ?? (hint.Length > 0 ? hint : on ? "Windows ile başlayacak ✓" : "Windows ile başlamayacak."));
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        // The widget keeps working while settings are open. Shortcuts pause only while one is being
        // recorded, because Windows never delivers a registered combo to the recording box.
        var window = new SettingsWindow(_settings, _session, _api, OpenBrowser,
            () => OpenWelcome(connect: true), DisconnectAsync, _startup, OnSettingsPreview);
        _settingsWindow = window;
        window.RecordingHotkeyChanged += recording =>
        {
            if (recording)
            {
                SuspendHotkeys();
            }
            else
            {
                RegisterHotkeys(_settings.Current);
            }
        };
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ApplySettings(); // also undoes any unsaved preview
            _playback.RefreshSoon();
            if (window.Saved)
            {
                ShowToast("Ayarlar kaydedildi ✓");
            }
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static void OpenBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void Ui(Action action)
    {
        if (_app.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _app.Dispatcher.BeginInvoke(action);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _borderless.RestoreAll(); // never leave a game stuck without its frame
        _foreground.Dispose();
        _hotkeys.Dispose();
        _tray?.Dispose();
        await _playback.DisposeAsync();
    }
}
