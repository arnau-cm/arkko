using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Una opción del buscador: el punto de su estado, un dato corto a la izquierda, su texto y un dato a la derecha.</summary>
/// <param name="Search">Lo que se compara con lo escrito, además de <paramref name="Left"/> y <paramref name="Text"/>.</param>
public sealed record PickerItem(string Id, string Left, string Text, string? Right, IBrush Dot, string Tip, string? Search = null);

/// <summary>Buscador en un desplegable: se escribe parte del texto y se elige con un clic o con Intro.</summary>
/// <remarks>Lo usan Sesiones para añadir una tarea a la sesión y el Tablero para añadir la tarea a una sesión.</remarks>
public static class Picker
{
    private const int MaxRows = 12;
    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("es-ES").CompareInfo;

    /// <summary>Abre el buscador junto a un elemento, o donde está el ratón, con las opciones en el orden dado.</summary>
    /// <param name="empty">Lo que se dice si no hay ninguna opción.</param>
    public static void Show(Control anchor, bool atPointer, string title, string hint, string empty, IReadOnlyList<PickerItem> items,
        Action<string> picked)
    {
        var flyout = new Flyout { Placement = atPointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft };
        flyout.FlyoutPresenterClasses.Add("picker");
        var search = new TextBox { FontSize = 12, Padding = new Thickness(8, 5, 8, 5) };
        var list = new StackPanel();

        void Pick(string id)
        {
            flyout.Hide();
            picked(id);
        }

        List<PickerItem> Matches()
        {
            var q = (search.Text ?? "").Trim();
            return items.Where(i => q.Length == 0 || Has(i.Left, q) || Has(i.Text, q) || Has(i.Search, q)).Take(MaxRows).ToList();
        }

        void Fill()
        {
            list.Children.Clear();
            var found = Matches();
            if (found.Count == 0) list.Children.Add(Note(items.Count == 0 ? empty : "Nada con ese texto."));
            foreach (var item in found) list.Children.Add(Row(item, Pick));
        }

        search.TextChanged += (_, _) => Fill();
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Matches().FirstOrDefault() is { } first) Pick(first.Id);
            else if (e.Key == Key.Escape) flyout.Hide();
        };
        flyout.Opened += (_, _) => search.Focus();

        var header = new TextBlock { Text = title, Margin = new Thickness(2, 0, 0, 8), Classes = { "menuLabel" } };
        var tip = Note(hint);
        tip.Margin = new Thickness(2, 6, 0, 6);
        var box = new Border
        {
            CornerRadius = Radius("RadiusM", 12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Width = 460,
            BoxShadow = BoxShadows.Parse("0 3 16 0 #38000000"),
            Child = new StackPanel
            {
                Children = { header, search, tip, new ScrollViewer { MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list } },
            },
        };
        box.Bind(Border.BackgroundProperty, box.GetResourceObservable("TipFill"));
        box.Bind(Border.BorderBrushProperty, box.GetResourceObservable("TipEdge"));
        flyout.Content = box;
        Fill();
        flyout.ShowAt(anchor, atPointer);
    }

    /// <summary>Una opción de la lista: punto, dato corto, texto y dato a la derecha; se elige con un clic.</summary>
    private static Border Row(PickerItem item, Action<string> pick)
    {
        var dot = new Ellipse { Width = 7, Height = 7, Fill = item.Dot, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var left = new TextBlock
        {
            Text = item.Left, FontSize = 11, Width = 96, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        left.Bind(TextBlock.FontFamilyProperty, left.GetResourceObservable("MonoFont"));
        left.Bind(TextBlock.ForegroundProperty, left.GetResourceObservable("FgMuted"));
        var right = new TextBlock { Text = item.Right ?? "", FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Bind(TextBlock.ForegroundProperty, right.GetResourceObservable("FgFaint"));
        var text = new TextBlock { Text = item.Text, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Fg"));
        var panel = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        panel.Children.Add(dot);
        panel.Children.Add(left);
        panel.Children.Add(right);
        panel.Children.Add(text);
        var row = new Border
        {
            CornerRadius = Radius("RadiusS", 8), Padding = new Thickness(8, 6, 8, 6), Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand), Child = panel,
        };
        ToolTip.SetTip(row, item.Tip);
        row.PointerEntered += (_, _) => row.Background = row.FindResource("TrackFill") as IBrush;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerReleased += (_, _) => pick(item.Id);
        return row;
    }

    private static TextBlock Note(string text)
    {
        var note = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 8, 6) };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("FgFaint"));
        return note;
    }

    /// <summary>Radio de la escala del tema activo; el de respaldo si no está.</summary>
    private static CornerRadius Radius(string key, double fallback) =>
        Application.Current?.TryGetResource(key, App.VariantOf(App.Design), out var value) == true && value is CornerRadius radius ? radius : new CornerRadius(fallback);

    private static bool Has(string? text, string query) =>
        text is not null && Compare.IndexOf(text, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}

/// <summary>El buscador con las tareas leídas de los tableros, para añadir una a una sesión.</summary>
public static class TaskPicker
{
    /// <summary>Abre el buscador de tareas, sin las de <paramref name="exclude"/>: lo que está en marcha, primero.</summary>
    public static void Show(Control anchor, bool atPointer, IEnumerable<string> exclude, Action<string> picked)
    {
        var skip = exclude.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = TaskDirectory.Instance.AllTasks()
            .Where(t => !skip.Contains(t.Key))
            .OrderBy(t => Rank(t.State))
            .ThenByDescending(t => t.UpdatedUtc ?? DateTime.MinValue)
            .Select(t => new PickerItem(t.Key, t.Key, t.Title, TaskStates.Name(t.State), TaskStates.ColorOf(t.State),
                $"{t.Key} · {t.Title} · {TaskStates.Name(t.State)}"))
            .ToList();
        Picker.Show(anchor, atPointer, "AÑADIR UNA TAREA A LA SESIÓN", "Escribe una clave o parte del título; Intro elige la primera.",
            "Aún no se ha leído ningún tablero.", items, picked);
    }

    // Lo que está en marcha, primero: en curso, en revisión, por hacer, bloqueada, backlog y, al final, lo hecho.
    private static int Rank(TaskState state) => state switch
    {
        TaskState.Doing => 0,
        TaskState.Review => 1,
        TaskState.Todo => 2,
        TaskState.Blocked => 3,
        TaskState.Backlog or TaskState.Unknown => 4,
        _ => 5,
    };
}

/// <summary>Nombre y color de cada columna de la app, los mismos en el Tablero y en los buscadores.</summary>
public static class TaskStates
{
    /// <summary>Nombre de una columna de la app.</summary>
    public static string Name(TaskState state) => TaskBoard.ColumnName(state);

    /// <summary>Color de una columna de la app.</summary>
    public static IBrush ColorOf(TaskState state) => state switch
    {
        TaskState.Todo => ThemeColors.Of("DataSonnet", "#61AFEF"),
        TaskState.Doing => ThemeColors.Of("DataFiveHour", "#E5C07B"),
        TaskState.Blocked => ThemeColors.Of("Danger", "#E03E4C"),
        TaskState.Review => ThemeColors.Of("DataFable", "#C678DD"),
        TaskState.Done => ThemeColors.Of("DataHaiku", "#98C379"),
        TaskState.ToDiscard => ThemeColors.Of("DataOpus", "#F09A76"),
        TaskState.Discarded => ThemeColors.Of("DataOther", "#ABB2BF"),
        _ => ThemeColors.Of("FgFaint", "#8A8D9C"),
    };
}
