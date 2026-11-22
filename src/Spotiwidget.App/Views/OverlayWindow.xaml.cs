using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Spotiwidget.App.ViewModels;
using Spotiwidget.Core.Settings;
using Spotiwidget.Native;

namespace Spotiwidget.App.Views;

public enum CardCommand
{
    Previous,
    PlayPause,
    Next,
    Like,
    AddToMood,
}

/// <summary>Where and how the card is shown; built from <see cref="AppSettings"/>.</summary>
public sealed record CardLayout(
    ScreenCorner Corner,
    double? Left,
    double? Anchor,
    bool AnchorBottom,
    bool DockOn,
    double IdleOpacity,
    double ActiveOpacity);

/// <summary>
/// Compact, topmost, never-activated player card. It is display-only (translucent, click-through)
/// until the interaction key unlocks it; then the buttons work and, where useful, the moods appear.
/// In move mode the whole card is a drag handle.
/// </summary>
public partial class OverlayWindow : Window
{
    private static readonly TimeSpan IdleLock = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LeaveGrace = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ToastTime = TimeSpan.FromSeconds(3);

    private readonly OverlayViewModel _vm;
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _leaveTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _topmostTimer;
    private DispatcherTimer? _previewTimer;
    private nint _hwnd;
    private CardLayout _layout = new(ScreenCorner.BottomRight, null, null, true, true, 0.6, 1);
    private bool _fullscreen;
    private bool _unlocked;
    private bool _moveMode;
    private bool _userHidden;
    private bool _mouseEntered;

    public OverlayWindow(OverlayViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        _idleTimer = new DispatcherTimer { Interval = IdleLock };
        _idleTimer.Tick += (_, _) => Lock();
        _leaveTimer = new DispatcherTimer { Interval = LeaveGrace };
        _leaveTimer.Tick += (_, _) => Lock();
        // Topmost fullscreen games re-raise themselves; win the z-order back periodically.
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => KeepOnTop();
        _toastTimer = new DispatcherTimer { Interval = ToastTime };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Anim.Fade(ToastBox, 0, 200, () =>
            {
                if (!_toastTimer.IsEnabled)
                {
                    ToastBox.Visibility = Visibility.Collapsed;
                }
            });
        };
        vm.PropertyChanged += OnViewModelChanged;

