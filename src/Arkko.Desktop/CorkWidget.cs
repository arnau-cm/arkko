using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Arkko.Desktop;

/// <summary>El tamaño de un widget según el ancho de su recuadro: estrecho, medio o ancho.</summary>
internal enum WidgetSize
{
    S,
    M,
    L,
}

/// <summary>La versión de una vista de la app pensada para un recuadro del corcho: enseña lo esencial y gana detalle al
/// ensancharse, en tres tamaños (S, M y L) según el ancho.</summary>
/// <remarks>
/// Cada widget lee de los mismos datos que su vista del panel, pero guarda su estado aparte (lo que se elige en él), que
/// viaja con el corcho: así dos widgets de la misma vista pueden enseñar cosas distintas y el panel no se entera. Se pinta
/// entero con cada novedad, que son pocas y pequeñas; el desplazamiento de cada lista se conserva entre una vez y otra.
/// </remarks>
internal abstract class CorkWidget : UserControl
{
    /// <summary>Ancho a partir del cual el widget pasa a M.</summary>
    public const double MediumFrom = 480;

    /// <summary>Ancho a partir del cual el widget pasa a L.</summary>
    public const double LargeFrom = 900;

    /// <summary>Hueco a la derecha de una lista con desplazamiento, donde va su barra.</summary>
    protected const double ScrollGap = 10;

    // El texto de lo que flota, oscuro sobre su color fuerte en los dos temas.
    private static readonly IBrush FloatInk = new SolidColorBrush(Color.Parse("#1B1E25"));

    // Lo que dura el destello de lo que entra o cambia.
    private static readonly TimeSpan GlowTime = TimeSpan.FromSeconds(2.4);

    private readonly Dictionary<string, string> _state = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector> _scrolls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScrollViewer> _scrollers = new(StringComparer.Ordinal);
    private object? _layout;
    private bool _queued;

    // Los avisos de cambio: cómo estaba cada cosa de cada lista la última vez que se vio, cómo está en el dibujo de ahora,
    // hasta cuándo dura el destello de las que acaban de cambiar y los grupos plegados que esconden algo nuevo.
    private readonly Dictionary<string, Dictionary<string, string>> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _glowUntil = new(StringComparer.Ordinal);
    private readonly HashSet<string> _groupNews = new(StringComparer.Ordinal);

    // Las cosas apuntadas en el dibujo de ahora, por lista y clave, y las que acaban de entrar o cambiar.
    private Dictionary<string, Control> _items = new(StringComparer.Ordinal);
    private readonly List<Control> _novel = [];

    // La primera cosa que se veía arriba de cada lista desplazada antes de repintar, y a qué distancia del borde, para dejarla
    // en su sitio aunque entre algo encima.
    private readonly Dictionary<string, (string Item, double Delta)> _anchors = new(StringComparer.Ordinal);

    // Lo que flota encima del widget: «↑ N nuevos» y el aviso que lleva a algo que queda fuera de la vista.
    private readonly StackPanel _floatTop = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel _floatBottom = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
    private int _above;
    private (Control Target, string Text, IBrush Color)? _beacon;
    private DateTime? _refreshed;

    /// <summary>Crea el widget con el margen común a todos, alineado con el texto de la cabecera del recuadro.</summary>
    protected CorkWidget()
    {
        Padding = new Thickness(12, 10, 12, 10);
        Background = Brushes.Transparent;
        SizeChanged += (_, e) =>
        {
            Size = SizeOf(e.NewSize.Width);
            if (!Equals(LayoutKey(e.NewSize.Width), _layout)) Render();
        };
        AttachedToVisualTree += (_, _) =>
        {
            App.ThemeChanged += Render;
            Attached();
            Render();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            App.ThemeChanged -= Render;
            Detached();
        };
    }

    /// <summary>Avisa de que ha cambiado lo que el widget guarda de sí mismo, para guardarlo con el corcho.</summary>
    public event Action? StateChanged;

    /// <summary>El tamaño del widget ahora.</summary>
    public WidgetSize Size { get; private set; } = WidgetSize.S;

    /// <summary>La sección del panel que corresponde al widget, para «Abrir en el panel».</summary>
    public abstract PanelSection Section { get; }

    /// <summary>Si el widget tiene su sección en el panel; si no, no ofrece «Abrir en el panel».</summary>
    public virtual bool InPanel => true;

    /// <summary>Lo que el widget guarda de sí mismo: lo elegido en él.</summary>
    public IReadOnlyDictionary<string, string> State => _state;

