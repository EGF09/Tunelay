using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Spotiwidget.App.Views;

/// <summary>
/// Short, event-driven animations on opacity and transforms only (cheap to compose).
/// Nothing loops. When <see cref="Enabled"/> is false (setting off, Windows animations off,
/// or a fullscreen game in front) every call jumps straight to the end state.
/// </summary>
public static class Anim
{
    private const int FrameRate = 60;
    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static bool Enabled { get; set; } = true;

    /// <summary>Animates <see cref="UIElement.Opacity"/> (or a window's) to <paramref name="to"/>.</summary>
    public static void Fade(UIElement element, double to, int ms = 180, Action? done = null)
    {
        if (!Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            done?.Invoke();
            return;
        }

        var animation = Make(element.Opacity, to, ms);
        if (done is not null)
        {
            animation.Completed += (_, _) => done();
        }

        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>Fades in while sliding a few pixels into place (used for appearing content).</summary>
    public static void Enter(UIElement element, double fromY = 6, int ms = 200, double fromOpacity = 0)
    {
        var shift = Translate(element);
        if (!Enabled)
        {
            shift.BeginAnimation(TranslateTransform.YProperty, null);
            shift.Y = 0;
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }

        shift.BeginAnimation(TranslateTransform.YProperty, Make(fromY, 0, ms));
        element.BeginAnimation(UIElement.OpacityProperty, Make(fromOpacity, 1, ms));
    }

    /// <summary>A quick "pop": grow and settle back (e.g. the heart when a song is liked).</summary>
    public static void Pop(UIElement element, double peak = 1.3, int ms = 260)
    {
        if (!Enabled)
        {
            return;
        }

        var scale = Scale(element);
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(0.4), EaseOut));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), EaseOut));
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    /// <summary>Fades a (non-frozen) brush in from transparent.</summary>
    public static void BrushIn(SolidColorBrush brush, int ms = 220)
    {
        if (Enabled && !brush.IsFrozen)
        {
            brush.BeginAnimation(Brush.OpacityProperty, Make(0, 1, ms));
        }
    }

    private static DoubleAnimation Make(double from, double to, int ms)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EaseOut };
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        return animation;
    }

    private static TranslateTransform Translate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform t && !t.IsFrozen)
        {
            return t;
        }

        var created = new TranslateTransform();
        element.RenderTransform = created;
        return created;
    }

    private static ScaleTransform Scale(UIElement element)
    {
        if (element.RenderTransform is ScaleTransform s && !s.IsFrozen)
        {
            return s;
        }

        var created = new ScaleTransform(1, 1);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = created;
        return created;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
