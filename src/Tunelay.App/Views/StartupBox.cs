using System.Windows;
using System.Windows.Controls;
using Tunelay.Core;

namespace Tunelay.App.Views;

/// <summary>The "start with Windows" switch shared by the welcome and settings windows. It applies at once.</summary>
internal static class StartupBox
{
    public static void Bind(Window window, CheckBox box, TextBlock hint, StartupSwitch startup)
    {
        void Refresh(string? error = null)
        {
            var state = startup.State;
            box.IsChecked = state == StartupState.On;
            box.IsEnabled = state != StartupState.BlockedByWindows; // only Task Manager can undo that
            hint.Text = error ?? startup.Hint;
            hint.Visibility = hint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        box.Click += (_, _) => Refresh(startup.SetEnabled(box.IsChecked == true));

        // The tray or Task Manager may have changed it meanwhile.
        window.Activated += (_, _) => Refresh();
        Refresh();
    }
}