    /// <summary>Si el widget se ve: mientras no, no relee.</summary>
    protected bool Active { get; private set; }

    /// <summary>Si ha cambiado algo mientras no se veía: su pestaña lleva el punto de novedad hasta que se mira.</summary>
    public bool HasNews { get; private set; }

    /// <summary>El color de lo más urgente del widget, para el punto de su cabecera; null si no pide nada.</summary>
    public virtual IBrush? Mood => null;

    /// <summary>Un dato breve para la cabecera, al lado del nombre: «3 te esperan», «DEVLAB»; null si no lleva.</summary>
    public virtual string? Headline => null;

    /// <summary>Cuándo leyó sus datos por última vez, para la cabecera; null si aún no ha leído nada.</summary>
    public virtual DateTime? RefreshedUtc => _refreshed;

    /// <summary>El tamaño que toca a un ancho.</summary>
    public static WidgetSize SizeOf(double width) => width >= LargeFrom ? WidgetSize.L : width >= MediumFrom ? WidgetSize.M : WidgetSize.S;

    /// <summary>Recupera lo guardado del widget al ponerlo en su sitio.</summary>
    public void Restore(IReadOnlyDictionary<string, string>? state)
    {
        _state.Clear();
        if (state is not null)
            foreach (var (key, value) in state) _state[key] = value;
        Restored();
        Render();
    }

    /// <summary>Empieza a leer y a ponerse al día, al verse.</summary>
    public void Activate()
    {
        Active = true;
        HasNews = false;
        Started();
        Render();
    }

    /// <summary>Deja de releer, al dejar de verse.</summary>
    public void Deactivate()
    {
        Active = false;
        Stopped();
    }

    /// <summary>Las opciones del widget para el menú ··· de su recuadro; vacío si no tiene.</summary>
    public virtual IEnumerable<Control> MenuItems() => [];

