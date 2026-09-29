using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using TutorSpace.Services;

namespace TutorSpace.UI;

/// <summary>Small helpers for messages and for building UI from code.</summary>
public static class Ui
{
    public static string? Friendly(Exception ex) => Errors.Friendly(ex);

    public static void ShowError(Exception ex)
    {
        if (Friendly(ex) is { } friendly) ex = new UserError(friendly);
        var inner = ex;
        while (inner is not UserError && inner.InnerException != null) inner = inner.InnerException;

        if (inner is UserError)
            MessageBox.Show(inner.Message, "TutorSpace", MessageBoxButton.OK, MessageBoxImage.Warning);
        else if (ex is DbUpdateException)
            MessageBox.Show("Ошибка сохранения в базу:\n" + (ex.InnerException?.Message ?? ex.Message), "TutorSpace",
                MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show("Непредвиденная ошибка:\n" + ex.Message, "TutorSpace", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Runs an action and shows its error instead of crashing. Returns true on success.</summary>
    public static bool Try(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
    }

    public static bool Confirm(string message, string title = "Подтверждение") =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Info(string message) =>
        MessageBox.Show(message, "TutorSpace", MessageBoxButton.OK, MessageBoxImage.Information);

    public static Window? OwnerOf(DependencyObject? element) =>
        element == null ? Application.Current.MainWindow : Window.GetWindow(element) ?? Application.Current.MainWindow;

    /// <summary>
    /// Centers a dialog over the window that opened it. Only a visible window other than
    /// the dialog itself can own it — at startup or after logout there may be none.
    /// </summary>
    public static void SetOwner(Window dialog, DependencyObject? element)
    {
        var owner = OwnerOf(element);
        if (owner != null && owner != dialog && owner.IsVisible)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    // ---------- Builders ----------

    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static TextBlock Text(string text, string? style = null, double size = 0, bool bold = false, Brush? color = null, Thickness? margin = null)
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (style != null) tb.Style = (Style)Application.Current.FindResource(style);
        if (size > 0) tb.FontSize = size;
        if (bold) tb.FontWeight = FontWeights.SemiBold;
        if (color != null) tb.Foreground = color;
        if (margin != null) tb.Margin = margin.Value;
        return tb;
    }

    /// <summary>A glyph from the Windows icon font (see <see cref="Icons"/>).</summary>
    public static TextBlock Icon(string glyph, double size = 16, Brush? color = null, Thickness? margin = null)
    {
        var tb = new TextBlock { Text = glyph, Style = (Style)Application.Current.FindResource("Icon"), FontSize = size };
        if (color != null) tb.Foreground = color;
        if (margin != null) tb.Margin = margin.Value;
        return tb;
    }

    /// <summary>A button; with <paramref name="icon"/> the glyph goes before the text.</summary>
    public static Button Button(string content, Action onClick, string? style = null, string? tooltip = null, string? icon = null)
    {
        var button = new Button { Margin = new Thickness(0, 4, 8, 0), Content = ButtonContent(content, icon) };
        // A button whose content is an icon plus text has no name of its own for screen readers.
        System.Windows.Automation.AutomationProperties.SetName(button, content.Length > 0 ? content : tooltip ?? "");
        if (style != null) button.Style = (Style)Application.Current.FindResource(style);
        if (tooltip != null) button.ToolTip = tooltip;
        button.Click += (_, _) => Try(onClick);
        return button;
    }

    public static object ButtonContent(string text, string? icon)
    {
        if (icon == null) return text;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Icon(icon, 14, margin: new Thickness(0, 0, text.Length > 0 ? 8 : 0, 0)));
        if (text.Length > 0) row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>A square button with only a glyph; the tooltip says what it does.</summary>
    public static Button IconButton(string glyph, Action onClick, string tooltip, bool danger = false)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("IconButton"),
            Content = Icon(glyph, 14),
            ToolTip = tooltip,
            Margin = new Thickness(0, 4, 8, 0),
        };
        if (danger) button.Foreground = Res("Danger");
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => Try(onClick);
        return button;
    }

    public static Border Card(UIElement child, Brush? background = null, Brush? border = null)
    {
        var card = new Border { Child = child, Style = (Style)Application.Current.FindResource("Card") };
        if (background != null) card.Background = background;
        if (border != null) card.BorderBrush = border;
        return card;
    }

    /// <summary>Card title on the left, optional extra (a counter, a button) on the right.</summary>
    public static DockPanel Header(string title, UIElement? right = null, string style = "H2")
    {
        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };
        if (right != null)
        {
            DockPanel.SetDock(right, Dock.Right);
            if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            dock.Children.Add(right);
        }
        var text = Text(title, style);
        text.Margin = new Thickness(0);
        text.VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(text);
        return dock;
    }

