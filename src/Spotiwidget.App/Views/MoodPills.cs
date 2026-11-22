using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Spotiwidget.App.ViewModels;
using Spotiwidget.Core.Settings;

namespace Spotiwidget.App.Views;

/// <summary>Builds the row of mood buttons shared by the desktop dock and the card.</summary>
public static class MoodPills
{
    public static void Build(Panel panel, IReadOnlyList<MoodDefinition> moods, string? activeId, Action<string> onClick, double fontSize = 12)
    {
        panel.Children.Clear();
        if (moods.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Ruh hali yok · Ayarlar'dan ekleyin",
                Foreground = (Brush)Application.Current.Resources["SubtleTextBrush"],
                FontSize = fontSize,
                Margin = new Thickness(2, 0, 0, 6),
            });
            return;
        }

        foreach (var mood in moods)
        {
            var isActive = mood.Id == activeId;
            var color = MoodPalette.Brush(mood.Color);
            Brush idle = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
            if (isActive)
            {
                // The newly active mood's color fades in instead of snapping.
                var fill = color.Clone();
                Anim.BrushIn(fill);
                idle = fill;
            }
            var hot = isActive ? color : MoodPalette.Brush(mood.Color, 0x66);

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = isActive ? Brushes.White : color,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            content.Children.Add(new TextBlock
            {
                Text = mood.Name,
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            });

            var pill = new Border
            {
                Background = idle,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Child = content,
                Cursor = Cursors.Hand,
                ToolTip = isActive ? "Aktif ruh hali" : $"{mood.Name} ruh haline geç",
            };
            pill.MouseEnter += (_, _) => pill.Background = hot;
            pill.MouseLeave += (_, _) => pill.Background = idle;
            pill.MouseLeftButtonDown += (_, e) => e.Handled = true; // don't start a window drag
            var id = mood.Id;
            pill.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                onClick(id);
            };
            panel.Children.Add(pill);
        }
    }
}