    /// <summary>Vuelve a pintar el widget entero, una vez aunque se pida varias seguidas.</summary>
    public void Render()
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            Paint();
        }, DispatcherPriority.Background);
    }

    /// <summary>Pinta el widget ya, guardando antes el desplazamiento de sus listas y lo que se veía arriba de cada una.</summary>
    private void Paint()
    {
        Anchor();
        foreach (var (key, scroller) in _scrollers) _scrolls[key] = scroller.Offset;
        _scrollers.Clear();
        _seen.Clear();
        _items = new Dictionary<string, Control>(StringComparer.Ordinal);
        _novel.Clear();
        _beacon = null;
        _layout = LayoutKey(Bounds.Width);
        Control built;
        try
        {
            built = Build();
        }
        catch (Exception ex)
        {
            Arkko.Core.JsonStore.LogError("widget", ex);
            built = Ui.Text("No se ha podido pintar el widget. El detalle está en el registro de errores.", 12, Ui.Muted, wrap: true);
        }
        // Visto, lo de ahora pasa a ser lo conocido; sin verse, solo se apunta la primera vez, para avisar al mirarlo.
        foreach (var (list, seen) in _seen)
            if (Active || !_known.ContainsKey(list)) _known[list] = seen;
        (_floatTop.Parent as Panel)?.Children.Remove(_floatTop);
        (_floatBottom.Parent as Panel)?.Children.Remove(_floatBottom);
        Content = new Panel { Children = { built, _floatTop, _floatBottom } };
        LayoutUpdated -= Settle;
        LayoutUpdated += Settle;
    }

    /// <summary>Apunta, en cada lista desplazada, la primera cosa que asoma arriba y a qué distancia del borde está.</summary>
    private void Anchor()
    {
        _anchors.Clear();
        foreach (var (key, scroller) in _scrollers)
        {
            if (scroller.Offset.Y < 1 || scroller.Content is not Visual content) continue;
            (string Id, double Top)? first = null;
            foreach (var (id, item) in _items)
            {
                if (item.TranslatePoint(default, content) is not { } at || at.Y + item.Bounds.Height <= scroller.Offset.Y) continue;
                if (first is null || at.Y < first.Value.Top) first = (id, at.Y);
            }
            if (first is { } found) _anchors[key] = (found.Id, found.Top - scroller.Offset.Y);
        }
    }

    /// <summary>Tras colocar el dibujo nuevo, deja cada lista desplazada donde estaba lo que se veía arriba, cuenta lo nuevo que
    /// ha entrado encima y pone al día lo que flota.</summary>
    private void Settle(object? sender, EventArgs e)
    {
        LayoutUpdated -= Settle;
        foreach (var (key, (id, delta)) in _anchors)
        {
            if (!_scrollers.TryGetValue(key, out var scroller) || !_items.TryGetValue(id, out var item) || scroller.Content is not Visual content) continue;
            if (item.TranslatePoint(default, content) is not { } at) continue;
            var target = Math.Max(0, at.Y - delta);
            _above += _novel.Count(n => n.TranslatePoint(default, content) is { } p && p.Y < target);
            if (Math.Abs(target - scroller.Offset.Y) > 0.5) scroller.Offset = new Vector(scroller.Offset.X, target);
        }
        Float();
    }

    /// <summary>Pone lo que flota encima del widget: «↑ N nuevos» si ha entrado algo por encima de lo que se ve, y el aviso que
    /// lleva a lo que pide atención si queda fuera de la vista.</summary>
    private void Float()
    {
        _floatTop.Children.Clear();
        _floatBottom.Children.Clear();
        if (_above > 0 && _scrollers.Values.Any(s => s.Offset.Y >= 1))
            _floatTop.Children.Add(FloatPill($"↑ {_above} {(_above == 1 ? "nuevo" : "nuevos")}", Ui.Wait, () =>
            {
                foreach (var scroller in _scrollers.Values) scroller.Offset = default;
            }));
        else _above = 0;
        if (_beacon is not { } beacon || beacon.Target.FindAncestorOfType<ScrollViewer>() is not { } owner) return;
        if (beacon.Target.TranslatePoint(default, owner) is not { } at) return;
        var height = beacon.Target.Bounds.Height;
        if (at.Y >= 0 && at.Y + height <= owner.Bounds.Height) return;
        var below = at.Y + height > owner.Bounds.Height;
        var pill = FloatPill($"{(below ? "↓" : "↑")} {beacon.Text}", beacon.Color, () =>
            owner.Offset = new Vector(owner.Offset.X, Math.Max(0, owner.Offset.Y + at.Y - 4)));
        (below ? _floatBottom : _floatTop).Children.Add(pill);
    }

    /// <summary>Una pastilla que flota encima del widget, de un color fuerte con el texto oscuro, que hace lo dado al pulsarla.</summary>
    private static Control FloatPill(string text, IBrush color, Action click)
    {
        var label = Ui.Text(text, 11, FloatInk, bold: true);
        var pill = new Border
        {
            Background = color, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 4),
            Child = label, Cursor = new Cursor(StandardCursorType.Hand), BoxShadow = BoxShadows.Parse("0 2 8 0 #60000000"),
        };
        pill.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            click();
        };
        return pill;
    }

    /// <summary>Lo que el widget enseña ahora, para su tamaño y sus datos.</summary>
    protected abstract Control Build();

    /// <summary>Lo que hace que cambie la forma del widget al cambiar su ancho; de serie, su tamaño.</summary>
    protected virtual object LayoutKey(double width) => SizeOf(width);

    /// <summary>Al recuperar lo guardado, antes de pintar.</summary>
    protected virtual void Restored()
    {
    }

    /// <summary>Al ponerse en una ventana: empieza a escuchar los avisos de sus datos.</summary>
    protected virtual void Attached()
    {
    }

    /// <summary>Al quitarse de su ventana: deja de escuchar.</summary>
    protected virtual void Detached()
    {
    }

    /// <summary>Al verse: lee lo que necesite.</summary>
    protected virtual void Started()
    {
    }

    /// <summary>Al dejar de verse: para sus relojes.</summary>
    protected virtual void Stopped()
    {
    }

    /// <summary>Un valor guardado del widget, o null.</summary>
    protected string? Get(string key) => _state.GetValueOrDefault(key);

    /// <summary>Guarda un valor del widget (null lo quita) y avisa para guardarlo con el corcho.</summary>
    protected void Set(string key, string? value)
    {
        if (value is null ? !_state.Remove(key) : _state.TryGetValue(key, out var old) && old == value) return;
        if (value is not null) _state[key] = value;
        StateChanged?.Invoke();
    }

    /// <summary>Un conjunto guardado como lista separada por comas; el dado si no se ha guardado nunca.</summary>
    protected HashSet<string> GetSet(string key, IEnumerable<string> fallback) =>
        Get(key) is { } saved ? saved.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal) : fallback.ToHashSet(StringComparer.Ordinal);

    /// <summary>Guarda un conjunto como lista separada por comas.</summary>
    protected void SetSet(string key, IEnumerable<string> values) => Set(key, string.Join(',', values));

    /// <summary>Si un grupo del widget (una columna, un día, un repositorio) está plegado.</summary>
    protected bool Folded(string group) => GetSet("folded", []).Contains(group);

    /// <summary>Pliega un grupo desplegado o despliega uno plegado, lo guarda con el corcho y repinta.</summary>
    /// <remarks>Al desplegarlo, lo nuevo que escondía ya se ha visto: se quita su punto.</remarks>
    protected void ToggleFold(string group)
    {
        var folded = GetSet("folded", []);
        if (folded.Remove(group)) _groupNews.Remove(group);
        else folded.Add(group);
        SetSet("folded", folded.Order(StringComparer.Ordinal));
        Render();
    }

    /// <summary>La cabecera de un grupo que se pliega: su flecha y lo dado, y el punto de novedad si, plegado, esconde algo que
    /// acaba de cambiar; al pulsarla, pliega o despliega el grupo.</summary>
    protected Control FoldHead(string group, params Control[] content)
    {
        var open = !Folded(group);
        var row = Ui.Row(6, Ui.Icon(open ? "" : "", 9));
        foreach (var control in content) row.Children.Add(control);
        if (!open && _groupNews.Contains(group)) row.Children.Add(Ui.NewsDot());
        var head = new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 3, 0, 3), Child = row, Cursor = new Cursor(StandardCursorType.Hand) };
        head.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            ToggleFold(group);
        };
        return head;
    }

    /// <summary>Una lista con desplazamiento que conserva dónde se dejó al volver a pintar el widget.</summary>
    /// <remarks>La barra se pinta encima del contenido; el hueco de la derecha evita que pise el borde de las tarjetas.</remarks>
    protected ScrollViewer Scroll(string key, Control content)
    {
        var scroller = new ScrollViewer
        {
            Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, ScrollGap, 0),
        };
        if (_scrolls.TryGetValue(key, out var offset)) scroller.Offset = offset;
        _scrollers[key] = scroller;
        scroller.ScrollChanged += (_, _) =>
        {
            if (scroller.Offset.Y < 1) _above = 0;
            Float();
        };
        return scroller;
    }

    /// <summary>Apunta que el widget acaba de leer sus datos, para el «hace…» de su cabecera.</summary>
    protected void Refreshed() => _refreshed = DateTime.UtcNow;

    /// <summary>Apunta una cosa de una lista para los avisos de cambio y la devuelve lista para ponerla en su sitio: con un
    /// destello de su color si acaba de entrar o de cambiar desde la última vez que se vio.</summary>
    /// <param name="signature">Lo que, si cambia, cuenta como cambio: el estado, la columna, el número de commits.</param>
    /// <param name="group">El grupo plegable donde va, para su punto de novedad.</param>
    protected Control Watch(Control item, string list, string key, string signature, IBrush? color = null, string? group = null)
    {
        var id = list + "\n" + key;
        if (Note(list, key, signature, group) && Active) _glowUntil[id] = DateTime.UtcNow + GlowTime;
        var holder = new Panel { Children = { item } };
        _items[id] = holder;
        if (!_glowUntil.TryGetValue(id, out var until)) return holder;
        var left = until - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            _glowUntil.Remove(id);
            return holder;
        }
        // Repintado a mitad del destello, sigue desde donde iba en lugar de empezar otra vez.
        holder.Children.Add(Glow(color ?? Ui.Wait, item.Margin, left.TotalMilliseconds / GlowTime.TotalMilliseconds, left));
        if (left > GlowTime - TimeSpan.FromMilliseconds(300)) _novel.Add(holder);
        return holder;
    }

    /// <summary>Apunta una cosa de una lista sin pintarla, como las de un grupo plegado; dice si acaba de entrar o de cambiar.</summary>
    /// <remarks>La primera vez que se ve una lista nada cuenta como nuevo. Si cambia algo en un grupo plegado, el grupo lleva
    /// el punto de novedad; si cambia sin verse el widget, lo lleva su pestaña.</remarks>
    protected bool Note(string list, string key, string signature, string? group = null)
    {
        if (!_seen.TryGetValue(list, out var seen)) _seen[list] = seen = new Dictionary<string, string>(StringComparer.Ordinal);
        seen[key] = signature;
        if (!_known.TryGetValue(list, out var known)) return false;
        if (known.TryGetValue(key, out var old) && old == signature) return false;
        if (group is not null && Folded(group)) _groupNews.Add(group);
        if (!Active) HasNews = true;
        return true;
    }

    /// <summary>Lo que pide atención en el widget, para avisar con una pastilla flotante si no se ve entero.</summary>
    protected void Beacon(Control target, string text, IBrush color) => _beacon = (target, text, color);

    /// <summary>El destello de una cosa que acaba de cambiar: su color encima, que se apaga en lo que le queda.</summary>
    /// <remarks>Se apaga despacio al principio y deprisa al final, para que dé tiempo a verlo.</remarks>
    private static Control Glow(IBrush color, Thickness margin, double from, TimeSpan left)
    {
        var c = color is ISolidColorBrush solid ? solid.Color : Colors.Goldenrod;
        var glow = new Border
        {
            IsHitTestVisible = false, CornerRadius = new CornerRadius(7), Margin = margin, Opacity = from,
            Background = new SolidColorBrush(c, 0.24), BorderBrush = new SolidColorBrush(c, 0.9), BorderThickness = new Thickness(1.5),
            Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = left, Easing = new QuadraticEaseIn() } },
        };
        glow.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => glow.Opacity = 0, DispatcherPriority.Background);
        return glow;
    }

    /// <summary>Una ficha que tapa el widget, con «‹ Volver» arriba para cerrarla: el detalle de algo en S y M.</summary>
    protected Control Over(Control content, Action back, params Control[] right)
    {
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var go = Ui.Link("‹ Volver", back);
        DockPanel.SetDock(go, Dock.Left);
        top.Children.Add(go);
        var extra = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var control in right) extra.Children.Add(control);
        top.Children.Add(extra);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(content);
        return dock;
    }
}

