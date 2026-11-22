using System.Windows;
using Spotiwidget.App.Services;

namespace Spotiwidget.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Spotiwidget.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Spotiwidget zaten çalışıyor (sistem tepsisine bakın).", "Spotiwidget");
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
        base.OnExit(e);
    }
}
