using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Tunelay.Core;
using Tunelay.Core.Settings;
using Tunelay.Core.Spotify;
using Tunelay.Native;

namespace Tunelay.App.Views;

/// <summary>First-run welcome, and the three-step wizard that connects the optional account.</summary>
public partial class WelcomeWindow : Window
{
    private const string DashboardUrl = "https://developer.spotify.com/dashboard";
    private const int LastStep = 3;
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(3);

    private readonly SettingsStore _store;
    private readonly SpotifyAuthSession _session;
    private readonly ISpotifyApi _api;
    private readonly Action<string> _openBrowser;
    private CancellationTokenSource? _login;
    private bool _closed;
    private int _step; // 0 is the welcome page

    public WelcomeWindow(SettingsStore store, SpotifyAuthSession session, ISpotifyApi api, Action<string> openBrowser,
        StartupSwitch startup, bool connect)
    {
        InitializeComponent();
        StartupBox.Bind(this, StartupToggle, StartupHint, startup);
        _store = store;
        _session = session;
        _api = api;
        _openBrowser = openBrowser;

        RedirectBox.Text = PkceAuth.RedirectUri;
        ClientIdBox.Text = store.Current.ClientId;
        if (connect)
        {
            ShowWizard();
        }
        else
        {
            Loaded += (_, _) => UseNowButton.Focus();
        }

        Closed += (_, _) =>
        {
            _closed = true;
            _login?.Cancel();
        };
    }

    /// <summary>True when the window closed because the account got connected.</summary>
    public bool Connected { get; private set; }

    /// <summary>Opens the wizard. Someone who already has a Client ID lands on the last step.</summary>
    public void ShowWizard()
    {
        if (_step == 0)
        {
            ShowStep(AccountSetup.IsValidClientId(ClientIdBox.Text.Trim()) ? LastStep : 1);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    private void ShowStep(int step)
    {
        _step = step;
        Title = step == 0 ? "Tunelay'e hoş geldiniz" : "Spotify hesabını bağla";
        IntroPage.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        WizardPage.Visibility = step == 0 ? Visibility.Collapsed : Visibility.Visible;
        var pages = new[] { Step1, Step2, Step3 };
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].Visibility = step == i + 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        StepLabel.Text = $"Adım {step} / {LastStep}";
        NextButton.Content = step == LastStep ? "Bağlan" : "Devam";
        ShowStatus("");
        UpdateNext();
    }

    private void UpdateNext()
    {
        var waiting = _login is not null;
        var typed = ClientIdBox.Text.Trim();
        var valid = AccountSetup.IsValidClientId(typed);
        ClientIdHint.Text = typed.Length > 0 && !valid ? "Client ID 32 karakterdir, yalnızca 0-9 ve a-f içerir." : "";
        ClientIdHint.Foreground = (Brush)FindResource("DangerBrush");
        ClientIdBox.IsEnabled = !waiting;
        BackButton.Content = waiting ? "Vazgeç" : "Geri";
        NextButton.IsEnabled = !waiting && (_step != LastStep || valid);
    }

    private void ShowStatus(string text, bool error = false)
    {
        Status.Text = text;
        Status.Foreground = (Brush)FindResource(error ? "DangerBrush" : "SubtleTextBrush");
        Status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnUseNow(object sender, RoutedEventArgs e) => Close();

    private void OnConnectAccount(object sender, RoutedEventArgs e) => ShowWizard();

    private void OnOpenDashboard(object sender, RoutedEventArgs e) => _openBrowser(DashboardUrl);

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PkceAuth.RedirectUri);
            CopyButton.Content = "Kopyalandı ✓";
        }
        catch (ExternalException)
        {
            RedirectBox.Focus();
            RedirectBox.SelectAll();
            ShowStatus("Pano meşgul. Seçili adresi Ctrl+C ile kopyalayın.", error: true);
        }
    }

    private void OnClientIdChanged(object sender, TextChangedEventArgs e) => UpdateNext();

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_login is not null)
        {
            _login.Cancel();
        }
        else
        {
            ShowStep(_step - 1);
        }
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step < LastStep)
        {
            ShowStep(_step + 1);
            return;
        }

        var id = ClientIdBox.Text.Trim();
        _store.Update(s => s.ClientId = id); // the session reads it from settings

        using var login = new CancellationTokenSource(LoginTimeout);
        _login = login;
        UpdateNext();
        ShowStatus("Tarayıcıda girişi tamamlayın… Tarayıcı \"INVALID_CLIENT\" gösterirse Vazgeç'e basıp Client ID ve Redirect URI'yi denetleyin.");
        string? error;
        try
        {
            error = await AccountSetup.ConnectAsync(_session, _api, _openBrowser, login.Token);
        }
        finally
        {
            _login = null;
            UpdateNext();
        }

        if (_closed)
        {
            return;
        }

        if (error is not null)
        {
            ShowStatus(error, error: true);
            Activate();
            return;
        }

        Connected = true;
        Close();
    }
}
