using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Spotiwidget.App.Views;

/// <summary>
/// Short, event-driven animations on opacity and transforms only (cheap to compose).
/// Nothing loops. When <see cref="Enabled"/> is false (setting off, Windows animations off,
/// or the in-game level is "off") every call jumps straight to the end state.
/// Every element gets one Scale + Rotate + Translate group, so effects can combine.
/// Button effects are applied to the glyph inside the button, which is clipped by the button.
/// </summary>
public static class Anim
{
    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseInOut = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });

    public static bool Enabled { get; set; } = true;

    /// <summary>Frames per second for new animations (30 in a game on the "light" level).</summary>
    public static int FrameRate { get; set; } = 60;

    /// <summary>A key frame: value at a point (0–1) of the timeline; <c>Jump</c> = switch instantly.</summary>
    private readonly record struct K(double Value, double At, bool Jump = false);

    // ---------- Entrances / fades ----------

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
        var shift = Parts(element).Translate;
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

    /// <summary>Fades a (non-frozen) brush in from transparent.</summary>
    public static void BrushIn(SolidColorBrush brush, int ms = 220)
    {
        if (Enabled && !brush.IsFrozen)
        {
            brush.BeginAnimation(Brush.OpacityProperty, Make(0, 1, ms));
        }
    }

    // ---------- Glyph effects (all end at rest) ----------

    /// <summary>Grow and settle back (liked, repeat-one).</summary>
    public static void Pop(UIElement glyph, double peak = 1.3, int ms = 260) =>
        ScaleKeys(glyph, ms, new K(peak, 0.4), new K(1, 1));

    /// <summary>Shrink and come back (un-liked).</summary>
    public static void Shrink(UIElement glyph, int ms = 240) =>
        ScaleKeys(glyph, ms, new K(0.8, 0.4), new K(1, 1));

    /// <summary>One full turn, easing to a stop (repeat).</summary>
    public static void Spin(UIElement glyph, double degrees = 360, int ms = 450)
    {
        if (!Enabled)
        {
            return;
        }

        var animation = Make(0, degrees, ms);
        animation.FillBehavior = FillBehavior.Stop; // back to 0° (looks identical after a full turn)
        Parts(glyph).Rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    /// <summary>
    /// Shuffle: the arrows twist like a DNA helix (flipping over the horizontal axis twice)
    /// while travelling a few pixels forward, then glide back.
    /// </summary>
    public static void Helix(UIElement glyph, int ms = 520)
    {
        if (!Enabled)
        {
            return;
        }

        var parts = Parts(glyph);
        Run(parts.Scale, ScaleTransform.ScaleYProperty, ms, EaseInOut,
            new K(-1, 0.25), new K(1, 0.5), new K(-1, 0.75), new K(1, 1));
        Run(parts.Translate, TranslateTransform.XProperty, ms, EaseInOut,
            new K(6, 0.6), new K(0, 1));
    }

    /// <summary>
    /// Previous / next: the glyph slides out of the button on one side and comes back in from
    /// the other, as if the song were skipped. <paramref name="direction"/> is +1 (next) or −1.
    /// </summary>
    public static void SlideThrough(UIElement glyph, int direction, int ms = 320)
    {
        if (!Enabled)
        {
            return;
        }

        const double travel = 22;
        Run(Parts(glyph).Translate, TranslateTransform.XProperty, ms, EaseOut,
            new K(travel * direction, 0.45), new K(-travel * direction, 0.46, Jump: true), new K(0, 1));
        Run(glyph, UIElement.OpacityProperty, ms, EaseOut,
            new K(0, 0.45), new K(0, 0.46, Jump: true), new K(1, 1));
    }

    /// <summary>Add to playlist: the plus shrinks and drops out of the bottom, a new one lands from above.</summary>
    public static void Drop(UIElement glyph, int ms = 380)
    {
        if (!Enabled)
        {
            return;
        }

        var parts = Parts(glyph);
        Run(parts.Translate, TranslateTransform.YProperty, ms, EaseOut,
            new K(18, 0.45), new K(-18, 0.46, Jump: true), new K(0, 1));
        var scale = new[] { new K(0.5, 0.45), new K(0.5, 0.46, Jump: true), new K(1, 1) };
        Run(parts.Scale, ScaleTransform.ScaleXProperty, ms, EaseOut, scale);
        Run(parts.Scale, ScaleTransform.ScaleYProperty, ms, EaseOut, scale);
    }

    /// <summary>
    /// Play/pause: the old icon (shown on <paramref name="ghost"/>) spins down to nothing while
    /// the new one spins up from the opposite side.
    /// </summary>
    public static void Morph(UIElement glyph, TextBlock ghost, string oldText, int ms = 300)
    {
        if (!Enabled)
        {
            ghost.BeginAnimation(UIElement.OpacityProperty, null);
            ghost.Opacity = 0;
            return;
        }

        ghost.Text = oldText;
        var g = Parts(ghost);
        Run(ghost, UIElement.OpacityProperty, ms, EaseOut, new K(1, 0, Jump: true), new K(0, 0.6), new K(0, 1));
        Run(g.Scale, ScaleTransform.ScaleXProperty, ms, EaseOut, new K(1, 0, Jump: true), new K(0, 0.6), new K(0, 1));
        Run(g.Scale, ScaleTransform.ScaleYProperty, ms, EaseOut, new K(1, 0, Jump: true), new K(0, 0.6), new K(0, 1));
        Run(g.Rotate, RotateTransform.AngleProperty, ms, EaseOut, new K(0, 0, Jump: true), new K(-90, 0.6), new K(-90, 1));
        ghost.Opacity = 0; // base value once the animation stops

        var p = Parts(glyph);
        Run(p.Scale, ScaleTransform.ScaleXProperty, ms, EaseOut, new K(0, 0, Jump: true), new K(0, 0.25), new K(1, 1));
        Run(p.Scale, ScaleTransform.ScaleYProperty, ms, EaseOut, new K(0, 0, Jump: true), new K(0, 0.25), new K(1, 1));
        Run(p.Rotate, RotateTransform.AngleProperty, ms, EaseOut, new K(90, 0, Jump: true), new K(90, 0.25), new K(0, 1));
    }

    /// <summary>Heart burst: six small dots fly out from <paramref name="center"/> on the effect layer and fade.</summary>
    public static void Burst(Canvas layer, Point center, Brush brush, int ms = 380)
    {
        if (!Enabled)
        {
            return;
        }

        const int count = 6;
        const double size = 4, distance = 15;
        for (var i = 0; i < count; i++)
        {
            var angle = (i * 360.0 / count - 90) * Math.PI / 180;
            var dot = new Ellipse { Width = size, Height = size, Fill = brush, IsHitTestVisible = false };
            Canvas.SetLeft(dot, center.X - size / 2);
            Canvas.SetTop(dot, center.Y - size / 2);
            layer.Children.Add(dot);

            var shift = Parts(dot).Translate;
            Run(shift, TranslateTransform.XProperty, ms, EaseOut, new K(Math.Cos(angle) * distance, 1));
            Run(shift, TranslateTransform.YProperty, ms, EaseOut, new K(Math.Sin(angle) * distance, 1));
            var fade = Run(dot, UIElement.OpacityProperty, ms, EaseOut, new K(1, 0.3), new K(0, 1));
            fade.Completed += (_, _) => layer.Children.Remove(dot);
        }
    }

    /// <summary>
    /// Volume: two arcs ripple out from the speaker when it gets louder, or fold in when quieter.
    /// The arcs rest invisible.
    /// </summary>
    public static void Waves(UIElement wave1, UIElement wave2, bool outward, int ms = 420)
    {
        if (!Enabled)
        {
            return;
        }

        var (from, to) = outward ? (0.6, 1.4) : (1.4, 0.6);
        var delay = 0.0;
        foreach (var wave in new[] { wave1, wave2 })
        {
            var s = Parts(wave).Scale;
            var scale = new[] { new K(from, delay, Jump: true), new K(to, Math.Min(1, delay + 0.7)) };
            Run(s, ScaleTransform.ScaleXProperty, ms, EaseOut, scale);
            Run(s, ScaleTransform.ScaleYProperty, ms, EaseOut, scale);
            Run(wave, UIElement.OpacityProperty, ms, EaseOut,
                new K(0, delay, Jump: true), new K(0.9, delay + 0.15), new K(0, Math.Min(1, delay + 0.7)));
            delay += 0.3;
        }
    }

    /// <summary>
    /// Mood button: the mood's color spreads from the pressed point until it fills the button,
    /// then fades (the redrawn button is already colored if the mood switch worked).
    /// </summary>
    /// <param name="layer">A zero-size canvas under the button's text (children don't affect layout).</param>
    /// <param name="bounds">The button, to size the ripple so it covers it.</param>
    public static void Ripple(Canvas layer, FrameworkElement bounds, Point at, Brush brush, int ms = 300)
    {
        if (!Enabled)
        {
            return;
        }

        var reach = Math.Max(bounds.ActualWidth, bounds.ActualHeight) * 2.2;
        var circle = new Ellipse { Width = reach, Height = reach, Fill = brush, IsHitTestVisible = false };
        Canvas.SetLeft(circle, at.X - reach / 2);
        Canvas.SetTop(circle, at.Y - reach / 2);
        layer.Children.Add(circle);
        var host = layer;

        var s = Parts(circle).Scale;
        var total = ms + 700 + 250;
        var grow = new[] { new K(0, 0, Jump: true), new K(1, (double)ms / total) };
        Run(s, ScaleTransform.ScaleXProperty, total, EaseOut, grow);
        Run(s, ScaleTransform.ScaleYProperty, total, EaseOut, grow);
        var fade = Run(circle, UIElement.OpacityProperty, total, EaseOut,
            new K(1, (ms + 700.0) / total), new K(0, 1));
        fade.Completed += (_, _) => host.Children.Remove(circle);
    }

    // ---------- Plumbing ----------

    private static void ScaleKeys(UIElement element, int ms, params K[] keys)
    {
        if (!Enabled)
        {
            return;
        }

        var scale = Parts(element).Scale;
        Run(scale, ScaleTransform.ScaleXProperty, ms, EaseOut, keys);
        Run(scale, ScaleTransform.ScaleYProperty, ms, EaseOut, keys);
    }

    private static DoubleAnimationUsingKeyFrames Run(IAnimatable target, DependencyProperty property, int ms, IEasingFunction ease, params K[] keys)
    {
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            FillBehavior = FillBehavior.Stop, // every effect ends at the resting value
        };
        foreach (var key in keys)
        {
            var time = KeyTime.FromPercent(Math.Clamp(key.At, 0, 1));
            animation.KeyFrames.Add(key.Jump
                ? new DiscreteDoubleKeyFrame(key.Value, time)
                : new EasingDoubleKeyFrame(key.Value, time, ease));
        }

        Timeline.SetDesiredFrameRate(animation, FrameRate);
        target.BeginAnimation(property, animation);
        return animation;
    }

    private static DoubleAnimation Make(double from, double to, int ms)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EaseOut };
        Timeline.SetDesiredFrameRate(animation, FrameRate);
        return animation;
    }

    /// <summary>The element's Scale/Rotate/Translate group, created (centered) on first use.</summary>
    private static (ScaleTransform Scale, RotateTransform Rotate, TranslateTransform Translate) Parts(UIElement element)
    {
        if (element.RenderTransform is TransformGroup { IsFrozen: false } g
            && g.Children.Count == 3
            && g.Children[0] is ScaleTransform s
            && g.Children[1] is RotateTransform r
            && g.Children[2] is TranslateTransform t)
        {
            return (s, r, t);
        }

        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform(0);
        var translate = new TranslateTransform();
        if (element.RenderTransformOrigin == default)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5); // keep an origin set in XAML (e.g. the sound waves)
        }
        element.RenderTransform = new TransformGroup { Children = { scale, rotate, translate } };
        return (scale, rotate, translate);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