/// <summary>Piezas comunes de los widgets: textos, pastillas, chips, tarjetas y enlaces, con los colores del tema.</summary>
internal static class Ui
{
    public static IBrush Fg => ThemeColors.Of("Fg", "#F2F3F7");
    public static IBrush Muted => ThemeColors.Of("FgMuted", "#B0B4C0");
    public static IBrush Faint => ThemeColors.Of("FgFaint", "#8A8D9C");
    public static IBrush Accent => ThemeColors.Of("Accent", "#61AFEF");
    public static IBrush Wait => ThemeColors.Of("DataFiveHour", "#E5C07B");
    public static IBrush Danger => ThemeColors.Of("Danger", "#E03E4C");
    public static IBrush Track => ThemeColors.Of("TrackFill", "#20FFFFFF");
    public static IBrush TrackEdge => ThemeColors.Of("TrackEdge", "#30FFFFFF");
    public static IBrush CardFill => ThemeColors.Of("TaskCardFill", "#14FFFFFF");
    public static IBrush CardEdge => ThemeColors.Of("TaskCardEdge", "#24FFFFFF");
    public static IBrush CardHover => ThemeColors.Of("TaskCardHoverEdge", "#40FFFFFF");
    public static IBrush Section => ThemeColors.Of("TaskSection", "#14FFFFFF");

