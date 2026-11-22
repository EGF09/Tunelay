using System.Windows;
using Tunelay.App.Services;
using Tunelay.Core;

namespace Tunelay.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Mutex? _legacyInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, $@"Local\{AppInfo.Name}.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show($"{AppInfo.Name} zaten çalışıyor (sistem tepsisine bakın).", AppInfo.Name);
            Shutdown();
            return;
        }

        // Also hold the old name's lock: both versions at once would fight over hotkeys and the Spotify session.
        _legacyInstance = new Mutex(initiallyOwned: true, $@"Local\{AppInfo.LegacyName}.SingleInstance", out var oldNotRunning);
        if (!oldNotRunning)
        {
            MessageBox.Show(
                $"Eski sürüm ({AppInfo.LegacyName}) hâlâ açık. Tepsideki ♪ simgesinden Çıkış'ı seçip {AppInfo.Name}'i yeniden açın.",
                AppInfo.Name);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            _controller?.ShowToast("Beklenmeyen hata: " + args.Exception.Message);
            args.Handled = true;
        };

        _controller = new AppController(this);
        _controller.Start();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_controller is not null)
        {
            await _controller.DisposeAsync();
        }

        _singleInstance?.Dispose();
        _legacyInstance?.Dispose();
        base.OnExit(e);
    }
}