    public static TextBox Box(string text = "", bool multiline = false, double minHeight = 0)
    {
        var box = new TextBox { Text = text };
        if (multiline)
        {
            box.Style = (Style)Application.Current.FindResource("MultilineBox");
            if (minHeight > 0) box.MinHeight = minHeight;
        }
        return box;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static WrapPanel Wrap(params UIElement[] children)
    {
        var panel = new WrapPanel();
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static Border Chip(string text, Brush background, Brush foreground) => new()
    {
        Background = background,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 3, 8, 3),
        Margin = new Thickness(0, 0, 6, 4),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock { Text = text, Foreground = foreground, FontSize = 12, FontWeight = FontWeights.SemiBold },
    };

    /// <summary>A rounded square with a glyph, used to mark the kind of an item.</summary>
    public static Border IconBadge(string glyph, Brush background, Brush foreground, double size = 36) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 4),
        Background = background,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = glyph, Style = (Style)Application.Current.FindResource("Icon"), FontSize = Math.Round(size * 0.45),
            Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center,
        },
    };

    /// <summary>A round avatar with the person's initials.</summary>
    public static Border Avatar(string name, double size = 36) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 2),
        Background = Res("PrimarySoft"),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = Initials(name), FontSize = Math.Round(size * 0.38), FontWeight = FontWeights.SemiBold, Foreground = Res("Primary"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>A circular progress ring, <paramref name="fraction"/> from 0 to 1.</summary>
    public static Grid Ring(double fraction, double size, double thickness)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Res("PrimarySoft"), StrokeThickness = thickness });
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction >= 0.999)
        {
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Res("Success"), StrokeThickness = thickness });
            return grid;
        }
        if (fraction <= 0) return grid;

        var radius = (size - thickness) / 2;
        var center = size / 2;
        Point At(double angle) => new(center + radius * Math.Cos(angle), center + radius * Math.Sin(angle));
        var start = -Math.PI / 2;
        var end = start + 2 * Math.PI * fraction;
        var figure = new PathFigure { StartPoint = At(start), IsClosed = false };
        figure.Segments.Add(new ArcSegment(At(end), new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        grid.Children.Add(new System.Windows.Shapes.Path
        {
            Data = new PathGeometry(new[] { figure }), Stroke = Res("Success"), StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        return grid;
    }

    public static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpper(),
            _ => (parts[0][..1] + parts[1][..1]).ToUpper(),
        };
    }

    /// <summary>A labelled number tile for dashboards.</summary>
    public static Border Tile(string title, string value, string? hint = null, string? icon = null)
    {
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        if (icon != null)
        {
            var badge = IconBadge(icon, Res("PrimarySoft"), Res("Primary"), 30);
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
        }
        var label = Text(title, "Muted", size: 13);
        label.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(label);
        panel.Children.Add(head);
        panel.Children.Add(Text(value, size: 26, bold: true));
        if (hint != null) panel.Children.Add(Text(hint, "Muted", size: 12, margin: new Thickness(0, 2, 0, 0)));
        var card = Card(panel);
        card.Width = 230;
        card.Margin = new Thickness(0, 0, 16, 16);
        return card;
    }

    public static string Plural(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        var word = mod10 == 1 && mod100 != 11 ? one
            : mod10 is >= 2 and <= 4 && (mod100 < 10 || mod100 >= 20) ? few : many;
        return $"{n} {word}";
    }
}
