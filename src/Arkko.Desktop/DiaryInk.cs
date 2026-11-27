using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Los colores, las marcas y las fichas del Diario, comunes a todas sus vistas.</summary>
/// <remarks>
/// Cada repositorio lleva su color de <see cref="RepoColors"/>, el mismo en Sesiones, el Tablero y Estadísticas.
/// </remarks>
internal static class DiaryInk
{
    /// <summary>El color de un repositorio: el elegido, el de su subtablero o uno fijo por su nombre.</summary>
    public static IBrush Repo(string project, double opacity = 1) => RepoColors.Of(project, opacity);

    /// <summary>Un pincel del tema, con su color de reserva.</summary>
    public static IBrush Of(string key, string fallback, double opacity = 1) =>
        opacity < 1 ? ThemeColors.Of(key, fallback, opacity) : ThemeColors.Of(key, fallback);

    public static IBrush Fg => Of("Fg", "#F2F3F7");
    public static IBrush Muted => Of("FgMuted", "#B0B4C0");
    public static IBrush Faint => Of("FgFaint", "#8A8D9C");
    public static IBrush Wait => Of("DataFiveHour", "#E5C07B");

    /// <summary>La marca de un commit, como en un grafo de git: un anillo del color de su repositorio.</summary>
    public static Control CommitMark(IBrush color, double size = 9) => new Ellipse
    {
        Width = size, Height = size, Stroke = color, StrokeThickness = 2, Fill = Of("TaskCardFill", "#2A2D36"),
    };

    /// <summary>La marca de un cambio de columna: dos flechas en una pastilla.</summary>
    public static Control MoveMark() => new Border
    {
        CornerRadius = new CornerRadius(4), Padding = new Thickness(3, 0, 3, 1), Background = Of("TipFill", "#202228"),
        BorderBrush = Of("Accent", "#61AFEF", 0.6), BorderThickness = new Thickness(1),
        Child = new TextBlock { Text = "⇄", FontSize = 10, Foreground = Of("Accent", "#61AFEF") },
    };

    /// <summary>Un texto con su tamaño, su color y, si se pide, en seminegrita.</summary>
    public static TextBlock Text(string text, double size = 12, IBrush? color = null, bool bold = false, bool wrap = false) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Fg, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    /// <summary>Una cifra grande con su rótulo debajo, en mayúsculas pequeñas.</summary>
    public static StackPanel Figure(string value, string caption, IBrush? color = null)
    {
        var figure = new StackPanel();
        figure.Children.Add(Text(value, 15, color, bold: true));
        figure.Children.Add(Text(caption.ToUpperInvariant(), 10, Faint));
        return figure;
    }

    /// <summary>Una ficha para un tooltip: cabecera con su color, título y subtítulo; cifras; filas de dato; y una nota al pie.</summary>
    public static Control Card(string title, string? subtitle = null, IBrush? color = null,
        IEnumerable<(string Value, string Caption)>? figures = null, IEnumerable<(string Label, string Value)>? rows = null,
        IEnumerable<Control>? extra = null, string? footer = null)
    {
        var card = new StackPanel { MinWidth = 220, MaxWidth = 340 };

        var head = new DockPanel();
        if (color is not null)
        {
            var stripe = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = color, Margin = new Thickness(0, 1, 9, 1) };
            DockPanel.SetDock(stripe, Dock.Left);
            head.Children.Add(stripe);
        }
        var titles = new StackPanel();
        titles.Children.Add(Text(title, 13, bold: true, wrap: true));
        if (!string.IsNullOrEmpty(subtitle)) titles.Children.Add(Text(subtitle, 11, Muted, wrap: true));
        head.Children.Add(titles);
        card.Children.Add(head);

        if (figures?.ToList() is { Count: > 0 } list)
        {
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 0), ColumnDefinitions = new ColumnDefinitions(string.Join(",", list.Select(_ => "*"))) };
            for (var i = 0; i < list.Count; i++)
            {
                var figure = Figure(list[i].Value, list[i].Caption);
                figure.Margin = new Thickness(0, 0, 12, 0);
                Grid.SetColumn(figure, i);
                grid.Children.Add(figure);
            }
            card.Children.Add(grid);
        }

        if (rows?.ToList() is { Count: > 0 } lines)
        {
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            for (var i = 0; i < lines.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = Text(lines[i].Label, 11, Faint);
                label.Margin = new Thickness(0, 0, 12, 4);
                var value = Text(lines[i].Value, 11, wrap: true);
                Grid.SetRow(label, i);
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);
                grid.Children.Add(label);
                grid.Children.Add(value);
            }
            card.Children.Add(grid);
        }

        foreach (var control in extra ?? []) card.Children.Add(control);

        if (footer is not null)
            card.Children.Add(new Border
            {
                BorderBrush = Of("TipEdge", "#30FFFFFF"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 0),
                Margin = new Thickness(0, 10, 0, 0), Child = Text(footer, 11, Faint, wrap: true),
            });
        return card;
    }

    /// <summary>Pone a un control una ficha como tooltip.</summary>
    public static void Tip(Control control, Control card) => ToolTip.SetTip(control, card);

    /// <summary>Una fila con un punto de color y un texto, para listas de tareas en fichas y en el mes.</summary>
    public static DockPanel Dotted(IBrush color, string text, double size = 11, IBrush? foreground = null)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        var dot = new Ellipse { Width = 7, Height = 7, Fill = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(Text(text, size, foreground));
        return row;
    }
}
