using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Tunelay.Core.Settings;
using Tunelay.Native;

namespace Tunelay.App.Views;

/// <summary>Mood buttons pinned to the desktop, behind all other windows. Movable in move mode.</summary>
public partial class MoodDockWindow : Window
{
    private const double EdgeMargin = 24;
    private readonly DesktopPin _pin = new();

    public MoodDockWindow()
    {
        InitializeComponent();
        MoveFrame.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                ScreenPlacement.Clamp(this);
                Moved?.Invoke(Left, Top);
            }
        };
    }

    public event Action<string>? MoodClicked;

    /// <summary>Raised after the user drags the dock, with the new position.</summary>
    public event Action<double, double>? Moved;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        WindowStyles.MakeOverlay(source.Handle); // no taskbar/Alt-Tab entry, never takes focus
        _pin.Attach(source);
    }

    public void SetMoods(IReadOnlyList<MoodDefinition> moods, string? activeId) =>
        MoodPills.Build(Moods, moods, activeId, id => MoodClicked?.Invoke(id), 13);

    /// <summary>Without an account the moods are dimmed; pressing one says how to connect.</summary>
    public void SetAccount(bool connected) => Moods.Opacity = connected ? 1 : 0.5;

    /// <summary>In move mode the dock is lifted above other windows so it can be found and dragged.</summary>
    public void SetMoveMode(bool on)
    {
        MoveFrame.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            _pin.SetEnabled(false);
            Topmost = true;
        }
        else
        {
            Topmost = false;
            _pin.SetEnabled(true);
        }
    }

    /// <summary>Restores a saved position (any monitor), or bottom-left of the primary work area by default.</summary>
    public void Place(double? left, double? top)
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = left ?? area.Left + EdgeMargin;
        Top = top ?? area.Bottom - ActualHeight - EdgeMargin;
        ScreenPlacement.Clamp(this);
    }
}