    /// <summary>La letra de ancho fijo de la app, para claves, horas y carpetas.</summary>
    public static FontFamily Mono => Application.Current?.FindResource("MonoFont") as FontFamily ?? FontFamily.Default;

    /// <summary>Un texto de una línea que termina en «…» si no cabe, o que se parte si se pide.</summary>
    public static TextBlock Text(string? text, double size = 12, IBrush? color = null, bool bold = false, bool wrap = false, bool mono = false)
    {
        var block = new TextBlock
        {
            Text = text ?? "", FontSize = size, Foreground = color ?? Fg, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };
        if (mono) block.FontFamily = Mono;
        if (color is not null) block.Classes.Add("ownColor");
        return block;
    }

    /// <summary>Un título de grupo en mayúsculas pequeñas, como los del panel.</summary>
    public static TextBlock Label(string text)
    {
        var label = Text(text.ToUpperInvariant(), 10, Faint, bold: true);
        label.LetterSpacing = 0.6;
        return label;
    }

    /// <summary>Un punto de color.</summary>
    public static Ellipse Dot(IBrush brush, double size = 7) => new()
    {
        Width = size, Height = size, Fill = brush, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>El punto de novedad: algo ha cambiado donde no se ve (un grupo plegado, una columna apagada).</summary>
    public static Ellipse NewsDot()
    {
        var dot = Dot(Wait, 6);
        DiaryInk.Tip(dot, DiaryInk.Card("Hay novedades", "Algo ha cambiado aquí desde la última vez que se vio."));
        return dot;
    }

    /// <summary>Un rayado en diagonal del color dado: lo que aún no ha pasado pero se calcula, o lo que sigue en marcha.</summary>
    public static IBrush Hatch(IBrush color)
    {
        var c = color is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(6, 6, RelativeUnit.Absolute),
            SpreadMethod = GradientSpreadMethod.Repeat,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(110, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(110, c.R, c.G, c.B), 0.5),
                new GradientStop(Color.FromArgb(25, c.R, c.G, c.B), 0.5), new GradientStop(Color.FromArgb(25, c.R, c.G, c.B), 1),
            },
        };
    }

    /// <summary>Un icono de la letra de iconos de la app.</summary>
    public static TextBlock Icon(string glyph, double size = 11, IBrush? color = null)
    {
        var icon = new TextBlock { Text = glyph, FontSize = size, Foreground = color ?? Muted, VerticalAlignment = VerticalAlignment.Center };
        icon.Classes.Add("icon");
        return icon;
    }

    /// <summary>Una fila de cosas, una detrás de otra, con un hueco entre ellas.</summary>
    public static StackPanel Row(double spacing, params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    /// <summary>Una columna de cosas, una debajo de otra, con un hueco entre ellas.</summary>
    public static StackPanel Column(double spacing, params Control[] children)
    {
        var column = new StackPanel { Spacing = spacing };
        foreach (var child in children) column.Children.Add(child);
        return column;
    }

    /// <summary>Una pastilla redondeada con un texto, en un color sobre su tinte.</summary>
    public static Border Pill(string text, IBrush color, IBrush? fill = null) => new()
    {
        CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 8, 2), Background = fill ?? Track, VerticalAlignment = VerticalAlignment.Center,
        Child = Text(text, 10.5, color, bold: true),
    };

    /// <summary>Un chip que se enciende: el de un filtro o una opción de la barra.</summary>
    public static ToggleButton Chip(object content, bool on, Action click, Control? tip = null)
    {
        var chip = new ToggleButton { Content = content, IsChecked = on, Focusable = false };
        if (Application.Current?.FindResource("ChipToggle") is ControlTheme theme) chip.Theme = theme;
        chip.IsCheckedChanged += (_, _) =>
        {
            if (chip.IsChecked != on) click();
        };
        if (tip is not null) DiaryInk.Tip(chip, tip);
        return chip;
    }

    /// <summary>Un botón pequeño con un icono, como los de la barra de las vistas.</summary>
    public static Button IconButton(string glyph, Action click, Control? tip = null)
    {
        var button = new Button { Content = Icon(glyph, 11), Focusable = false, Padding = new Thickness(8, 4) };
        if (Application.Current?.FindResource("ChipButton") is ControlTheme theme) button.Theme = theme;
        button.Click += (_, _) => click();
        if (tip is not null) DiaryInk.Tip(button, tip);
        return button;
    }

    /// <summary>Un texto que se pulsa, en el color de acento.</summary>
    public static Control Link(string text, Action click)
    {
        var link = Text(text, 12, Accent, bold: true);
        link.Cursor = new Cursor(StandardCursorType.Hand);
        // Marcado como atendido: dentro de una tarjeta que se pulsa, el clic es del enlace y no de la tarjeta.
        link.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            click();
        };
        return link;
    }

    /// <summary>Una tarjeta con borde que se marca al pasar el ratón y que, si se pide, se pulsa.</summary>
    /// <param name="stripe">El color de la raya de la izquierda, si lleva.</param>
    public static Border Card(Control child, Action? click = null, IBrush? stripe = null, IBrush? fill = null, IBrush? edge = null, bool on = false)
    {
        var card = new Border
        {
            Background = on ? ThemeColors.Of("Accent", "#61AFEF", 0.14) : fill ?? CardFill, BorderBrush = on ? Accent : edge ?? CardEdge,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(9, 6, 9, 7), Child = child,
        };
        // Con raya, el borde es solo la raya: un borde de dos colores no lo dibuja un Border.
        if (stripe is not null)
        {
            card.BorderBrush = stripe;
            card.BorderThickness = new Thickness(3, 0, 0, 0);
            card.CornerRadius = new CornerRadius(2, 7, 7, 2);
        }
        if (click is null) return card;
        card.Cursor = new Cursor(StandardCursorType.Hand);
        var rest = card.BorderBrush;
        if (stripe is null)
        {
            card.PointerEntered += (_, _) => card.BorderBrush = CardHover;
            card.PointerExited += (_, _) => card.BorderBrush = rest;
        }
        else
        {
            var restFill = card.Background;
            card.PointerEntered += (_, _) => card.Background = Track;
            card.PointerExited += (_, _) => card.Background = restFill;
        }
        card.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) click();
        };
        return card;
    }

    /// <summary>Un bloque del detalle de algo: un fondo propio para separar sus datos.</summary>
    public static Border Block(params Control[] children) => new()
    {
        Background = Section, CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 8), Child = Column(4, children),
    };

    /// <summary>Un texto apagado en el centro, para cuando no hay nada que enseñar.</summary>
    public static Control Empty(string text)
    {
        var empty = Text(text, 12, Faint, wrap: true);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.TextAlignment = TextAlignment.Center;
        empty.Margin = new Thickness(12, 24);
        return empty;
    }

    /// <summary>Un aviso de una línea, en el color de peligro: que no se ha podido leer algo.</summary>
    public static Control Warning(string text)
    {
        var warning = new Border
        {
            Background = ThemeColors.Of("Danger", "#E03E4C", 0.12), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5),
            Child = Text(text, 11, Danger),
        };
        DiaryInk.Tip(warning, DiaryInk.Card("No se ha podido leer", text));
        return warning;
    }

    /// <summary>Una rejilla de columnas iguales con sus hijos, de izquierda a derecha y de arriba abajo.</summary>
    public static Grid Columns(int count, double gap, IEnumerable<Control> children, bool stretch = false)
    {
        var grid = new Grid();
        for (var i = 0; i < count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var list = children.ToList();
        var rows = (list.Count + count - 1) / Math.Max(1, count);
        for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition(stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto));
        for (var i = 0; i < list.Count; i++)
        {
            var child = list[i];
            Grid.SetColumn(child, i % count);
            Grid.SetRow(child, i / count);
            child.Margin = new Thickness(i % count == 0 ? 0 : gap / 2, i / count == 0 ? 0 : gap / 2, i % count == count - 1 ? 0 : gap / 2, i / count == rows - 1 ? 0 : gap / 2);
            grid.Children.Add(child);
        }
        return grid;
    }

    /// <summary>Una opción del menú de un widget, con su acción.</summary>
    public static MenuItem Item(string header, Action act, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => act();
        return item;
    }

    /// <summary>Una opción del menú de un widget que se marca y se desmarca.</summary>
    public static MenuItem Check(string header, bool on, Action act)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
        item.Click += (_, _) => act();
        return item;
    }

    /// <summary>Un título de grupo del menú de un widget, que no se pulsa.</summary>
    public static MenuItem Caption(string header)
    {
        var caption = new MenuItem { Header = header };
        caption.Classes.Add("caption");
        return caption;
    }
}

