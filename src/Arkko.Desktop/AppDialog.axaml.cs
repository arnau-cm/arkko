using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Arkko.Desktop;

/// <summary>Tono de un diálogo: decide el icono y su color.</summary>
public enum DialogTone
{
    Question,
    Warning,
    Error,
}

/// <summary>Una fila de la tabla de un diálogo: pastilla de estado, título, valor en monoespaciada y detalle debajo.</summary>
public sealed record DialogRow(string Badge, IBrush BadgeBrush, IBrush BadgeFill, string Title, string Value, string Detail);

/// <summary>Diálogo de la app, con su tema, en lugar del cuadro de mensaje del sistema.</summary>
/// <remarks>
/// Tiene título, texto, una lista opcional (lo que se va a borrar o añadir), una tabla de filas, y una nota
/// al pie para el detalle técnico, como la ruta de un fichero. Se arrastra desde cualquier punto de la tarjeta.
/// En Avalonia los diálogos son asíncronos: cada función devuelve una tarea que acaba al cerrarlo.
/// </remarks>
public partial class AppDialog : Window
{
    private static readonly IBrush QuestionBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x7A, 0xB8, 0xFF));
    private static readonly IBrush WarningBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x57));

    private bool _confirmed;

    /// <summary>Constructor vacío para el cargador de XAML; los diálogos se abren con las funciones estáticas.</summary>
    public AppDialog() => InitializeComponent();

    private AppDialog(DialogTone tone, string heading, string body, IReadOnlyList<string>? items, string? note,
        string confirm, string? cancel, IReadOnlyList<DialogRow>? rows = null) : this()
    {
        var brush = tone switch { DialogTone.Warning => WarningBrush, DialogTone.Error => ThemeColors.Of("Danger", "#E03E4C"), _ => QuestionBrush };
        Glyph.Text = tone switch { DialogTone.Question => "", DialogTone.Warning => "", _ => "" };
        Glyph.Foreground = brush;
        IconBack.Background = new ImmutableSolidColorBrush(((ISolidColorBrush)brush).Color, 0.16);

        Heading.Text = heading;
        Body.Text = body;
        Body.IsVisible = body.Length > 0;
        Items.ItemsSource = items;
        ItemsBox.IsVisible = items is { Count: > 0 };
        Rows.ItemsSource = rows;
        RowsBox.IsVisible = rows is { Count: > 0 };
        Note.Text = note ?? "";
        Note.IsVisible = !string.IsNullOrEmpty(note);
        ConfirmButton.Content = confirm;
        CancelButton.Content = cancel;
        CancelButton.IsVisible = cancel is not null;
        // Sin botón de cancelar, Esc cierra igual: el principal también hace de cancelar.
        if (cancel is null) ConfirmButton.IsCancel = true;
        Opened += (_, _) => (cancel is null ? ConfirmButton : CancelButton).Focus();
    }

    /// <summary>Pide confirmación; true si la persona pulsa el botón principal.</summary>
    /// <param name="items">Lista de lo que va a pasar, en un recuadro; null si no hace falta.</param>
    /// <param name="note">Detalle técnico al pie, en pequeño: rutas, copias de seguridad.</param>
    public static Task<bool> ConfirmAsync(Window? owner, string heading, string body, string confirm, DialogTone tone = DialogTone.Question,
        IReadOnlyList<string>? items = null, string? note = null, string cancel = "Cancelar") =>
        new AppDialog(tone, heading, body, items, note, confirm, cancel).OpenAsync(owner);

    /// <summary>Pide confirmación enseñando una tabla de filas; true si la persona pulsa el botón principal.</summary>
    public static Task<bool> ConfirmRowsAsync(Window? owner, string heading, string body, string confirm, IReadOnlyList<DialogRow> rows,
        DialogTone tone = DialogTone.Warning, string? note = null, string cancel = "Cancelar") =>
        new AppDialog(tone, heading, body, null, note, confirm, cancel, rows).OpenAsync(owner);

    /// <summary>Avisa de algo, como un fallo, con un solo botón.</summary>
    public static Task ShowAsync(Window? owner, string heading, string body, DialogTone tone = DialogTone.Error, string? note = null) =>
        new AppDialog(tone, heading, body, null, note, "Entendido", null).OpenAsync(owner);

    /// <summary>Abre el diálogo sobre su ventana, o centrado en la pantalla si no la hay, y espera a que se cierre.</summary>
    private async Task<bool> OpenAsync(Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            await ShowDialog(owner);
            return _confirmed;
        }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var closed = new TaskCompletionSource();
        Closed += (_, _) => closed.TrySetResult();
        Show();
        await closed.Task;
        return _confirmed;
    }

    /// <summary>Acepta y cierra.</summary>
    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    /// <summary>Cancela y cierra.</summary>
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Mueve el diálogo arrastrando la tarjeta.</summary>
    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
}
