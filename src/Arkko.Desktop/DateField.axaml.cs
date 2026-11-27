using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Arkko.Desktop;

/// <summary>Campo de fecha con calendario desplegable, con el aspecto de la app en tema claro y oscuro.</summary>
/// <remarks>
/// La fecha se enseña como «15-10-2026» y se elige con un clic; sin escribir, no hay fechas mal formadas. Es el
/// mismo campo que el de la ventana de WPF, para que las dos se usen igual.
/// </remarks>
public partial class DateField : UserControl
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static readonly StyledProperty<DateTime?> DateProperty =
        AvaloniaProperty.Register<DateField, DateTime?>(nameof(Date), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<DateField, string>(nameof(EmptyText), "Sin fecha");

    public static readonly StyledProperty<bool> AllowEmptyProperty =
        AvaloniaProperty.Register<DateField, bool>(nameof(AllowEmpty), true);

    private DateTime _month;

    /// <summary>Crea el campo, sin fecha.</summary>
    public DateField()
    {
        InitializeComponent();
        foreach (var day in new[] { "L", "M", "X", "J", "V", "S", "D" })
        {
            var label = new TextBlock { Text = day, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("FgFaint"));
            WeekDays.Children.Add(label);
        }
        ShowDate();
    }

    /// <summary>La fecha elegida; null sin fecha.</summary>
    public DateTime? Date
    {
        get => GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    /// <summary>Lo que se lee sin fecha, como «Sin fecha» o «Nunca».</summary>
    public string EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    /// <summary>Si se puede dejar sin fecha: entonces el calendario ofrece quitarla.</summary>
    public bool AllowEmpty
    {
        get => GetValue(AllowEmptyProperty);
        set => SetValue(AllowEmptyProperty, value);
    }

    /// <summary>Repinta el campo al cambiar la fecha o el texto de sin fecha.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DateProperty || change.Property == EmptyTextProperty) ShowDate();
    }

    /// <summary>Pinta en el campo la fecha elegida, o el texto de sin fecha más apagado.</summary>
    private void ShowDate()
    {
        if (Shown is null) return;
        Shown.Text = Date?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? EmptyText;
        Shown.Bind(TextBlock.ForegroundProperty, Shown.GetResourceObservable(Date is null ? "FgFaint" : "Fg"));
    }

    /// <summary>Abre el calendario en el mes de la fecha elegida, o en el de hoy.</summary>
    private void Field_Click(object? sender, RoutedEventArgs e)
    {
        var at = Date ?? DateTime.Today;
        _month = new DateTime(at.Year, at.Month, 1);
        ClearButton.Content = EmptyText;
        ClearButton.IsVisible = AllowEmpty;
        FillMonth();
        Pop.IsOpen = true;
    }

    /// <summary>Pinta el mes: seis semanas de lunes a domingo, con el día elegido recuadrado y hoy en el acento.</summary>
    private void FillMonth()
    {
        var title = _month.ToString("MMMM yyyy", Es);
        Month.Text = char.ToUpper(title[0], Es) + title[1..];
        Days.Children.Clear();
        var start = _month.AddDays(-(((int)_month.DayOfWeek + 6) % 7));
        var theme = (Avalonia.Styling.ControlTheme)Resources["DayButton"]!;
        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var button = new Button { Theme = theme, Content = day.Day.ToString(Es), Tag = day };
            var color = day == DateTime.Today ? "Accent" : day.Month == _month.Month ? "Fg" : "FgFaint";
            button.Bind(ForegroundProperty, button.GetResourceObservable(color));
            if (day == DateTime.Today) button.FontWeight = FontWeight.SemiBold;
            if (day == Date?.Date)
            {
                button.Bind(BorderBrushProperty, button.GetResourceObservable("ChipOnEdge"));
                button.FontWeight = FontWeight.SemiBold;
            }
            button.Click += (_, _) => Choose(day);
            Days.Children.Add(button);
        }
    }

    /// <summary>Deja elegida una fecha, o ninguna, y cierra el calendario.</summary>
    private void Choose(DateTime? day)
    {
        Date = day;
        Pop.IsOpen = false;
        Field.Focus();
    }

    /// <summary>Pasa al mes anterior.</summary>
    private void Previous_Click(object? sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        FillMonth();
    }

    /// <summary>Pasa al mes siguiente.</summary>
    private void Next_Click(object? sender, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        FillMonth();
    }

    /// <summary>Elige hoy.</summary>
    private void Today_Click(object? sender, RoutedEventArgs e) => Choose(DateTime.Today);

    /// <summary>Quita la fecha.</summary>
    private void Clear_Click(object? sender, RoutedEventArgs e) => Choose(null);
}