/// <summary>Una lista que enseña, de arriba abajo, lo que cabe en su alto y, debajo, «+ N más» con lo que no cabe; nunca se
/// desplaza. Al pulsar «+ N más», hace lo que se le dé (abrir la lista entera).</summary>
/// <remarks>
/// Con alto sin límite (dentro de algo que se desplaza) lo enseña todo. Lo que no cabe se coloca fuera de su caja, que
/// recorta, en lugar de esconderse: cambiar si se ve en mitad de la medida volvería a pedir otra.
/// </remarks>
internal sealed class FitList : Panel
{
    private readonly TextBlock _more;
    private int _shown;

    /// <summary>Crea la lista vacía, con lo que hace «+ N más» al pulsarlo.</summary>
    public FitList(Action more)
    {
        ClipToBounds = true;
        _more = Ui.Text("", 11.5, Ui.Accent, bold: true);
        _more.Cursor = new Cursor(StandardCursorType.Hand);
        _more.Margin = new Thickness(2, 0, 0, 0);
        _more.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            more();
        };
        Children.Add(_more);
    }

    /// <summary>El hueco entre una cosa y la siguiente.</summary>
    public double Spacing { get; init; } = 6;

    /// <summary>Añade una cosa al final de la lista.</summary>
    public void Add(Control item) => Children.Insert(Children.Count - 1, item);

    /// <summary>Cuántas cosas tiene.</summary>
    public int Count => Children.Count - 1;

    /// <summary>El alto que necesita para enseñar las primeras, con «+ N más» si quedan otras; con lo que midió sin límite.</summary>
    public double HeightFor(int shown)
    {
        var items = Children.Where(c => c != _more).ToList();
        var sum = items.Take(shown).Sum(c => c.DesiredSize.Height) + Spacing * Math.Max(0, shown - 1);
        return shown >= items.Count ? sum : sum + (shown > 0 ? Spacing : 0) + Math.Ceiling(_more.FontSize * 1.4);
    }

    protected override Avalonia.Size MeasureOverride(Avalonia.Size available)
    {
        var items = Children.Where(c => c != _more).ToList();
        var width = available.Width;
        var room = available.Height;
        double used = 0;
        _shown = 0;
        // El alto de «+ N más» es el de una línea: medirlo con otro texto cambiaría el suyo y pediría otra medida sin fin.
        var moreHeight = Math.Ceiling(_more.FontSize * 1.4);
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Measure(new Avalonia.Size(width, double.PositiveInfinity));
            var next = used + (i > 0 ? Spacing : 0) + items[i].DesiredSize.Height;
            var last = i == items.Count - 1;
            if (!double.IsInfinity(room) && next + (last ? 0 : Spacing + moreHeight) > room) break;
            used = next;
            _shown++;
        }
        var hidden = items.Count - _shown;
        var text = hidden > 0 ? $"+ {hidden} más" : "";
        if (_more.Text != text) _more.Text = text;
        _more.Measure(new Avalonia.Size(width, double.PositiveInfinity));
        if (hidden > 0) used += (_shown > 0 ? Spacing : 0) + moreHeight;
        var widest = items.Take(_shown).Select(c => c.DesiredSize.Width).DefaultIfEmpty(0).Max();
        return new Avalonia.Size(double.IsInfinity(width) ? widest : width, used);
    }

    protected override Avalonia.Size ArrangeOverride(Avalonia.Size final)
    {
        var items = Children.Where(c => c != _more).ToList();
        double y = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (i >= _shown)
            {
                item.Arrange(new Rect(0, final.Height + 1000, final.Width, item.DesiredSize.Height));
                continue;
            }
            item.Arrange(new Rect(0, y, final.Width, item.DesiredSize.Height));
            y += item.DesiredSize.Height + Spacing;
        }
        if (_shown < items.Count) _more.Arrange(new Rect(0, y, final.Width, Math.Max(_more.DesiredSize.Height, Math.Ceiling(_more.FontSize * 1.4))));
        else _more.Arrange(new Rect(0, final.Height + 1000, 0, 0));
        return final;
    }
}