        MouseEnter += (_, _) =>
        {
            _mouseEntered = true;
            _leaveTimer.Stop();
        };
        MouseMove += (_, _) =>
        {
            _leaveTimer.Stop();
            RestartIdle();
        };
        MouseLeave += (_, _) =>
        {
            if (_unlocked && _mouseEntered)
            {
                _leaveTimer.Start();
            }
        };
        Card.MouseWheel += (_, e) => VolumeWheel?.Invoke(e.Delta > 0 ? 5 : -5);
        MoveFrame.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            DragMove();
            ScreenPlacement.Clamp(this);
            var area = ScreenPlacement.WorkAreaNear(this, Left + ActualWidth / 2, Top + ActualHeight / 2);
            var anchorBottom = Top + ActualHeight / 2 > area.Top + area.Height / 2;
            _layout = _layout with { Left = Left, Anchor = anchorBottom ? Top + ActualHeight : Top, AnchorBottom = anchorBottom };
            OrderToast();
            Moved?.Invoke(Left, _layout.Anchor!.Value, anchorBottom);
        };
        SizeChanged += (_, e) => Place(e.PreviousSize);
    }

    public event Action<CardCommand>? Command;

    public event Action<string>? MoodClicked;

    public event Action<int>? VolumeWheel;

    /// <summary>Raised when the user releases the volume slider, with the chosen percent.</summary>
    public event Action<int>? VolumeSet;

    /// <summary>Raised when an unlock ends (timeout, mouse left, Esc, key pressed again).</summary>
    public event Action? Locked;

    /// <summary>Raised after a drag in move mode: left, anchor edge (top or bottom), whether it is the bottom edge.</summary>
    public event Action<double, double, bool>? Moved;

    public bool IsUnlocked => _unlocked;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.MakeOverlay(_hwnd);
        Apply();
    }

    public void Configure(CardLayout layout, IReadOnlyList<MoodDefinition> moods, string? activeMood)
    {
        _layout = layout;
        OrderToast();
        SetMoods(moods, activeMood);
        Apply();
        Place(null);
    }

    public void SetMoods(IReadOnlyList<MoodDefinition> moods, string? activeMood) =>
        MoodPills.Build(MoodRow, moods, activeMood, id => MoodClicked?.Invoke(id), 11);

    public void SetFullscreen(bool fullscreen)
    {
        if (_fullscreen == fullscreen)
        {
            return;
        }

        _fullscreen = fullscreen;
        if (fullscreen)
        {
            _topmostTimer.Start();
        }
        else
        {
            _topmostTimer.Stop();
        }

        Apply();
        KeepOnTop();
    }

    /// <summary>Puts the card above other always-on-top windows (without taking focus).</summary>
    public void KeepOnTop()
    {
        if (_hwnd != 0 && IsVisible)
        {
            WindowStyles.BringToTopmost(_hwnd);
        }
    }

    public void SetUserHidden(bool hidden)
    {
        _userHidden = hidden;
        Apply();
    }

    /// <summary>Interaction key: toggles the clickable state. Returns the new state.</summary>
    public bool ToggleUnlock()
    {
        if (_unlocked)
        {
            Lock();
            return false;
        }

        if (_moveMode)
        {
            return false;
        }

        _unlocked = true;
        _mouseEntered = false;
        Apply();
        RestartIdle();
        return true;
    }

    public void Lock()
    {
        StopTimers();
        if (!_unlocked)
        {
            return;
        }

        _unlocked = false;
        Apply();
        Locked?.Invoke();
    }

    public void SetMoveMode(bool on)
    {
        if (on)
        {
            Lock();
        }

        _moveMode = on;
        Apply();
    }

    public void ShowToast(string message)
    {
        _vm.Toast = message;
        var wasHidden = ToastBox.Visibility != Visibility.Visible;
        ToastBox.Visibility = Visibility.Visible;
        if (wasHidden || ToastBox.Opacity < 1)
        {
            Anim.Enter(ToastBox, 6, 200);
        }

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>Shows the card at <paramref name="opacity"/> for a moment (live preview from settings).</summary>
    public void PreviewOpacity(double opacity)
    {
        Anim.Fade(this, opacity, 120);
        _previewTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _previewTimer.Stop();
        _previewTimer.Tick -= OnPreviewEnded;
        _previewTimer.Tick += OnPreviewEnded;
        _previewTimer.Start();
    }

    private void OnPreviewEnded(object? sender, EventArgs e)
    {
        _previewTimer!.Stop();
        Apply();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // New song: the track row fades and slides in.
            case nameof(OverlayViewModel.Title) when IsVisible:
                Anim.Enter(TrackRow, 5, 240, 0.2);
                break;

            // Liked: the heart pops.
            case nameof(OverlayViewModel.IsLiked) when _vm.IsLiked == true:
                Anim.Pop(LikeButton);
                break;
        }
    }

    private void Apply()
    {
        var passive = !_unlocked && !_moveMode; // display-only

        if (_hwnd != 0)
        {
            WindowStyles.SetClickThrough(_hwnd, passive);
        }

        Anim.Fade(this, _moveMode ? 1 : passive ? _layout.IdleOpacity : _layout.ActiveOpacity, 200);
        Reveal(Controls, !passive);
        Controls.IsHitTestVisible = !_moveMode;
        MoveFrame.Visibility = _moveMode ? Visibility.Visible : Visibility.Collapsed;
        Reveal(MoodRow, _unlocked && (_fullscreen || !_layout.DockOn));

        if (_userHidden && IsVisible)
        {
            Hide();
        }
        else if (!_userHidden && !IsVisible && _hwnd != 0)
        {
            Show();
        }
    }

    /// <summary>
    /// Positions the card: at the chosen corner, or at the spot the user dragged it to. A card dragged
    /// into the lower half of a screen keeps its bottom edge fixed, so it grows upward when it expands.
    /// </summary>
    private void Place(Size? previous)
    {
        var height = ActualHeight;
        if (_layout.Left is { } left && _layout.Anchor is { } anchor)
        {
            Left = left;
            Top = _layout.AnchorBottom ? anchor - height : anchor;
            ScreenPlacement.Clamp(this);
            return;
        }

        var area = SystemParameters.WorkArea;
        var right = _layout.Corner is ScreenCorner.TopRight or ScreenCorner.BottomRight;
        var bottom = _layout.Corner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight;
        Left = right ? area.Right - ActualWidth : area.Left;
        Top = bottom ? area.Bottom - height : area.Top;
    }

    /// <summary>Toast sits on the side of the card facing the screen center.</summary>
    private void OrderToast()
    {
        var bottom = _layout.Left is not null
            ? _layout.AnchorBottom
            : _layout.Corner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight;
        CardStack.Children.Remove(ToastBox);
        CardStack.Children.Insert(bottom ? 0 : 1, ToastBox);
    }

    /// <summary>Shows a section with a short slide-in; hides it at once (so the card shrinks immediately).</summary>
    private static void Reveal(UIElement element, bool show)
    {
        if (!show)
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        if (element.Visibility != Visibility.Visible)
        {
            element.Visibility = Visibility.Visible;
            Anim.Enter(element, 8, 200);
        }
    }

    private void RestartIdle()
    {
        _idleTimer.Stop();
        if (_unlocked)
        {
            _idleTimer.Start();
        }
    }

    private void StopTimers()
    {
        _idleTimer.Stop();
        _leaveTimer.Stop();
    }

    private void Raise(CardCommand command)
    {
        RestartIdle();
        Command?.Invoke(command);
    }

    private void OnVolumeCommitted(object sender, MouseButtonEventArgs e)
    {
        RestartIdle();
        // Dispatch after the slider has applied the click/drag position.
        Dispatcher.BeginInvoke(() => VolumeSet?.Invoke((int)Math.Round(VolumeSlider.Value)));
    }

    private void OnPrevious(object sender, RoutedEventArgs e) => Raise(CardCommand.Previous);

    private void OnPlayPause(object sender, RoutedEventArgs e) => Raise(CardCommand.PlayPause);

    private void OnNext(object sender, RoutedEventArgs e) => Raise(CardCommand.Next);

    private void OnLike(object sender, RoutedEventArgs e) => Raise(CardCommand.Like);

    private void OnAddToMood(object sender, RoutedEventArgs e) => Raise(CardCommand.AddToMood);
}