/// <summary>Cosas una debajo de otra que se reparten el alto: las cabeceras, lo que piden; las listas que caben a medias
/// (<see cref="FitList"/>), lo que queda, una cosa cada vez y por turnos, para que todas enseñen algo.</summary>
/// <remarks>
/// Así una columna con una tarjeta la enseña entera y lo que no usa pasa a las largas, que muestran lo que les toca y su
/// «+ N más». Con alto sin límite, cada lista lo enseña todo.
/// </remarks>
internal sealed class SharedRows : Panel
{
    private readonly Dictionary<Control, double> _heights = new();

    protected override Avalonia.Size MeasureOverride(Avalonia.Size available)
    {
        var width = available.Width;
        var tall = new Avalonia.Size(width, double.PositiveInfinity);
        foreach (var child in Children) child.Measure(tall);
        if (double.IsInfinity(available.Height))
        {
            foreach (var child in Children) _heights[child] = child.DesiredSize.Height;
            return new Avalonia.Size(width, Children.Sum(c => c.DesiredSize.Height));
        }
        var shared = Children.OfType<FitList>().ToList();
        var fixedHeight = Children.Where(c => c is not FitList).Sum(c => c.DesiredSize.Height);
        foreach (var child in Children.Where(c => c is not FitList)) _heights[child] = child.DesiredSize.Height;
        // Por tarjetas, una vuelta tras otra: cada lista gana su siguiente cosa mientras quepa, empezando todas por su «+ N más».
        double Edges(FitList list) => list.Margin.Top + list.Margin.Bottom;
        var shown = shared.ToDictionary(l => l, _ => 0);
        var left = available.Height - fixedHeight - shared.Sum(l => l.HeightFor(0) + Edges(l));
        for (var grew = true; grew && left > 0;)
        {
            grew = false;
            foreach (var list in shared)
            {
                if (shown[list] >= list.Count) continue;
                var more = list.HeightFor(shown[list] + 1) - list.HeightFor(shown[list]);
                if (more > left) continue;
                shown[list]++;
                left -= more;
                grew = true;
            }
        }
        foreach (var list in shared) _heights[list] = Math.Ceiling(list.HeightFor(shown[list]) + Edges(list)) + 0.5;
        foreach (var list in shared) list.Measure(new Avalonia.Size(width, _heights[list]));
        return new Avalonia.Size(width, Children.Sum(c => _heights.GetValueOrDefault(c)));
    }

    protected override Avalonia.Size ArrangeOverride(Avalonia.Size final)
    {
        double y = 0;
        foreach (var child in Children)
        {
            var height = _heights.GetValueOrDefault(child, child.DesiredSize.Height);
            child.Arrange(new Rect(0, y, final.Width, height));
            y += height;
        }
        return final;
    }
}
