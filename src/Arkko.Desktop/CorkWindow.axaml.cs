using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Un corcho: una ventana que empieza vacía y se divide en recuadros, y en cada recuadro, uno o varios paneles de
/// Sesiones en pestañas, colocados a gusto.</summary>
/// <remarks>
/// La forma es un árbol: cada división reparte su sitio, en horizontal o en vertical, entre sus hijos, y cada hoja es un
/// recuadro con sus pestañas. Lo que se ve (cabeceras, separadores y paneles) se coloca sobre una sola superficie
/// (<see cref="CorkBoard"/>): partir o juntar recuadros solo cambia dónde va cada panel, sin recargar su página. Un
/// recuadro nuevo parte el más grande, a lo ancho si es más ancho que alto y a lo alto si no, como los gestores de
/// ventanas en mosaico. Puede haber varios corchos; lo que se clava va al último que se usó. Los corchos se guardan
/// (<see cref="CorkStore"/>) cada vez que cambian, y vuelven al arrancar la app, cada uno en su sitio y con sus paneles;
/// cerrar uno a mano lo olvida, y cerrar la app no. Un panel se lleva a otro sitio arrastrando la cabecera de su
/// recuadro: a un lado de otro recuadro, a sus pestañas, a otro corcho o fuera, a una ventana suelta.
/// </remarks>
public partial class CorkWindow : Window, IPieceHost
{
    // El alto de la cabecera de un recuadro y el grueso de un separador.
    private const double HeadHeight = 40;
    private const double Gap = 8;

    // Lo menos que puede medir un recuadro al arrastrar un separador.
    private const double MinPart = 140;

    // Lo que hay que mover el ratón con la cabecera agarrada para que cuente como arrastrar, y no como pulsar.
    private const double DragStart = 8;

    // La franja de cada borde de un recuadro que, al soltar encima, lo parte por ese lado; el resto lo junta en pestañas.
    private const double EdgeShare = 0.25;

    // La franja junto al borde del corcho donde soltar abre una columna o una fila entera, de lado a lado.
    private const double EdgeBand = 28;

    private static readonly List<CorkWindow> Corks = [];
    private static CorkWindow? _last;

    // Se guarda un poco después del último cambio, para no escribir el fichero con cada paso de un arrastre. Al cerrar la
    // app se guarda en el acto y ya no se toca: los corchos que se cierran entonces no se olvidan.
    private static readonly DispatcherTimer SaveTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private static bool _quitting;

    // Cada poco se mira el estado de las sesiones clavadas, para la barra y el punto de cada cabecera.
    private static readonly DispatcherTimer StatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    static CorkWindow()
    {
        SaveTimer.Tick += (_, _) =>
        {
            SaveTimer.Stop();
            SaveNow();
        };
        StatusTimer.Tick += (_, _) =>
        {
            foreach (var cork in Corks)
            {
                cork.ShowStatus();
                // Por si el sistema no avisa del cambio de pantallas.
                cork.OnScreensChanged();
            }
        };
        RepoColors.Changed += () =>
        {
            foreach (var cork in Corks) cork.ShowStatus(force: true);
        };
        // Las cabeceras toman del tema el borde de las vistas de la app y el fondo de la pestaña elegida.
        App.ThemeChanged += () =>
        {
            foreach (var cork in Corks) cork.ShowStatus(force: true);
        };
    }

    private readonly int _number;
    private Node? _root;

    // Cómo estaban las sesiones la última vez que se pintó la barra: si no cambia, no se vuelve a pintar.
    private string _status = "";

    // El nombre que le ha puesto la persona; sin él, se llama por su número.
    private string? _name;

    /// <summary>Cómo se llama el corcho: el nombre que se le ha puesto o «Corcho N».</summary>
    internal string DisplayName => _name ?? $"Corcho {_number}";

    // Dónde estaba y cuánto medía la ventana la última vez que se guardó sin estar maximizada ni minimizada.
    private PixelPoint _normalAt;
    private Size _normalSize;

    // Dónde va con cada juego de pantallas, por su huella; y el juego para el que vale su sitio de ahora. Mientras no
    // coinciden con las pantallas conectadas, el cambio está a medio atender y no se guarda su sitio.
    private Dictionary<string, CorkPlace> _places = new();
    private string? _screenKey;

    // Lo que no son paneles (cabeceras, separadores y la marca de un arrastre), por lo que acompañan: se reutilizan
    // mientras siga eso mismo, para que no parpadeen. Y dónde quedó cada recuadro.
    private readonly Dictionary<object, Control> _chrome = new();
    private readonly Dictionary<Node, Rect> _rects = new();

    // Mientras un arrastre tiene un hueco abierto aquí, dónde estaba cada recuadro antes de abrirlo: con eso se decide
    // dónde caería lo que se suelta, para que lo que se aparta no cambie a qué se apunta.
    private Dictionary<Node, Rect>? _dropRects;

    // Si ya hay pedida una recolocación para el próximo fotograma.
    private bool _relayoutAsked;

    /// <summary>Un trozo del árbol: una división o un recuadro.</summary>
    private abstract class Node
    {
        public Split? Parent;
    }

    /// <summary>Una división: reparte su sitio entre sus hijos, uno al lado del otro o uno debajo del otro, según sus pesos.</summary>
    private sealed class Split : Node
    {
        public Orientation Orientation;
        public readonly List<Node> Children = [];
        public readonly List<double> Weights = [];
    }

    /// <summary>Dónde cae un panel que se suelta: en las pestañas de un recuadro, a uno de sus lados, o junto a un borde
    /// del corcho, en una columna o una fila entera.</summary>
    private enum Zone { Center, Left, Right, Top, Bottom, EdgeLeft, EdgeRight, EdgeTop, EdgeBottom }

    /// <summary>Dónde caería un panel arrastrado: en qué corcho, junto a qué recuadro (ninguno sobre un separador) y por
    /// qué lado, con el rectángulo de la pantalla que se marca; sin corcho, fuera de todos.</summary>
    private sealed record Drop(CorkWindow? Cork, Stack? Stack, Zone Zone, PixelRect Area);

    // La marca de dónde caerá lo que se arrastra a las pestañas de un recuadro, encima de su página, común a todos los
    // corchos; y la tarjeta que sigue al cursor.
    private static DropHint? _hint;
    private static DragGhost? _ghost;

    /// <summary>Un recuadro: sus paneles, en pestañas, y el que se ve.</summary>
    /// <remarks>Un recuadro sin paneles es un hueco (<see cref="Hole"/>): un sitio que se deja en la composición para
    /// llenarlo después, que se ve como una tecla en blanco y se guarda con el corcho. El hueco que abre un arrastre,
    /// donde caerá lo que se suelte, es otra cosa: lleva <see cref="Preview"/>, se ve como una marca, no se guarda y no
    /// cuenta como sitio donde soltar.</remarks>
    private sealed class Stack : Node
    {
        public readonly List<PiecePane> Panes = [];
        public int Selected;
        public bool Preview;

        // Lo que dice la marca del hueco de un arrastre, si no es lo de siempre.
        public string? Label;

        // En un hueco que dejó un perfil, las partes que espera («live», «chat»…): su tecla solo pide de qué sesión.
        public List<string>? Want;

        public bool Hole => Panes.Count == 0 && !Preview;
    }

    /// <summary>Un arrastre en curso: el panel, el corcho de donde sale, y dónde caería ahora, con el hueco que lo enseña y
    /// cómo quitarlo.</summary>
    private sealed class Dragging(PiecePane pane, CorkWindow source)
    {
        public readonly PiecePane Pane = pane;
        public readonly CorkWindow Source = source;
        public Drop? Target;
        public Stack? Hole;
        public Action? Undo;

        // El hueco de la composición sobre el que está ahora, con su piloto encendido.
        public (CorkWindow Cork, Stack Stack)? Lit;
    }

    // El piloto y el borde de cada hueco, para encenderlos cuando se le pasa por encima un panel arrastrado.
    private readonly Dictionary<Stack, (Border Led, Rectangle Edge)> _holeParts = new();

    private static Dragging? _drag;

    /// <summary>Lo necesita el diseñador de XAML; la app crea los corchos con <see cref="Pin"/>.</summary>
    public CorkWindow() : this(1)
    {
    }

    /// <summary>Monta un corcho vacío con su número.</summary>
    private CorkWindow(int number)
    {
        InitializeComponent();
        _number = number;
        // Un corcho vacío es un hueco que lo ocupa todo, esperando lo que se clave.
        _root = new Stack();
        PinMark.Content = CorkIcons.Make("pin", 14, KeyMuted);
        ShowName();
        NameText.DoubleTapped += (_, e) =>
        {
            Rename();
            e.Handled = true;
        };
        var barMenu = new ContextMenu();
        barMenu.Opening += (_, _) => FillBarMenu(barMenu);
        BarLeft.ContextMenu = barMenu;
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            EndRename(keep: e.Key == Key.Enter);
            e.Handled = true;
        };
        NameBox.LostFocus += (_, _) => EndRename(keep: true);
        if (!StatusTimer.IsEnabled) StatusTimer.Start();
        Board.SizeChanged += (_, _) => Relayout();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _drag is null) return;
            CancelDrag();
            e.Handled = true;
        };
        Activated += (_, _) =>
        {
            _last = this;
            ScheduleSave();
        };
        Opened += (_, _) =>
        {
            _screenKey ??= ScreenKey();
            Screens.Changed += (_, _) => Dispatcher.UIThread.Post(OnScreensChanged);
            if (WindowState != WindowState.Normal) return;
            _normalAt = Position;
            _normalSize = ClientSize;
        };
        PositionChanged += (_, _) => ScheduleSave();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty) ScheduleSave();
            if (e.Property != WindowStateProperty) return;
            ScheduleSave();
            var maximized = WindowState == WindowState.Maximized;
            MaximizeButton.Content = maximized ? "" : "";
            ToolTip.SetTip(MaximizeButton, maximized ? "Restaurar" : "Maximizar");
            Relayout();
        };
        // Al cerrar el corcho se cierran sus paneles; si lo ha cerrado la persona, y no la app al salir, se olvida.
        Closed += (_, _) =>
        {
            Corks.Remove(this);
            if (_last == this) _last = null;
            foreach (var pane in Panes().ToList())
            {
                Board.Children.Remove(pane);
                pane.Close();
            }
            _root = null;
            ScheduleSave();
            ListChanged?.Invoke();
        };
    }

    /// <summary>Clava un panel en el corcho que se usó por última vez, en un recuadro nuevo; si no hay ninguno, abre uno.</summary>
    internal static void Pin(PiecePane pane) => PinTo(pane, null, fresh: false);

    /// <summary>Clava un panel en un corcho: uno nuevo, el de ese número o, si no se dice o ya no está, el último que se usó.</summary>
    internal static void PinTo(PiecePane pane, int? number, bool fresh)
    {
        var cork = fresh ? Create()
            : Corks.FirstOrDefault(c => c._number == number)
              ?? (_last is { } last && Corks.Contains(last) ? last : Corks.LastOrDefault() ?? Create());
        cork.Add(pane);
        cork.Bring();
    }

    /// <summary>Abre un panel en un recuadro nuevo del corcho donde va otro; si ya está abierto, lo pone a la vista donde
    /// esté. Si el otro no va en un corcho, se abre como siempre.</summary>
    internal static void OpenBeside(PiecePane beside, PieceSpec spec)
    {
        if (PiecePane.Find(spec) is { } open)
        {
            open.ShowRound(spec);
            Pieces.Reveal(open);
            return;
        }
        if (TopLevel.GetTopLevel(beside) is not CorkWindow cork)
        {
            Pieces.Open(spec);
            return;
        }
        cork.Add(Pieces.Make(spec));
    }

    /// <summary>Abre la ficha de un fichero desde los cambios del corcho donde van: como pestaña nueva del recuadro que ya
    /// tenga fichas de esa sesión o, si no hay o se pide uno nuevo, en un hueco libre o en un recuadro nuevo a la derecha
    /// de los cambios. Si ya está abierta, la pone a la vista; si los cambios no van en un corcho, se abre como siempre.</summary>
    internal static void OpenFile(PiecePane from, PieceSpec spec, bool fresh)
    {
        if (PiecePane.Find(spec) is { } open)
        {
            open.ShowRound(spec);
            Pieces.Reveal(open);
            return;
        }
        if (TopLevel.GetTopLevel(from) is not CorkWindow cork || cork.StackOf(from) is not { } source)
        {
            Pieces.Open(spec);
            return;
        }
        var pane = Pieces.Make(spec);
        var files = fresh ? null : cork.Stacks().FirstOrDefault(s => s != source
            && s.Panes.Any(p => p.Spec.Kind == "file" && string.Equals(p.Spec.SessionId, spec.SessionId, StringComparison.OrdinalIgnoreCase)));
        if (files is not null)
        {
            files.Panes.Add(pane);
            files.Selected = files.Panes.Count - 1;
        }
        else if (cork.Stacks().FirstOrDefault(s => s.Hole && s.Want is null) is { } hole) hole.Panes.Add(pane);
        else
        {
            var stack = new Stack();
            stack.Panes.Add(pane);
            cork.SplitBeside(source, stack, Orientation.Horizontal);
        }
        if (!cork.Board.Children.Contains(pane)) cork.Board.Children.Add(pane);
        cork.Rebuild();
    }

    /// <summary>Quita un panel de donde esté: en un corcho, de su recuadro, que se queda como un hueco si era el último; en
    /// una ventana suelta, la cierra.</summary>
    internal static void EmptyWhereItIs(PiecePane pane)
    {
        switch (TopLevel.GetTopLevel(pane))
        {
            case CorkWindow cork:
                cork.EmptyPane(pane);
                break;
            case PieceWindow window:
                window.Close();
                break;
        }
    }

    /// <summary>Abre un panel como pestaña nueva del recuadro donde va otro, y la enseña; si ya está abierto, lo pone a la
    /// vista donde esté. Si el otro no va en un corcho, se abre como siempre.</summary>
    internal static void OpenAsTab(PiecePane beside, PieceSpec spec)
    {
        if (PiecePane.Find(spec) is { } open)
        {
            open.ShowRound(spec);
            Pieces.Reveal(open);
            return;
        }
        if (TopLevel.GetTopLevel(beside) is not CorkWindow cork || cork.StackOf(beside) is not { } stack)
        {
            Pieces.Open(spec);
            return;
        }
        var pane = Pieces.Make(spec);
        stack.Panes.Add(pane);
        stack.Selected = stack.Panes.Count - 1;
        if (!cork.Board.Children.Contains(pane)) cork.Board.Children.Add(pane);
        cork.Rebuild();
    }

    /// <summary>Pone un panel ya hecho en un recuadro nuevo del corcho donde va otro; false si el otro no va en un corcho.</summary>
    internal static bool AddBeside(PiecePane beside, PiecePane pane)
    {
        if (TopLevel.GetTopLevel(beside) is not CorkWindow cork) return false;
        cork.Add(pane);
        return true;
    }

    /// <summary>Avisa de que algo se ha abierto, cerrado o cambiado de sitio fuera de un corcho, en una ventana suelta.</summary>
    internal static void NotifyListChanged() => ListChanged?.Invoke();

    /// <summary>Saca un panel del corcho donde esté, para ponerlo en otro sitio.</summary>
    internal static void TakeOut(PiecePane pane)
    {
        if (TopLevel.GetTopLevel(pane) is not CorkWindow cork) return;
        cork.Detach(pane, offBoard: true);
        cork.Rebuild();
    }

    /// <summary>Los corchos abiertos, por su número: su número, su nombre y cuántos paneles tiene cada uno, para que una
    /// página ofrezca dónde clavar.</summary>
    internal static IReadOnlyList<object> List() =>
        Corks.OrderBy(c => c._number).Select(c => (object)new { id = c._number, name = c.DisplayName, count = c.Panes().Count() }).ToList();

    /// <summary>Ha cambiado la lista de corchos: uno nuevo, uno cerrado, un nombre o lo que tiene alguno.</summary>
    internal static event Action? ListChanged;

    /// <summary>Vuelve a abrir los corchos que había al cerrar la app, cada uno con sus recuadros y sus paneles y en el
    /// sitio que tenía con las pantallas conectadas ahora.</summary>
    /// <remarks>Sin sitio para este juego de pantallas, va donde estuvo la última vez; si eso cae fuera de las pantallas,
    /// centrado. Un panel que ya está abierto en otro sitio, o que no se sabe abrir, se salta.</remarks>
    internal static void Restore()
    {
        foreach (var saved in CorkStore.Load().Corks)
        {
            if (saved.Number < 1 || Corks.Any(c => c._number == saved.Number)) continue;
            var cork = new CorkWindow(saved.Number);
            cork._root = cork.Load(saved.Root, null) ?? new Stack();
            cork._name = string.IsNullOrWhiteSpace(saved.Name) ? null : saved.Name;
            cork._places = saved.Places ?? new();
            cork.ShowName();
            foreach (var pane in cork.Panes()) cork.Board.Children.Add(pane);
            var key = cork.ScreenKey();
            cork._screenKey = key;
            var place = cork._places.GetValueOrDefault(key)
                        ?? new CorkPlace { X = saved.X, Y = saved.Y, Width = saved.Width, Height = saved.Height, Maximized = saved.Maximized };
            if (place.Width > 0 && place.Height > 0) (cork.Width, cork.Height) = (place.Width, place.Height);
            if (ScreenSet.Reachable(WorkAreas(cork), place.X, place.Y))
            {
                cork.WindowStartupLocation = WindowStartupLocation.Manual;
                cork.Position = new PixelPoint(place.X, place.Y);
            }
            Corks.Add(cork);
            cork.Show();
            if (place.Maximized) cork.WindowState = WindowState.Maximized;
            cork.Rebuild();
            _last = cork;
        }
    }

    // LAS PANTALLAS

    /// <summary>La huella de las pantallas conectadas ahora.</summary>
    private string ScreenKey() => ScreenSet.Key(Screens.All.Select(s => new ScreenArea(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, s.Scaling)));

    /// <summary>Las zonas útiles de las pantallas conectadas ahora, sin la barra de tareas.</summary>
    private static List<ScreenArea> WorkAreas(Window window) =>
        window.Screens.All.Select(s => new ScreenArea(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height, s.Scaling)).ToList();

    /// <summary>Al conectar o desconectar pantallas: el corcho va al sitio que tenía con este juego; si no tiene, se queda
    /// donde lo deje el sistema, y si eso no se ve, a la pantalla principal.</summary>
    /// <remarks>Lo guardado para el juego de antes no se toca: al volver a él, cada corcho vuelve a su pantalla.</remarks>
    private void OnScreensChanged()
    {
        var key = ScreenKey();
        if (key == _screenKey) return;
        _screenKey = key;
        if (_places.TryGetValue(key, out var place)) MoveTo(place);
        else KeepReachable();
        ScheduleSave();
    }

    /// <summary>Lleva la ventana a un sitio guardado, entera dentro de las pantallas, y la maximiza si lo estaba.</summary>
    private void MoveTo(CorkPlace place)
    {
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        if (place.Width > 0 && place.Height > 0) (Width, Height) = (place.Width, place.Height);
        Position = new PixelPoint(place.X, place.Y);
        KeepReachable();
        if (place.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>El sitio sin maximizar de una ventana maximizada, en la pantalla donde está maximizada: si se movió
    /// maximizada a otra pantalla, su sitio de antes se lleva a esa, en el mismo punto de ella y entero dentro.</summary>
    /// <remarks>El sistema no avisa de dónde queda el sitio normal al mover una ventana maximizada: sin esto, al volver
    /// a abrir la app se maximizaba en la pantalla de antes.</remarks>
    private PixelPoint NormalOnItsScreen()
    {
        if (Screens.ScreenFromVisual(this) is not { } now) return _normalAt;
        var before = Screens.ScreenFromPoint(_normalAt);
        if (before is not null && before.Bounds == now.Bounds) return _normalAt;
        var from = before?.WorkingArea ?? now.WorkingArea;
        var to = now.WorkingArea;
        var area = new ScreenArea(to.X, to.Y, to.Width, to.Height, now.Scaling);
        var (width, height) = ((int)(_normalSize.Width * now.Scaling), (int)(_normalSize.Height * now.Scaling));
        var (x, y, _, _) = ScreenSet.Fit(area, to.X + _normalAt.X - from.X, to.Y + _normalAt.Y - from.Y, width, height);
        return new PixelPoint(x, y);
    }

    /// <summary>Si no se puede agarrar la ventana porque su pantalla ya no está, la encaja centrada en la principal.</summary>
    private void KeepReachable()
    {
        var areas = WorkAreas(this);
        if (areas.Count == 0 || ScreenSet.Reachable(areas, Position.X, Position.Y)) return;
        var maximized = WindowState == WindowState.Maximized;
        if (maximized) WindowState = WindowState.Normal;
        var primary = Screens.Primary ?? Screens.All[0];
        var area = new ScreenArea(primary.WorkingArea.X, primary.WorkingArea.Y, primary.WorkingArea.Width, primary.WorkingArea.Height, primary.Scaling);
        var (width, height) = ((int)(Width * primary.Scaling), (int)(Height * primary.Scaling));
        var (x, y, fitWidth, fitHeight) = ScreenSet.Fit(area, area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        (Width, Height) = (fitWidth / primary.Scaling, fitHeight / primary.Scaling);
        Position = new PixelPoint(x, y);
        if (maximized) WindowState = WindowState.Maximized;
    }

    // LOS PERFILES

    /// <summary>Los nombres de los perfiles de corcho guardados, para que una página ofrezca abrir un corcho con uno.</summary>
    internal static IReadOnlyList<string> ProfileNames() => CorkStore.LoadProfiles().Profiles.Select(p => p.Name).ToList();

    /// <summary>Los nombres de las plantillas que vienen con la app, sin las que la persona ya guardó como perfil suyo.</summary>
    internal static IReadOnlyList<string> TemplateNames() => Templates(CorkStore.LoadProfiles().Profiles).Select(t => t.Profile.Name).ToList();

    /// <summary>Las plantillas que vienen con la app, con lo que llevan, sin las que tienen el nombre de un perfil guardado:
    /// guardar una plantilla como perfil la sustituye por la versión de la persona.</summary>
    private static IEnumerable<(CorkProfile Profile, string Hint)> Templates(IEnumerable<CorkProfile> saved)
    {
        var names = saved.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return CorkTemplates.All.Where(t => !names.Contains(t.Profile.Name));
    }

    /// <summary>Pone en el menú del clic derecho de la barra lo que se hace con el corcho: cambiarle el nombre, guardarlo
    /// como plantilla con ese nombre, abrir una plantilla (las guardadas y las de la app, todas en el mismo desplegable) y
    /// borrar las guardadas.</summary>
    /// <remarks>En código se llaman perfiles; para la persona, plantillas.</remarks>
    private void FillBarMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        menu.Items.Add(Caption(Labelled(CorkIcons.Make("pin", 13), DisplayName)));
        menu.Items.Add(Item("Cambiar el nombre", "edit", Rename));
        menu.Items.Add(new Separator());
        menu.Items.Add(Caption("Plantillas de corcho"));
        var profiles = CorkStore.LoadProfiles().Profiles;
        var exists = profiles.Any(p => string.Equals(p.Name, DisplayName, StringComparison.OrdinalIgnoreCase));
        menu.Items.Add(Item(exists ? $"Actualizar la plantilla «{DisplayName}»" : $"Guardar como plantilla «{DisplayName}»", "plus", SaveProfile,
            enabled: Panes().Any()));

        // Se monta con huecos que esperan cada parte; la sesión se elige después, en cada hueco o para todo el corcho.
        // Las guardadas y, debajo, las que vienen con la app: todas en el mismo desplegable. Guardada con su nombre, la de la
        // persona sustituye a la de la app.
        var open = Item("Abrir una plantilla", "grid", null);
        open.Items.Add(Caption(Panes().Any() ? "En un corcho nuevo" : "En este corcho"));
        foreach (var profile in profiles)
        {
            var name = profile.Name;
            open.Items.Add(Item(Hinted(name, Boxes(profile.Root)), "grid", () => OpenProfile(name, null, this)));
        }
        var builtIn = Templates(profiles).ToList();
        if (builtIn.Count > 0)
        {
            if (profiles.Count > 0) open.Items.Add(new Separator());
            open.Items.Add(Caption("De la app"));
            foreach (var (template, hint) in builtIn)
                open.Items.Add(Item(Hinted(template.Name, hint), "grid", () => OpenProfile(template, null, this)));
        }
        open.IsEnabled = profiles.Count + builtIn.Count > 0;
        menu.Items.Add(open);

        var danger = this.FindResource("Danger") as IBrush ?? Brushes.IndianRed;
        var remove = Item("Borrar una plantilla", "close", null);
        remove.Items.Add(Caption("Se pide confirmación"));
        foreach (var profile in profiles)
        {
            var name = profile.Name;
            var item = Item(Hinted(name, Boxes(profile.Root)), null, () => DeleteProfile(name));
            item.Icon = CorkIcons.Make("close", 15, danger);
            item.Foreground = danger;
            remove.Items.Add(item);
        }
        remove.IsEnabled = profiles.Count > 0;
        menu.Items.Add(remove);
    }

    /// <summary>Cuántos recuadros tiene la forma de un perfil, dicho en corto.</summary>
    private static string Boxes(CorkNodeSaved? root)
    {
        static int Count(CorkNodeSaved? node) => node is null ? 0 : node.Children is { } children ? children.Sum(Count) : 1;
        return Count(root) switch
        {
            1 => "1 recuadro",
            var n => $"{n} recuadros",
        };
    }

    /// <summary>Una sesión abierta en un menú: el punto de su estado, su repositorio y, en letra de código, su carpeta.</summary>
    private Control SessionLabel(SessionState session)
    {
        var (name, place) = session.Repo is { } repo ? (repo, session.Project) : (session.Project ?? session.SessionId, null);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Dot(session, 7), new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center } } };
        if (place is not null)
        {
            var where = new TextBlock { Text = place, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
            where.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("MonoFont"));
            where.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgMuted"));
            row.Children.Add(where);
        }
        return row;
    }

    /// <summary>Guarda la composición del corcho como perfil, con el nombre del corcho; si ya había uno con ese nombre, lo
    /// sustituye.</summary>
    private void SaveProfile()
    {
        var state = CorkStore.LoadProfiles();
        state.Profiles.RemoveAll(p => string.Equals(p.Name, DisplayName, StringComparison.OrdinalIgnoreCase));
        state.Profiles.Add(new CorkProfile { Name = DisplayName, Root = ProfileNode(SaveNode(_root)) });
        CorkStore.SaveProfiles(state);
        ListChanged?.Invoke();
    }

    /// <summary>Borra un perfil, tras confirmarlo.</summary>
    private async void DeleteProfile(string name)
    {
        if (!await AppDialog.ConfirmAsync(this, "Borrar la plantilla", $"¿Seguro que quieres borrar la plantilla «{name}»?", "Borrar", DialogTone.Warning)) return;
        var state = CorkStore.LoadProfiles();
        state.Profiles.RemoveAll(p => p.Name == name);
        CorkStore.SaveProfiles(state);
        ListChanged?.Invoke();
    }

    /// <summary>Un trozo de la forma, como se guarda en un perfil: cada parte de una sesión, sin su sesión; las vistas de
    /// la app, tal cual; y lo que solo vale para una sesión concreta (la ficha de un fichero o de un paso), fuera.</summary>
    /// <remarks>Un recuadro que se queda sin nada vuelve como hueco, para que la forma sea la misma.</remarks>
    private static CorkNodeSaved? ProfileNode(CorkNodeSaved? node)
    {
        if (node is null) return null;
        if (node.Children is { } children)
            return new CorkNodeSaved { Orientation = node.Orientation, Children = children.Select(c => ProfileNode(c)!).ToList(), Weights = node.Weights };
        var panes = (node.Panes ?? [])
            .Where(p => NativeViews.Is(p.Kind) || (PieceSpec.IsPart(p.Kind) && p.Kind != "step"))
            .Select(p => NativeViews.Is(p.Kind) ? p : new PieceSaved { Kind = p.Kind })
            .ToList();
        return new CorkNodeSaved
        {
            Panes = panes, Selected = Math.Clamp(node.Selected, 0, Math.Max(0, panes.Count - 1)), Hole = panes.Count == 0 ? true : null,
        };
    }

    /// <summary>Un trozo de un perfil para montarlo: lleno con una sesión, cada parte de esa sesión (una repetida deja su
    /// sitio como hueco); sin sesión, cada recuadro de partes de sesión, un hueco que las espera.</summary>
    private static CorkNodeSaved? Filled(CorkNodeSaved? node, SessionState? session, HashSet<string> seen)
    {
        if (node is null) return null;
        if (node.Children is { } children)
            return new CorkNodeSaved { Orientation = node.Orientation, Children = children.Select(c => Filled(c, session, seen)!).ToList(), Weights = node.Weights };
        var panes = new List<PieceSaved>();
        var wanted = new List<string>();
        foreach (var pane in node.Panes ?? [])
        {
            if (!NativeViews.Is(pane.Kind) && session is null)
            {
                wanted.Add(pane.Kind);
                continue;
            }
            var piece = NativeViews.Is(pane.Kind) ? pane
                : new PieceSaved { Kind = pane.Kind, SessionId = session!.SessionId, Title = PartTitle(pane.Kind, session) };
            if (piece.ToSpec() is not { } spec || !seen.Add(spec.Key)) continue;
            panes.Add(piece);
        }
        return new CorkNodeSaved
        {
            Panes = panes, Selected = Math.Clamp(node.Selected, 0, Math.Max(0, panes.Count - 1)), Hole = panes.Count == 0 ? true : null,
            Want = panes.Count == 0 && wanted.Count > 0 ? wanted : null,
        };
    }

    /// <summary>Monta un perfil lleno con una sesión: en este corcho si está vacío y, si no, en uno nuevo, que toma el
    /// nombre del perfil. Lo que ya está abierto en otro sitio se trae de allí.</summary>
    internal static void OpenProfile(string name, string? sessionId, CorkWindow? from)
    {
        var saved = CorkStore.LoadProfiles().Profiles;
        var profile = saved.FirstOrDefault(p => p.Name == name) ?? Templates(saved).Select(t => t.Profile).FirstOrDefault(p => p.Name == name);
        if (profile is not null) OpenProfile(profile, sessionId, from);
    }

    /// <summary>Monta un perfil o una plantilla, lleno con una sesión si se da, como <see cref="OpenProfile(string, string?, CorkWindow?)"/>.</summary>
    private static void OpenProfile(CorkProfile profile, string? sessionId, CorkWindow? from)
    {
        var session = sessionId is null ? null : Pieces.Session(sessionId);
        var filled = Filled(profile.Root, session, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var cork = from is { _root: Stack { Panes.Count: 0 } } empty ? empty : Create();
        cork._name = profile.Name;
        cork.ShowName();
        cork._root = cork.Load(filled, null, claim: true) ?? new Stack();
        foreach (var pane in cork.Panes())
            if (!cork.Board.Children.Contains(pane)) cork.Board.Children.Add(pane);
        cork.Rebuild();
        cork.Bring();
    }

    /// <summary>Guarda los corchos al cerrar la app, antes de que se cierren sus ventanas, y deja de guardar.</summary>
    internal static void SaveForShutdown()
    {
        if (_drag is not null) CancelDrag();
        SaveTimer.Stop();
        SaveNow();
        _quitting = true;
    }

    /// <summary>Guarda los corchos dentro de un momento, salvo al cerrar la app.</summary>
    private static void ScheduleSave()
    {
        if (_quitting) return;
        SaveTimer.Stop();
        SaveTimer.Start();
    }

    /// <summary>Guarda ya los corchos abiertos, con el último que se usó al final.</summary>
    /// <remarks>A mitad de un arrastre la forma es provisional: se espera a que acabe.</remarks>
    private static void SaveNow()
    {
        if (_drag is not null)
        {
            ScheduleSave();
            return;
        }
        var state = new CorkState();
        foreach (var cork in Corks.OrderBy(c => c == _last ? 1 : 0)) state.Corks.Add(cork.Saved());
        CorkStore.Save(state);
    }

    /// <summary>El corcho tal como se guarda: su número, dónde está su ventana sin maximizar, con este juego de pantallas y
    /// con los demás, y sus recuadros.</summary>
    /// <remarks>La medida normal se toma aquí, al guardar, y no al moverse la ventana: al maximizar, el sistema avisa
    /// de que se ha movido antes de avisar de que está maximizada. Si las pantallas acaban de cambiar y aún no se ha
    /// recolocado, su sitio de ahora es el que dejó el sistema y no se apunta a ningún juego.</remarks>
    private CorkSaved Saved()
    {
        if (WindowState == WindowState.Normal || _normalSize == default) (_normalAt, _normalSize) = (Position, ClientSize);
        var maximized = WindowState == WindowState.Maximized;
        if (maximized) _normalAt = NormalOnItsScreen();
        if (_screenKey is { } key && key == ScreenKey())
            _places[key] = new CorkPlace { X = _normalAt.X, Y = _normalAt.Y, Width = _normalSize.Width, Height = _normalSize.Height, Maximized = maximized };
        return new CorkSaved
        {
            Number = _number, Name = _name, X = _normalAt.X, Y = _normalAt.Y, Width = _normalSize.Width, Height = _normalSize.Height,
            Maximized = maximized, Places = new Dictionary<string, CorkPlace>(_places), Root = SaveNode(_root),
        };
    }

    /// <summary>Un trozo de la forma, tal como se guarda.</summary>
    private static CorkNodeSaved? SaveNode(Node? node) => node switch
    {
        Split split => new CorkNodeSaved
        {
            Orientation = split.Orientation == Orientation.Vertical ? "vertical" : "horizontal",
            Children = split.Children.Select(c => SaveNode(c)!).ToList(), Weights = [.. split.Weights],
        },
        Stack stack => new CorkNodeSaved
        {
            Panes = stack.Panes.Select(p => PieceSaved.Of(p.Spec, p.WidgetState)).ToList(), Selected = stack.Selected, Hole = stack.Hole ? true : null,
            Want = stack.Hole ? stack.Want : null,
        },
        _ => null,
    };

    /// <summary>Un trozo de la forma guardada, con sus paneles ya creados; null si se queda sin ninguno.</summary>
    /// <param name="claim">Si una parte de una sesión ya está abierta en otro sitio, traerla de allí, y si es una vista de la
    /// app, abrir otra igual, como al montar un perfil; sin ello, se salta, como al volver a abrir la app.</param>
    private Node? Load(CorkNodeSaved? saved, Split? parent, bool claim = false)
    {
        if (saved is null) return null;
        if (saved.Children is { Count: > 0 } children)
        {
            var split = new Split { Orientation = saved.Orientation == "vertical" ? Orientation.Vertical : Orientation.Horizontal, Parent = parent };
            for (var i = 0; i < children.Count; i++)
            {
                if (Load(children[i], split, claim) is not { } child) continue;
                split.Children.Add(child);
                split.Weights.Add(saved.Weights?.ElementAtOrDefault(i) is > 0 and var weight ? weight : 1);
            }
            if (split.Children.Count == 0) return null;
            if (split.Children.Count > 1) return split;
            split.Children[0].Parent = parent;
            return split.Children[0];
        }
        var stack = new Stack { Parent = parent, Want = saved.Want is { Count: > 0 } want ? [.. want] : null };
        foreach (var piece in saved.Panes ?? [])
        {
            if (piece.ToSpec() is not { } spec || (!claim && PiecePane.Find(spec) is not null)) continue;
            // Una vista de la app abierta en otro corcho no se trae: se abre otra igual, para no desmontar aquel.
            if (claim && NativeViews.Is(spec.Kind) && PiecePane.Find(spec) is not null) spec = spec with { Path = Guid.NewGuid().ToString("N")[..8] };
            var fresh = PiecePane.Find(spec) is null;
            var pane = claim ? Pieces.Claim(spec) : Pieces.Make(spec);
            // Un widget nuevo vuelve con lo que se eligió en él; uno traído de otro sitio conserva lo suyo.
            if (fresh) pane.WidgetState = piece.State;
            stack.Panes.Add(pane);
        }
        // Un hueco guardado vuelve como hueco; un recuadro cuyos paneles ya no se saben abrir deja su sitio a los demás.
        if (stack.Panes.Count == 0) return saved.Hole == true ? stack : null;
        stack.Selected = Math.Clamp(saved.Selected, 0, stack.Panes.Count - 1);
        return stack;
    }

    /// <summary>Abre un corcho vacío nuevo, con el número libre más bajo: un poco más abajo y a la derecha del último que
    /// se usó, para que no lo tape, o centrado si no hay otro.</summary>
    private static CorkWindow Create()
    {
        var number = 1;
        while (Corks.Any(c => c._number == number)) number++;
        var cork = new CorkWindow(number);
        if ((_last ?? Corks.LastOrDefault()) is { WindowState: WindowState.Normal } previous)
        {
            var step = (int)(40 * previous.RenderScaling);
            cork.WindowStartupLocation = WindowStartupLocation.Manual;
            cork.Position = new PixelPoint(previous.Position.X + step, previous.Position.Y + step);
        }
        Corks.Add(cork);
        cork.Show();
        ListChanged?.Invoke();
        return cork;
    }

    /// <summary>Pone a la vista uno de sus paneles: elige su pestaña y trae el corcho al frente.</summary>
    void IPieceHost.Reveal(PiecePane pane)
    {
        if (StackOf(pane) is { } stack)
        {
            stack.Selected = stack.Panes.IndexOf(pane);
            Rebuild();
        }
        Bring();
    }

    /// <summary>Trae el corcho al frente.</summary>
    private void Bring()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // LA FORMA

    /// <summary>Añade un panel: en el hueco de un perfil que lo espera, si lo hay; si no, en el primer hueco libre o, si no
    /// hay ninguno, en un recuadro nuevo que parte el más grande, a lo largo de su lado más largo.</summary>
    /// <remarks>Los huecos de un perfil que esperan otra cosa se respetan.</remarks>
    private void Add(PiecePane pane)
    {
        var holes = Stacks().Where(s => s.Hole).ToList();
        if ((holes.FirstOrDefault(s => s.Want?.Contains(pane.Spec.Kind) == true) ?? holes.FirstOrDefault(s => s.Want is null)) is { } hole)
        {
            hole.Want = null;
            hole.Panes.Add(pane);
        }
        else
        {
            var stack = new Stack();
            stack.Panes.Add(pane);
            var target = Largest();
            var rect = _rects.GetValueOrDefault(target);
            SplitBeside(target, stack, rect.Width >= rect.Height ? Orientation.Horizontal : Orientation.Vertical);
        }
        if (!Board.Children.Contains(pane)) Board.Children.Add(pane);
        Rebuild();
    }

    /// <summary>Pone un trozo nuevo al lado de otro, repartiendo con él su sitio: en la misma división si ya va en ese
    /// sentido, o en una división nueva entre los dos.</summary>
    /// <param name="before">Si va delante (a la izquierda o encima); si no, detrás.</param>
    private void SplitBeside(Node target, Node added, Orientation orientation, bool before = false)
    {
        if (target.Parent is { } parent && parent.Orientation == orientation)
        {
            var at = parent.Children.IndexOf(target);
            var half = parent.Weights[at] / 2;
            parent.Weights[at] = half;
            parent.Children.Insert(before ? at : at + 1, added);
            parent.Weights.Insert(before ? at : at + 1, half);
            added.Parent = parent;
            return;
        }
        var split = new Split { Orientation = orientation };
        Replace(target, split);
        foreach (var child in before ? new[] { added, target } : new[] { target, added })
        {
            split.Children.Add(child);
            split.Weights.Add(1);
            child.Parent = split;
        }
    }

    /// <summary>Pone un trozo al lado de otro como <see cref="SplitBeside"/>, y devuelve cómo dejarlo todo como estaba,
    /// pesos incluidos: lo que hace el hueco de un arrastre, que va y viene.</summary>
    private Action SplitBesideUndoable(Node target, Node added, Orientation orientation, bool before)
    {
        if (target.Parent is { } parent && parent.Orientation == orientation)
        {
            var weight = parent.Weights[parent.Children.IndexOf(target)];
            SplitBeside(target, added, orientation, before);
            return () =>
            {
                var at = parent.Children.IndexOf(added);
                parent.Children.RemoveAt(at);
                parent.Weights.RemoveAt(at);
                parent.Weights[parent.Children.IndexOf(target)] = weight;
            };
        }
        SplitBeside(target, added, orientation, before);
        var split = target.Parent!;
        return () => Replace(split, target);
    }

    /// <summary>Pone un trozo junto a un borde del corcho, de lado a lado: una columna o una fila entera más, con su parte
    /// del sitio, y devuelve cómo dejarlo todo como estaba.</summary>
    /// <remarks>Si todo el corcho ya va en columnas (o en filas, para una fila), la nueva es una más entre ellas, con lo que
    /// mida la media; si no, ocupa un tercio junto a lo que había, o la mitad si había un solo recuadro.</remarks>
    private Action InsertAtEdge(Stack added, Orientation orientation, bool before)
    {
        if (_root is Split { } line && line.Orientation == orientation)
        {
            var at = before ? 0 : line.Children.Count;
            line.Children.Insert(at, added);
            line.Weights.Insert(at, line.Weights.Average());
            added.Parent = line;
            return () =>
            {
                var index = line.Children.IndexOf(added);
                line.Children.RemoveAt(index);
                line.Weights.RemoveAt(index);
            };
        }
        var old = _root!;
        var split = new Split { Orientation = orientation };
        var oldWeight = old is Stack ? 1 : 2;
        foreach (var (node, weight) in before ? new[] { ((Node)added, 1.0), (old, oldWeight) } : new[] { (old, oldWeight), ((Node)added, 1.0) })
        {
            split.Children.Add(node);
            split.Weights.Add(weight);
            node.Parent = split;
        }
        _root = split;
        return () =>
        {
            _root = old;
            old.Parent = null;
        };
    }

    /// <summary>Pone un trozo en el sitio de otro, dentro de la división de este o como raíz.</summary>
    private void Replace(Node old, Node with)
    {
        with.Parent = old.Parent;
        if (old.Parent is { } parent) parent.Children[parent.Children.IndexOf(old)] = with;
        else _root = with;
    }

    /// <summary>Quita un panel de su recuadro, y el recuadro si se queda vacío.</summary>
    /// <param name="offBoard">Si deja también la superficie, porque se va del corcho; si no, se queda en ella para
    /// ponerse en otro recuadro sin recargar su página.</param>
    private void Detach(PiecePane pane, bool offBoard)
    {
        if (StackOf(pane) is not { } stack) return;
        stack.Panes.Remove(pane);
        stack.Selected = Math.Clamp(stack.Selected, 0, Math.Max(0, stack.Panes.Count - 1));
        if (offBoard) Board.Children.Remove(pane);
        if (stack.Panes.Count == 0) RemoveNode(stack);
    }

    /// <summary>Quita un trozo del árbol; una división que se queda con un solo hijo deja su sitio a ese hijo, y el corcho
    /// que se queda sin nada vuelve a ser un hueco.</summary>
    private void RemoveNode(Node node)
    {
        if (node.Parent is not { } parent)
        {
            _root = new Stack();
            return;
        }
        var at = parent.Children.IndexOf(node);
        parent.Children.RemoveAt(at);
        parent.Weights.RemoveAt(at);
        if (parent.Children.Count == 1) Replace(parent, parent.Children[0]);
    }

    /// <summary>Los recuadros, en el orden en que se ven, sin el hueco de un arrastre.</summary>
    private IEnumerable<Stack> Stacks() => Walk(_root).OfType<Stack>().Where(s => !s.Preview);

    /// <summary>Los paneles del corcho.</summary>
    private IEnumerable<PiecePane> Panes() => Stacks().SelectMany(s => s.Panes);

    /// <summary>El árbol entero, de la raíz a las hojas.</summary>
    private static IEnumerable<Node> Walk(Node? node)
    {
        if (node is null) yield break;
        yield return node;
        if (node is not Split split) yield break;
        foreach (var child in split.Children)
        foreach (var inner in Walk(child))
            yield return inner;
    }

    /// <summary>El recuadro donde está un panel; null si no está en este corcho.</summary>
    private Stack? StackOf(PiecePane pane) => Stacks().FirstOrDefault(s => s.Panes.Contains(pane));

    /// <summary>El recuadro más grande, que es el que se parte para uno nuevo.</summary>
    private Stack Largest() =>
        Stacks().OrderByDescending(s => _rects.TryGetValue(s, out var r) ? r.Width * r.Height : 0).First();

    // EL NOMBRE

    /// <summary>Pone el nombre en la barra y en el título de la ventana, que es el que sale en la barra de tareas.</summary>
    private void ShowName()
    {
        NameText.Text = DisplayName;
        Title = $"{DisplayName} · Arkko";
    }

    /// <summary>Cambia el nombre en la propia barra: el texto pasa a ser un campo con el nombre elegido, listo para escribir.</summary>
    private void Rename()
    {
        NameBox.Text = DisplayName;
        NameText.IsVisible = false;
        NameBox.IsVisible = true;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    /// <summary>Acaba de cambiar el nombre: lo guarda si se ha pedido o lo deja como estaba; vacío, vuelve a «Corcho N».</summary>
    private void EndRename(bool keep)
    {
        if (!NameBox.IsVisible) return;
        NameBox.IsVisible = false;
        NameText.IsVisible = true;
        if (!keep) return;
        var name = NameBox.Text?.Trim();
        _name = string.IsNullOrEmpty(name) || name == $"Corcho {_number}" ? null : name;
        ShowName();
        ScheduleSave();
        ListChanged?.Invoke();
    }

    // LA BARRA Y EL ESTADO DE LAS SESIONES

    /// <summary>El color del estado de una sesión, el mismo que en toda la app; el de reposo si ya se cerró.</summary>
    private static IBrush StateBrush(SessionState? session) => session?.Status switch
    {
        SessionStatus.Working => Brushes_.Working,
        SessionStatus.Waiting => Brushes_.Waiting,
        SessionStatus.Done => Brushes_.Done,
        SessionStatus.Agents => Brushes_.Agents,
        SessionStatus.Command => Brushes_.Command,
        _ => Brushes_.Idle,
    };

    /// <summary>Cómo se dice el estado de una sesión.</summary>
    private static string StateText(SessionState? session) => session?.Status switch
    {
        null => "Cerrada",
        SessionStatus.Working => "Trabajando",
        SessionStatus.Waiting => "Te espera",
        SessionStatus.Done => "Ha terminado",
        SessionStatus.Agents => "Espera a " + MainViewModel.AgentsWord(session.Background?.RunningAgents ?? 0),
        SessionStatus.Command => "Espera a " + MainViewModel.CommandsWord(session.Background?.AwaitedCommands ?? 0),
        _ => "En reposo",
    };

    /// <summary>El punto del estado de una sesión; con un halo si está trabajando, en órbita si espera a sus agentes y en un
    /// recuadro de consola si espera a un comando, como en el resto de la app.</summary>
    private static Control Dot(SessionState? session, double size)
    {
        var brush = StateBrush(session);
        if (session?.Status == SessionStatus.Agents)
            return new OrbitDot { Width = size + 4, Height = size + 4, Brush = brush, VerticalAlignment = VerticalAlignment.Center };
        if (session?.Status == SessionStatus.Command)
            return new CommandDot { Width = size + 7, Height = size + 4, Brush = brush, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Ellipse { Width = size, Height = size, Fill = brush, VerticalAlignment = VerticalAlignment.Center };
        if (session?.Status != SessionStatus.Working || brush is not ISolidColorBrush solid) return dot;
        var halo = new Ellipse { Width = size + 6, Height = size + 6, Fill = new SolidColorBrush(solid.Color, 0.25) };
        return new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { halo, dot } };
    }

    /// <summary>La marca de un panel delante de su nombre: el punto del estado de su sesión; en una vista de la app, el punto
    /// del color de lo más urgente de su widget, o un aro vacío si no pide nada.</summary>
    private static Control Mark(PiecePane pane, double size)
    {
        if (!NativeViews.Is(pane.Spec.Kind)) return Dot(Pieces.Session(pane.Spec.SessionId), size);
        if (pane.Widget?.Mood is { } mood) return new Ellipse { Width = size, Height = size, Fill = mood, VerticalAlignment = VerticalAlignment.Center };
        return new Ellipse { Width = size, Height = size, StrokeThickness = 1.5, Stroke = Brushes_.Idle, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Cómo está la cabecera del widget de un panel, para saber si hay que repintarla: su color, su dato, si tiene
    /// novedades y cuándo leyó; vacío si no es una vista de la app.</summary>
    private static string HeadOf(PiecePane pane) => pane.Widget is { } widget
        ? $"{(widget.Mood as ISolidColorBrush)?.Color}:{widget.Headline}:{widget.HasNews}:{Refreshed(widget)}"
        : "";

    /// <summary>Cuándo leyó un widget sus datos, «hace 3 min»; null si aún no ha leído nada.</summary>
    private static string? Refreshed(CorkWidget widget) => widget.RefreshedUtc is { } at ? TasksModel.Ago(at) : null;

    /// <summary>Cómo se llama una parte en la barra y en las fichas: Flujo, Chat, Cambios, una vista de Sesiones, Paso,
    /// una vista de la app o el nombre del fichero.</summary>
    private static string PartName(PieceSpec spec) => spec.Kind switch
    {
        "step" => "Paso",
        _ when PieceSpec.IsPart(spec.Kind) => KindName(spec.Kind),
        _ when NativeViews.Is(spec.Kind) => NativeViews.NameOf(spec.Kind),
        _ => System.IO.Path.GetFileName(spec.Path ?? spec.Title),
    };

    /// <summary>El nombre de una sesión en dos partes, el repositorio y la carpeta (sin repositorio, solo la carpeta); de
    /// una que ya se cerró, lo que dice el título de su panel.</summary>
    private static (string Name, string? Place) Naming(SessionState? session, PiecePane pane)
    {
        if (session is { Project: { } project }) return session.Repo is { } repo ? (repo, project) : (project, null);
        var title = pane.Spec.Title;
        var cut = title.IndexOf(" · ", StringComparison.Ordinal);
        return (cut >= 0 ? title[(cut + 3)..] : title, null);
    }

    /// <summary>Pone al día la barra y el punto de cada cabecera con el estado de las sesiones clavadas; sin
    /// <paramref name="force"/>, solo si ha cambiado algo.</summary>
    private void ShowStatus(bool force = false)
    {
        // Las vistas de la app no son de ninguna sesión: no llevan pastilla.
        var groups = Panes().Where(p => !NativeViews.Is(p.Spec.Kind)).GroupBy(p => p.Spec.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Panes: g.ToList(), Session: Pieces.Session(g.Key))).ToList();
        var key = string.Join("|", groups.Select(g => $"{g.Panes[0].Spec.SessionId}:{g.Session?.Status}:{g.Panes.Count}:{g.Session?.Title}"))
                  + "#" + string.Join("|", Panes().Where(p => NativeViews.Is(p.Spec.Kind)).Select(p => $"{p.Spec.Key}:{HeadOf(p)}"));
        if (!force && key == _status) return;
        _status = key;
        Chips.Children.Clear();
        foreach (var (panes, session) in groups) Chips.Children.Add(Chip(session, panes));
        BarSplit.IsVisible = groups.Count > 0;
        foreach (var (owner, control) in _chrome)
            if (owner is Stack stack && control is Border head) Fill(head, stack);
    }

    // Las teclas de la barra, con los colores fijos de la tecla del logo, como el hueco.
    private static readonly IBrush KeyText = new SolidColorBrush(Color.Parse("#E6E3DC"));
    private static readonly IBrush KeyMuted = new SolidColorBrush(Color.Parse("#9A968F"));
    private static readonly IBrush KeyWell = new SolidColorBrush(Color.Parse("#232327"));

    /// <summary>La tecla de una sesión en la barra: un cuadrado de su color con sus iniciales y el punto de su estado en la
    /// esquina; al lado, su nombre y, debajo, su carpeta y su estado; y cuántos paneles tiene en este corcho. Con su ficha.</summary>
    private Control Chip(SessionState? session, List<PiecePane> panes)
    {
        var (name, place) = Naming(session, panes[0]);
        var color = RepoColors.Of(name);
        var initials = new TextBlock
        {
            Text = Initials(name), FontSize = 10.5, FontWeight = FontWeight.ExtraBold, Foreground = InkOn(color),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        // El punto, con un aro del color de la tecla para que se despegue del cuadrado.
        var state = new Border
        {
            Width = 11, Height = 11, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), BorderBrush = KeyFace,
            Background = StateBrush(session), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -3, -3),
        };
        var avatar = new Grid
        {
            Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { CornerRadius = new CornerRadius(7), Background = color }, initials, state },
        };
        var title = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = KeyText, LineHeight = 15 };
        var line = new TextBlock
        {
            Text = string.Join(" · ", new[] { place, StateText(session).ToLowerInvariant() }.Where(s => s is not null)),
            FontSize = 10, Foreground = KeyMuted, LineHeight = 13,
        };
        line.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("MonoFont"));
        var count = new TextBlock
        {
            Text = panes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 10.5, FontWeight = FontWeight.SemiBold,
            Foreground = KeyMuted, VerticalAlignment = VerticalAlignment.Center,
        };
        count.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("MonoFont"));
        var badge = new Border { Child = count, Padding = new Thickness(6, 1), CornerRadius = new CornerRadius(6), Background = KeyWell, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center,
            Children = { avatar, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, line } }, badge },
        };
        var chip = new Border
        {
            Child = row, Height = 38, Padding = new Thickness(5, 0, 10, 3), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
            Background = KeyFace, BorderBrush = KeyEdge, BoxShadow = BoxShadows.Parse("inset 0 -3 0 0 #1B1B1E"),
        };
        DiaryInk.Tip(chip, DiaryInk.Card(session?.Title ?? name, StateText(session), StateBrush(session),
            rows: [("En este corcho", string.Join(", ", panes.Select(p => PartName(p.Spec))))]));
        return chip;
    }

    /// <summary>Las iniciales de un repositorio para su tecla: la primera letra de sus dos primeras palabras, o las dos
    /// primeras letras si es una sola.</summary>
    private static string Initials(string name)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(name, @"\p{Lu}?\p{Ll}+|\p{Lu}+(?!\p{Ll})|\d+")
            .Select(m => m.Value).ToList();
        if (words.Count >= 2) return $"{words[0][0]}{words[1][0]}".ToUpperInvariant();
        var only = words.Count == 1 ? words[0] : name;
        return (only.Length >= 2 ? only[..2] : only).ToUpperInvariant();
    }

    /// <summary>El color del texto sobre un fondo: oscuro sobre uno claro, blanco sobre uno oscuro.</summary>
    private static IBrush InkOn(IBrush background)
    {
        if (background is not ISolidColorBrush { Color: var c }) return KeyText;
        var light = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 > 0.55;
        return light ? new SolidColorBrush(Color.Parse("#141416")) : Brushes.White;
    }

    // LO QUE SE VE

    /// <summary>Lo vuelve a colocar todo, animado, después de cambiar la forma o la pestaña elegida: la barra y cada
    /// cabecera se ponen al día.</summary>
    private void Rebuild()
    {
        ShowStatus(force: true);
        Relayout(animate: true);
        ScheduleSave();
        ListChanged?.Invoke();
    }

    /// <summary>Lo recoloca todo en el próximo fotograma, una sola vez aunque se pida muchas veces antes.</summary>
    /// <remarks>Mover un separador avisa con cada movimiento del ratón, más a menudo de lo que se pinta: cambiar de tamaño
    /// las páginas con cada aviso es lo que daba tirones.</remarks>
    private void RelayoutSoon()
    {
        if (_relayoutAsked) return;
        _relayoutAsked = true;
        RequestAnimationFrame(_ =>
        {
            _relayoutAsked = false;
            Relayout();
        });
    }

    /// <summary>Calcula dónde va cada recuadro y coloca en su sitio sus cabeceras, sus paneles y los separadores; animado,
    /// cada uno va de donde se ve a donde le toca.</summary>
    /// <remarks>Los paneles de las pestañas de detrás no se ven y no siguen su sesión; tampoco los de un corcho minimizado.
    /// Lo que ya no acompaña a nada sale de la superficie, salvo a mitad de un arrastre: la cabecera que se agarró tiene
    /// que seguir en ella hasta soltar, aunque su recuadro ya no esté.</remarks>
    private void Relayout(bool animate = false)
    {
        _rects.Clear();
        var places = new Dictionary<Control, Rect>();
        var used = new HashSet<object>();
        var size = Board.Bounds.Size;
        if (_root is not null && size.Width > 0 && size.Height > 0) Lay(_root, new Rect(size), places, used);
        if (_drag is null)
            foreach (var stale in _chrome.Keys.Where(k => !used.Contains(k)).ToList())
            {
                Board.Children.Remove(_chrome[stale]);
                _chrome.Remove(stale);
            }
        foreach (var control in _chrome.Values) control.IsVisible = places.ContainsKey(control);
        var hidden = WindowState == WindowState.Minimized;
        foreach (var stack in Stacks())
            for (var i = 0; i < stack.Panes.Count; i++)
            {
                var shown = i == stack.Selected;
                stack.Panes[i].IsVisible = shown;
                stack.Panes[i].Paused = hidden || !shown;
            }
        Board.Place(places, animate);
    }

    /// <summary>Coloca un trozo del árbol en su rectángulo: un recuadro, con su cabecera arriba; el hueco de un arrastre,
    /// con su marca; una división, repartida entre sus hijos con un separador entre cada dos.</summary>
    /// <param name="used">Apunta lo que acompaña a lo colocado, para quitar después lo demás.</param>
    private void Lay(Node node, Rect rect, Dictionary<Control, Rect> places, HashSet<object> used)
    {
        _rects[node] = rect;
        if (node is Stack { Preview: true } hole)
        {
            var key = (hole, "hueco");
            used.Add(key);
            places[Chrome(key, () => Hole(hole))] = rect;
            return;
        }
        if (node is Stack { Hole: true } empty)
        {
            var key = (empty, "vacío");
            used.Add(key);
            places[Chrome(key, () => HoleCard(empty))] = rect;
            return;
        }
        if (node is Stack stack)
        {
            used.Add(stack);
            var head = Chrome(stack, () => Header(stack));
            places[head] = new Rect(rect.X, rect.Y, rect.Width, HeadHeight);
            if (stack.Panes.Count > 0)
                places[stack.Panes[stack.Selected]] = new Rect(rect.X, rect.Y + HeadHeight, rect.Width, Math.Max(0, rect.Height - HeadHeight));
            return;
        }
        var split = (Split)node;
        var across = split.Orientation == Orientation.Horizontal;
        var length = (across ? rect.Width : rect.Height) - Gap * (split.Children.Count - 1);
        var total = split.Weights.Sum();
        var at = across ? rect.X : rect.Y;
        for (var i = 0; i < split.Children.Count; i++)
        {
            var part = Math.Max(0, length * split.Weights[i] / total);
            Lay(split.Children[i], across ? new Rect(at, rect.Y, part, rect.Height) : new Rect(rect.X, at, rect.Width, part), places, used);
            at += part;
            if (i == split.Children.Count - 1) break;
            var index = i;
            used.Add((split, index));
            var gap = Chrome((split, index), () => Splitter(split, index));
            places[gap] = across ? new Rect(at, rect.Y, Gap, rect.Height) : new Rect(rect.X, at, rect.Width, Gap);
            at += Gap;
        }
    }

    /// <summary>La cabecera, el separador o la marca de algo, si ya está en la superficie; si no, lo crea y lo pone en ella.</summary>
    private Control Chrome(object owner, Func<Control> make)
    {
        if (_chrome.TryGetValue(owner, out var found)) return found;
        var made = make();
        _chrome[owner] = made;
        Board.Children.Add(made);
        return made;
    }

    /// <summary>La marca del hueco que abre un arrastre: dónde caerá el panel si se suelta ahí, y si es una columna o una
    /// fila entera.</summary>
    private Control Hole(Stack hole)
    {
        var label = new TextBlock { Text = hole.Label ?? "Soltar aquí", FontSize = 12.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Accent"));
        var accent = (this.FindResource("Accent") as ISolidColorBrush)?.Color ?? Colors.Orange;
        var mark = new Border
        {
            Child = label, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(accent, 0.14), BorderBrush = new SolidColorBrush(accent, 0.85),
        };
        return mark;
    }

    // LOS HUECOS

    // Los colores de la tecla del hueco: los de la tecla del logo, fijos, como el logo, en cualquier tema.
    private static readonly IBrush KeyBody = new SolidColorBrush(Color.Parse("#1B1B1E"));
    private static readonly IBrush KeyEdge = new SolidColorBrush(Color.Parse("#34343A"));
    private static readonly IBrush KeyFace = new SolidColorBrush(Color.Parse("#2C2C31"));
    private static readonly IBrush KeyFaceHover = new SolidColorBrush(Color.Parse("#34343A"));
    private static readonly IBrush KeyLegend = new SolidColorBrush(Color.Parse("#D9D5CD"));
    private static readonly IBrush LedOff = new SolidColorBrush(Color.Parse("#45454C"));
    private static readonly IBrush LedOn = new SolidColorBrush(Color.Parse("#3FB950"));

    /// <summary>Un hueco de la composición: un borde discontinuo y, en medio, una tecla en blanco con un «+» y su piloto,
    /// que al pulsarla deja elegir qué clavar; arriba a la derecha, su menú.</summary>
    private Control HoleCard(Stack stack)
    {
        var edge = new Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = 1.2, StrokeDashArray = [4, 3], Fill = new SolidColorBrush(Colors.Black, 0.18) };
        edge.Bind(Shape.StrokeProperty, this.GetResourceObservable("TrackEdge"));

        var face = new Border { Margin = new Thickness(7, 4, 7, 10), CornerRadius = new CornerRadius(14), Background = KeyFace };
        var led = new Border
        {
            Width = 10, Height = 4, CornerRadius = new CornerRadius(2), Background = LedOff,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 15, 0),
        };
        var key = new Grid
        {
            Width = 84, Height = 84,
            Children =
            {
                new Border { CornerRadius = new CornerRadius(19), Background = KeyBody, BorderBrush = KeyEdge, BorderThickness = new Thickness(1.5) },
                face, led,
                new Border { Width = 22, Height = 4, CornerRadius = new CornerRadius(2), Background = KeyLegend, Margin = new Thickness(0, 0, 0, 6) },
                new Border { Width = 4, Height = 22, CornerRadius = new CornerRadius(2), Background = KeyLegend, Margin = new Thickness(0, 0, 0, 6) },
            },
        };
        var press = new Button { Theme = ThemeOf("PlainButton"), Content = key, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(press, "Clavar aquí");
        DiaryInk.Tip(press, DiaryInk.Card("Clavar aquí", "Elige una sesión abierta y qué parte de ella. También se puede soltar encima un panel arrastrado."));
        press.PointerEntered += (_, _) => face.Background = KeyFaceHover;
        press.PointerExited += (_, _) => face.Background = KeyFace;
        // El hueco de un perfil ya sabe qué va: solo pide de qué sesión.
        var wanted = stack.Want is { Count: > 0 } want ? string.Join(" · ", want.Select(KindName)) : null;
        press.Click += (_, _) =>
        {
            if (stack.Want is { Count: > 0 }) ChooseSessionFor(stack, press);
            else ChooseFor(stack, press);
        };

        var title = new TextBlock { Text = wanted ?? "Clava algo aquí", FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Fg"));
        var hint = new TextBlock
        {
            Text = wanted is null ? "Pulsa la tecla para elegir, o suelta un panel encima" : "Pulsa la tecla para elegir de qué sesión",
            FontSize = 11.5, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 260,
        };
        hint.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgFaint"));
        var middle = new StackPanel
        {
            Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { press, new StackPanel { Spacing = 3, Children = { title, hint } } },
        };

        var menu = new Button { Theme = ThemeOf("BarButton"), Content = "" };
        AutomationProperties.SetName(menu, "Opciones del hueco");
        DiaryInk.Tip(menu, DiaryInk.Card("Opciones del hueco", "Partirlo en más huecos, o quitarlo para que los demás ocupen su sitio."));
        menu.Click += (_, _) => HoleMenu(stack, menu);
        var remove = new Button { Theme = ThemeOf("BarButton"), Content = "" };
        AutomationProperties.SetName(remove, "Quitar el hueco");
        DiaryInk.Tip(remove, DiaryInk.Card("Quitar el hueco", "Los demás recuadros ocupan su sitio."));
        remove.Click += (_, _) =>
        {
            RemoveNode(stack);
            Rebuild();
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 6, 0), Children = { menu, remove },
        };

        _holeParts[stack] = (led, edge);
        return new Grid { ClipToBounds = true, Children = { edge, middle, buttons } };
    }

    /// <summary>Enciende o apaga el piloto de un hueco, y su borde con él: encendido, ahí cae el panel que se arrastra.</summary>
    private void Light(Stack stack, bool on)
    {
        if (!_holeParts.TryGetValue(stack, out var parts)) return;
        parts.Led.Background = on ? LedOn : LedOff;
        if (on) parts.Edge.Stroke = LedOn;
        else parts.Edge.Bind(Shape.StrokeProperty, this.GetResourceObservable("TrackEdge"));
    }

    /// <summary>Abre, junto a la tecla de un hueco, la lista de las sesiones abiertas con sus partes, para clavar ahí la
    /// que se elija.</summary>
    private void ChooseFor(Stack stack, Control anchor)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };
        menu.Items.Add(Caption("Clavar aquí"));
        var sessions = Pieces.Sessions();
        foreach (var session in sessions)
        {
            var name = session.SessionName ?? session.Title ?? session.SessionId;
            var entry = Item(Labelled(Dot(session, 7), session.Title ?? name), null, null);
            foreach (var (kind, icon) in new[] { ("live", "grid"), ("chat", "tabs"), ("changes", "split-right"), ("review", "tabs"), ("history", "tabs") })
            {
                var spec = new PieceSpec(kind, session.SessionId, $"{KindName(kind)} · {name}");
                entry.Items.Add(Item(KindName(kind), icon, () => PlaceInHole(stack, spec)));
            }
            menu.Items.Add(entry);
        }
        if (sessions.Count == 0) menu.Items.Add(Item("No hay ninguna sesión abierta", null, null, enabled: false));
        menu.Items.Add(new Separator());
        menu.Items.Add(Caption("Vistas"));
        foreach (var kind in ViewKinds)
            menu.Items.Add(Item(NativeViews.NameOf(kind), null, () => PlaceInHole(stack, NativeViews.Spec(kind))));
        menu.Open(anchor);
    }

    /// <summary>Abre, junto a la tecla del hueco de un perfil, las sesiones abiertas, para llenarlo con la elegida; y, si
    /// hay más huecos así en el corcho, la misma lista para llenarlos todos con una.</summary>
    private void ChooseSessionFor(Stack stack, Control anchor)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };
        menu.Items.Add(Caption($"{string.Join(" · ", stack.Want!.Select(KindName))} de la sesión"));
        var sessions = Pieces.Sessions();
        foreach (var session in sessions)
            menu.Items.Add(Item(SessionLabel(session), null, () => FillWanted([stack], session)));
        if (sessions.Count == 0)
        {
            menu.Items.Add(Item("No hay ninguna sesión abierta", null, null, enabled: false));
        }
        else if (Stacks().Count(s => s.Hole && s.Want is { Count: > 0 }) > 1)
        {
            menu.Items.Add(new Separator());
            var all = Item("En todo el corcho", "grid", null);
            foreach (var session in sessions)
                all.Items.Add(Item(SessionLabel(session), null, () => FillWanted(Stacks().Where(s => s.Hole && s.Want is { Count: > 0 }).ToList(), session)));
            menu.Items.Add(all);
        }
        menu.Open(anchor);
    }

    /// <summary>Llena huecos de un perfil con las partes que esperan, de una sesión; lo que ya esté abierto en otro sitio
    /// se trae de allí.</summary>
    private void FillWanted(List<Stack> stacks, SessionState session)
    {
        foreach (var stack in stacks)
        {
            foreach (var kind in stack.Want ?? [])
            {
                var spec = new PieceSpec(kind, session.SessionId, PartTitle(kind, session));
                if (stack.Panes.Any(p => p.Spec.Key == spec.Key)) continue;
                var pane = Fetch(spec);
                if (!Board.Children.Contains(pane)) Board.Children.Add(pane);
                stack.Panes.Add(pane);
            }
            stack.Want = null;
            stack.Selected = 0;
        }
        Rebuild();
    }

    /// <summary>El nombre de una parte de una sesión, como lo pone la página al clavarla: «Flujo · nombre de la sesión».</summary>
    private static string PartTitle(string kind, SessionState session) =>
        $"{KindName(kind)} · {session.SessionName ?? session.Title ?? session.SessionId}";

    // Las vistas de la app que se clavan, en el orden del menú lateral del panel.
    private static readonly string[] ViewKinds = ["home", "clocks", "board", "worktrees", "diary", "artifacts", "stats", "browser", "tetoca", "pulso", "todas", "avisos", "carril"];

    /// <summary>El menú de clavar una vista de la app, desde su elemento del menú lateral del panel: en cada corcho, por su
    /// nombre y con lo que tiene, en uno nuevo o en una ventana suelta.</summary>
    internal static ContextMenu PinMenu(PieceSpec spec)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Caption(spec.Title));
        menu.Items.Add(Caption("Clavar en el corcho"));
        foreach (var cork in Corks.OrderBy(c => c._number))
        {
            var number = cork._number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            menu.Items.Add(Item(Hinted(cork.DisplayName, cork.Contents()), "pin", () => Pieces.Open(spec, number)));
        }
        menu.Items.Add(Item("Un corcho nuevo", "plus", () => Pieces.Open(spec, "new")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Abrir en una ventana suelta", "window", () => Pieces.Open(spec, "window")));
        return menu;
    }

    /// <summary>Cómo se llama en el corcho cada parte de una sesión.</summary>
    private static string KindName(string kind) => kind switch
    {
        "live" => "Flujo",
        "chat" => "Chat",
        "changes" => "Cambios",
        "review" => "Notas de la revisión",
        "history" => "Historia del fichero",
        "diagram" => "Diagrama",
        "now" => "Ahora",
        "parte" => "Parte",
        "obra" => "Obra",
        _ => kind,
    };

    /// <summary>Clava una parte en un hueco: la crea o, si ya está abierta en otro sitio (un recuadro, otro corcho o una
    /// ventana suelta), la trae de allí.</summary>
    private void PlaceInHole(Stack stack, PieceSpec spec)
    {
        if (!stack.Hole) return;
        var pane = Fetch(spec);
        if (!stack.Hole) return;
        if (!Board.Children.Contains(pane)) Board.Children.Add(pane);
        stack.Panes.Add(pane);
        stack.Want = null;
        stack.Selected = 0;
        Rebuild();
    }

    /// <summary>El panel con eso, para ponerlo en este corcho: el que ya está abierto, sacado de donde esté (de este mismo
    /// corcho, sin quitarlo de la superficie), o uno nuevo.</summary>
    private PiecePane Fetch(PieceSpec spec)
    {
        var pane = PiecePane.Find(spec);
        switch (pane is null ? null : TopLevel.GetTopLevel(pane))
        {
            case CorkWindow other:
                other.Detach(pane!, offBoard: other != this);
                if (other != this) other.Rebuild();
                break;
            case PieceWindow loose:
                loose.Release();
                break;
        }
        return pane ?? Pieces.Make(spec);
    }

    /// <summary>El menú de un hueco: partirlo en más huecos o quitarlo, para que los demás ocupen su sitio.</summary>
    private void HoleMenu(Stack stack, Control anchor)
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(Caption("Hueco"));
        menu.Items.Add(Item("Partir a la derecha", "split-right", () => SplitHole(stack, Orientation.Horizontal)));
        menu.Items.Add(Item("Partir abajo", "split-down", () => SplitHole(stack, Orientation.Vertical)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quitar el hueco", "close", () =>
        {
            RemoveNode(stack);
            Rebuild();
        }, enabled: stack.Parent is not null));
        menu.Open(anchor);
    }

    /// <summary>Deja un hueco al lado de un recuadro, repartiendo con él su sitio, para llenarlo después.</summary>
    private void SplitHole(Stack stack, Orientation orientation)
    {
        SplitBeside(stack, new Stack(), orientation);
        Rebuild();
    }

    /// <summary>Cierra los paneles de un recuadro y deja su sitio como hueco, sin mover a los demás.</summary>
    private void EmptyStack(Stack stack)
    {
        foreach (var pane in stack.Panes.ToList())
        {
            Board.Children.Remove(pane);
            pane.Close();
        }
        stack.Panes.Clear();
        stack.Selected = 0;
        Rebuild();
    }

    /// <summary>La cabecera de un recuadro, que se puede arrastrar: el fondo de cristal y, encima, el marco con el tinte;
    /// lo que lleva dentro lo pone <see cref="Fill"/>.</summary>
    /// <remarks>El tinte va en el fondo del propio marco, con sus esquinas: puesto en un hijo cuadrado, tapaba el borde en
    /// las esquinas redondeadas.</remarks>
    private Control Header(Stack stack)
    {
        var head = new Border { BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(12, 12, 0, 0) };
        var frame = new Border { Child = head, CornerRadius = new CornerRadius(12, 12, 0, 0) };
        frame.Bind(Border.BackgroundProperty, this.GetResourceObservable("LensFill"));
        Fill(frame, stack);
        Draggable(frame, stack);
        return frame;
    }

    /// <summary>El repositorio de un panel, como se nombra su color; null en una vista de la app, que no es de ninguna sesión.</summary>
    private static string? RepoOf(PiecePane pane) =>
        NativeViews.Is(pane.Spec.Kind) ? null : Naming(Pieces.Session(pane.Spec.SessionId), pane).Name;

    /// <summary>El color del marco de un panel en el corcho, el de su repositorio, en «#rrggbb» para su página; null en
    /// una vista de la app, que va con el borde de siempre.</summary>
    internal static string? EdgeHex(PiecePane pane) =>
        RepoOf(pane) is { } repo && RepoColors.Of(repo) is ISolidColorBrush { Color: var c } ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : null;

    /// <summary>El color del marco de un panel, o el borde de cristal de siempre si no es de ninguna sesión.</summary>
    private IBrush EdgeBrush(PiecePane? pane) =>
        pane is not null && RepoOf(pane) is { } repo ? RepoColors.Of(repo) : this.FindResource("LensEdge") as IBrush ?? Brushes.Gray;

    /// <summary>Pone en la cabecera de un recuadro el nombre de su panel o sus pestañas, su menú y el cierre del panel que
    /// se ve, tal como está ahora el recuadro; enmarcada y teñida del color de la sesión del panel que se ve.</summary>
    /// <remarks>La página del panel sigue el marco por los lados y por abajo con el mismo color (se lo dice
    /// <see cref="PiecePane"/>): el recuadro entero queda enmarcado.</remarks>
    private void Fill(Border frame, Stack stack)
    {
        var head = (Border)frame.Child!;
        var shown = stack.Panes.Count == 0 ? null : stack.Panes[Math.Clamp(stack.Selected, 0, stack.Panes.Count - 1)];
        var edge = EdgeBrush(shown);
        head.BorderBrush = edge;
        head.Background = shown is not null && RepoOf(shown) is not null && edge is ISolidColorBrush tint ? new SolidColorBrush(tint.Color, 0.14) : null;

        var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(6, 0, 6, 0) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var menu = new Button { Theme = ThemeOf("HeadButton"), Content = "" };
        DiaryInk.Tip(menu, DiaryInk.Card("Opciones del panel", "Partirlo, juntarlo con otro, llevarlo a otro corcho, sacarlo a una ventana suelta o cerrarlo."));
        AutomationProperties.SetName(menu, "Opciones del panel");
        menu.Click += (_, _) => Menu(stack, menu);
        var close = new Button { Theme = ThemeOf("HeadCloseButton"), Content = "" };
        DiaryInk.Tip(close, DiaryInk.Card("Quitar este panel", stack.Panes.Count > 1 ? "Las otras pestañas del recuadro se quedan."
            : "El recuadro se queda vacío, como un hueco; su cruz lo quita."));
        AutomationProperties.SetName(close, "Quitar este panel");
        close.Click += (_, _) => EmptyPane(stack.Panes[stack.Selected]);
        tools.Children.Add(menu);
        tools.Children.Add(close);
        DockPanel.SetDock(tools, Dock.Right);
        bar.Children.Add(tools);

        // Un panel solo: su nombre y, en letra de código, de qué repositorio y carpeta es.
        if (stack.Panes.Count == 1)
        {
            var pane = stack.Panes[0];
            // En un recuadro estrecho se recorta con puntos suspensivos, sin meterse bajo los botones.
            var named = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), ClipToBounds = true };
            var mark = Mark(pane, 7);
            mark.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(mark, Dock.Left);
            named.Children.Add(mark);
            var title = new TextBlock
            {
                Text = PartName(pane.Spec), FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0),
            };
            title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Fg"));
            DockPanel.SetDock(title, Dock.Left);
            named.Children.Add(title);
            // Una vista de la app lleva, a la derecha, cuándo leyó sus datos: así se ve si se ha quedado parada.
            if (pane.Widget is { } widget && Refreshed(widget) is { } ago)
            {
                var when = new TextBlock { Text = ago, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                when.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgFaint"));
                DiaryInk.Tip(when, DiaryInk.Card("Datos leídos", $"El widget leyó sus datos por última vez {ago}."));
                DockPanel.SetDock(when, Dock.Right);
                named.Children.Add(when);
            }
            if (PlaceOf(pane) is { } place)
            {
                var where = new TextBlock { Text = place, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                where.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("MonoFont"));
                where.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgMuted"));
                named.Children.Add(where);
            }
            else if (pane.Widget?.Headline is { } headline)
            {
                var note = new TextBlock { Text = "· " + headline, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                note.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgMuted"));
                named.Children.Add(note);
            }
            bar.Children.Add(named);
            head.Child = bar;
            return;
        }

        // Pestañas de carpeta: la elegida lleva el fondo de la página y se abre hacia ella; ninguna lleva borde, que el
        // marco va solo por el contorno del recuadro. Si el recuadro mezcla repositorios, cada una lleva arriba una franja
        // con el color del suyo: el marco solo dice el de la que se ve.
        var mixed = stack.Panes.Select(RepoOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, ClipToBounds = true };
        for (var i = 0; i < stack.Panes.Count; i++)
        {
            var pane = stack.Panes[i];
            var on = i == stack.Selected;
            var title = new TextBlock
            {
                Text = TabName(pane), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(on ? "Fg" : "FgMuted"));
            var dot = Mark(pane, 7);
            dot.Margin = new Thickness(0, 0, 7, 0);
            DockPanel.SetDock(dot, Dock.Left);
            var named = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Children = { dot } };
            // La pestaña de detrás cuyo widget ha cambiado sin verse lleva el punto de novedad, hasta que se mira.
            if (!on && pane.Widget is { HasNews: true })
            {
                var news = Ui.NewsDot();
                news.Margin = new Thickness(6, 0, 0, 0);
                DockPanel.SetDock(news, Dock.Right);
                named.Children.Add(news);
            }
            named.Children.Add(title);
            // Se elige al soltar, en la cabecera: así una pestaña también se puede arrastrar.
            var tab = new Border
            {
                Child = named, Padding = new Thickness(12, 0), Height = 31, CornerRadius = new CornerRadius(9, 9, 0, 0), Cursor = new Cursor(StandardCursorType.Hand), Tag = i,
                Background = on ? new SolidColorBrush(PageTheme.PageColor()) : Brushes.Transparent,
            };
            if (mixed && RepoOf(pane) is { } repo)
            {
                tab.BorderBrush = RepoColors.Of(repo);
                tab.BorderThickness = new Thickness(0, 2.5, 0, 0);
            }
            DiaryInk.Tip(tab, DiaryInk.Card(FullName(pane), "Púlsala para verla; arrástrala a otro recuadro, a otro corcho o fuera, a una ventana suelta."));
            tabs.Children.Add(tab);
        }
        bar.Children.Add(tabs);
        // Sin sitio para todas, las pestañas se encogen a partes iguales y su nombre se recorta.
        bar.SizeChanged += (_, e) =>
        {
            var room = (e.NewSize.Width - tools.Bounds.Width - 8) / tabs.Children.Count - tabs.Spacing;
            foreach (var tab in tabs.Children) tab.MaxWidth = Math.Max(48, room);
        };
        head.Child = bar;
    }

    /// <summary>De qué repositorio y carpeta es un panel, para su cabecera: «Tienda · DEVLAB-99»; null en una vista de la app.</summary>
    private static string? PlaceOf(PiecePane pane)
    {
        if (NativeViews.Is(pane.Spec.Kind)) return null;
        var (name, place) = Naming(Pieces.Session(pane.Spec.SessionId), pane);
        return place is null ? name : $"{name} · {place}";
    }

    /// <summary>El nombre de un panel en su pestaña: la parte y su repositorio, «Chat · Tienda», y también la carpeta si en
    /// el corcho hay sesiones del mismo repositorio en carpetas distintas, «Chat · Tienda · DEVLAB-99»; en una vista de la
    /// app, solo su nombre.</summary>
    /// <remarks>La carpeta solo cuando hace falta: en una pestaña estrecha el nombre se recorta por el final, y es justo lo
    /// primero que se perdería.</remarks>
    private string TabName(PiecePane pane)
    {
        if (RepoOf(pane) is not { } repo) return PartName(pane.Spec);
        return InSeveralFolders(repo) ? FullName(pane) : $"{PartName(pane.Spec)} · {repo}";
    }

    /// <summary>El nombre completo de un panel, con su repositorio y su carpeta: «Chat · Tienda · DEVLAB-99».</summary>
    private static string FullName(PiecePane pane) => PlaceOf(pane) is { } place ? $"{PartName(pane.Spec)} · {place}" : PartName(pane.Spec);

    /// <summary>Si en el corcho hay paneles de un repositorio en más de una carpeta, como dos worktrees.</summary>
    private bool InSeveralFolders(string repo) =>
        Panes().Where(p => string.Equals(RepoOf(p), repo, StringComparison.OrdinalIgnoreCase))
            .Select(p => Naming(Pieces.Session(p.Spec.SessionId), p).Place ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();

    // ARRASTRAR

    /// <summary>Hace que la cabecera de un recuadro arrastre su panel (el de la pestaña agarrada, o el que se ve) y que,
    /// pulsada sin arrastrar, elija la pestaña pulsada.</summary>
    private void Draggable(Border head, Stack stack)
    {
        Point? start = null;
        int? tab = null;
        var dragged = false;
        head.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(head).Properties.IsLeftButtonPressed) return;
            // Los botones de la cabecera son suyos: no arrastran.
            if (e.Source is Visual pressed && pressed.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
            start = e.GetPosition(this);
            tab = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Tag is int)?.Tag as int?;
            dragged = false;
            e.Pointer.Capture(head);
        };
        head.PointerMoved += (_, e) =>
        {
            if (start is not { } from) return;
            var now = e.GetPosition(this);
            if (!dragged)
            {
                if (Math.Abs(now.X - from.X) + Math.Abs(now.Y - from.Y) < DragStart || stack.Panes.Count == 0) return;
                dragged = true;
                BeginDrag(stack.Panes[tab is { } i && i < stack.Panes.Count ? i : stack.Selected]);
            }
            MoveDrag(this.PointToScreen(now));
        };
        head.PointerCaptureLost += (_, _) =>
        {
            start = null;
            CancelDrag();
        };
        head.PointerReleased += (_, e) =>
        {
            var index = tab;
            if (start is null) return;
            start = null;
            if (dragged) EndDrag(this.PointToScreen(e.GetPosition(this)));
            e.Pointer.Capture(null);
            if (dragged || index is not { } chosen || chosen == stack.Selected || chosen >= stack.Panes.Count) return;
            stack.Selected = chosen;
            Rebuild();
        };
    }

    /// <summary>Empieza a arrastrar un panel: la tarjeta con su nombre empieza a seguir al cursor, y los tooltips callan
    /// hasta soltar.</summary>
    /// <remarks>El panel se queda en su sitio hasta soltarlo: si su recuadro se cerrara al empezar, todo se recolocaría
    /// bajo el cursor y el sitio al que se apuntaba se movería.</remarks>
    private void BeginDrag(PiecePane pane)
    {
        _drag = new Dragging(pane, this);
        foreach (var cork in Corks) ToolTip.SetServiceEnabled(cork, false);
        (_ghost ??= new DragGhost()).Start(pane.Spec.Title);
    }

    /// <summary>Sigue el arrastre con el cursor, en píxeles de la pantalla: mueve la tarjeta y, si cambia dónde caería,
    /// cierra el hueco de antes y abre el nuevo, apartando a los demás; para juntarlo en pestañas, marca el recuadro.</summary>
    private static void MoveDrag(PixelPoint screen)
    {
        if (_drag is not { } drag) return;
        var drop = DropAt(screen);
        _ghost?.Follow(screen, drop.Cork is null);
        // Entre dos recuadros, sobre un separador, se queda como estaba.
        if (drop is { Cork: not null, Stack: null, Zone: Zone.Center }) return;
        if (drag.Target is { } now && now.Cork == drop.Cork && now.Stack == drop.Stack && now.Zone == drop.Zone) return;
        ClearPreview(drag);
        drag.Target = drop;
        if (drop is { Cork: { } edged, Stack: null } && drop.Zone >= Zone.EdgeLeft)
        {
            var across = drop.Zone is Zone.EdgeLeft or Zone.EdgeRight;
            var whole = new Stack { Preview = true, Label = across ? "Columna entera" : "Fila entera" };
            edged._dropRects = new Dictionary<Node, Rect>(edged._rects);
            drag.Undo = edged.InsertAtEdge(whole, across ? Orientation.Horizontal : Orientation.Vertical, drop.Zone is Zone.EdgeLeft or Zone.EdgeTop);
            drag.Hole = whole;
            edged.Rebuild();
            return;
        }
        if (drop is not { Cork: { } cork, Stack: { } beside } || Stays(drag.Pane, drop)) return;
        if (beside.Hole)
        {
            drag.Lit = (cork, beside);
            cork.Light(beside, true);
            return;
        }
        if (drop.Zone == Zone.Center)
        {
            (_hint ??= new DropHint()).ShowAt(drop.Area);
            return;
        }
        var fresh = new Stack { Preview = true };
        cork._dropRects = new Dictionary<Node, Rect>(cork._rects);
        drag.Undo = cork.SplitBesideUndoable(beside, fresh, drop.Zone is Zone.Left or Zone.Right ? Orientation.Horizontal : Orientation.Vertical,
            drop.Zone is Zone.Left or Zone.Top);
        drag.Hole = fresh;
        cork.Rebuild();
    }

    /// <summary>Si soltar el panel ahí lo deja como está: en sus propias pestañas, o al lado del recuadro en que va solo.</summary>
    private static bool Stays(PiecePane pane, Drop drop) =>
        drop.Stack is { } own && own.Panes.Contains(pane) && (drop.Zone == Zone.Center || own.Panes.Count == 1);

    /// <summary>Quita lo que enseñaba dónde caería el panel: el hueco abierto, que devuelve su sitio a los demás, o la marca
    /// sobre un recuadro.</summary>
    private static void ClearPreview(Dragging drag)
    {
        _hint?.ShowAt(null);
        if (drag.Lit is var (litCork, litStack)) litCork.Light(litStack, false);
        drag.Lit = null;
        if (drag is { Undo: { } undo, Target.Cork: { } cork })
        {
            undo();
            cork._dropRects = null;
            cork.Rebuild();
        }
        drag.Undo = null;
        drag.Hole = null;
    }

    /// <summary>Suelta el panel arrastrado donde marcaba: en el hueco abierto, en las pestañas del recuadro marcado, o fuera
    /// de todos los corchos, en una ventana suelta donde está el cursor.</summary>
    /// <remarks>Al cambiar de corcho, el panel cambia de ventana y su página se recarga.</remarks>
    private static void EndDrag(PixelPoint screen)
    {
        if (_drag is not { } drag) return;
        Finish();
        var (pane, source) = (drag.Pane, drag.Source);
        if (drag.Target?.Cork is not { } cork)
        {
            source.Detach(pane, offBoard: true);
            source.Rebuild();
            PieceWindow.Hold(pane, screen);
            return;
        }
        if (Stays(pane, drag.Target))
        {
            ClearPreview(drag);
            return;
        }
        source.Detach(pane, offBoard: cork != source);
        if (!cork.Board.Children.Contains(pane)) cork.Board.Children.Add(pane);
        cork._dropRects = null;
        if (drag.Hole is { } hole)
        {
            hole.Preview = false;
            hole.Panes.Add(pane);
        }
        else if (drag.Target.Stack is { } stack)
        {
            // Soltar sobre el hueco de un perfil lo llena con lo soltado: deja de esperar sus partes.
            stack.Want = null;
            stack.Panes.Add(pane);
            stack.Selected = stack.Panes.Count - 1;
        }
        if (cork != source) source.Rebuild();
        cork.Rebuild();
        cork.Bring();
    }

    /// <summary>Deshace el arrastre en curso, si lo hay: cierra el hueco abierto y todo queda como estaba.</summary>
    private static void CancelDrag()
    {
        if (_drag is not { } drag) return;
        Finish();
        ClearPreview(drag);
    }

    /// <summary>Da por acabado el arrastre: esconde la tarjeta y la marca, y los tooltips vuelven.</summary>
    private static void Finish()
    {
        _drag = null;
        _ghost?.Hide();
        _hint?.ShowAt(null);
        foreach (var cork in Corks) ToolTip.SetServiceEnabled(cork, true);
    }

    /// <summary>Dónde caería lo que se suelta en un punto de la pantalla: el recuadro de un corcho que hay debajo y por qué
    /// lado, o fuera de todos.</summary>
    /// <remarks>Si hay corchos solapados, cuenta antes el último que se usó. Con un hueco abierto, se mira dónde estaban
    /// los recuadros antes de abrirlo.</remarks>
    private static Drop DropAt(PixelPoint screen)
    {
        foreach (var cork in Corks.OrderBy(c => c == _last ? 0 : 1))
        {
            if (cork.WindowState == WindowState.Minimized || !cork.IsVisible) continue;
            var local = cork.Board.PointToClient(screen);
            var board = new Rect(cork.Board.Bounds.Size);
            if (!board.Contains(local)) continue;
            // Pegado a un borde del corcho, una columna o una fila entera; salvo en un corcho vacío, que es todo un hueco.
            var edge = local.X < EdgeBand ? Zone.EdgeLeft : local.X > board.Width - EdgeBand ? Zone.EdgeRight
                : local.Y < EdgeBand ? Zone.EdgeTop : local.Y > board.Height - EdgeBand ? Zone.EdgeBottom : Zone.Center;
            if (edge != Zone.Center && cork._root is not Stack { Hole: true }) return new Drop(cork, null, edge, cork.ScreenRect(board));
            var rects = cork._dropRects ?? cork._rects;
            var stack = cork.Stacks().FirstOrDefault(s => rects.TryGetValue(s, out var r) && r.Contains(local));
            if (stack is null) return new Drop(cork, null, Zone.Center, cork.ScreenRect(board));
            var rect = rects[stack];
            var (x, y) = ((local.X - rect.X) / rect.Width, (local.Y - rect.Y) / rect.Height);
            // En un hueco, cualquier sitio es soltar dentro: es para eso.
            var zone = stack.Hole ? Zone.Center
                : x < EdgeShare ? Zone.Left : x > 1 - EdgeShare ? Zone.Right : y < EdgeShare ? Zone.Top : y > 1 - EdgeShare ? Zone.Bottom : Zone.Center;
            return new Drop(cork, stack, zone, cork.ScreenRect(rect));
        }
        return new Drop(null, null, Zone.Center, default);
    }

    /// <summary>Un rectángulo de la superficie, en píxeles de la pantalla.</summary>
    private PixelRect ScreenRect(Rect rect) => new(Board.PointToScreen(rect.TopLeft), Board.PointToScreen(rect.BottomRight));

    /// <summary>El separador entre dos hijos de una división: arrastrándolo se reparte el sitio entre los dos.</summary>
    private Control Splitter(Split split, int index)
    {
        var across = split.Orientation == Orientation.Horizontal;
        var line = new Border
        {
            Width = across ? 2 : 40, Height = across ? 40 : 2, CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        line.Bind(Border.BackgroundProperty, this.GetResourceObservable("TrackEdge"));
        var gap = new Border
        {
            Child = line, Background = Brushes.Transparent,
            Cursor = new Cursor(across ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth),
        };
        DiaryInk.Tip(gap, DiaryInk.Card("Repartir el sitio", "Arrastra para dar más a un lado que al otro."));
        Point? from = null;
        double first = 0, pair = 0;
        gap.PointerEntered += (_, _) => line.Bind(Border.BackgroundProperty, this.GetResourceObservable("Accent"));
        gap.PointerExited += (_, _) =>
        {
            if (from is null) line.Bind(Border.BackgroundProperty, this.GetResourceObservable("TrackEdge"));
        };
        gap.PointerPressed += (_, e) =>
        {
            from = e.GetPosition(Board);
            e.Pointer.Capture(gap);
            var a = _rects.GetValueOrDefault(split.Children[index]);
            var b = _rects.GetValueOrDefault(split.Children[index + 1]);
            (first, pair) = across ? (a.Width, a.Width + b.Width) : (a.Height, a.Height + b.Height);
        };
        gap.PointerMoved += (_, e) =>
        {
            if (from is not { } start || pair <= 0) return;
            var now = e.GetPosition(Board);
            var moved = Math.Clamp(first + (across ? now.X - start.X : now.Y - start.Y), MinPart, Math.Max(MinPart, pair - MinPart));
            var weights = split.Weights[index] + split.Weights[index + 1];
            split.Weights[index] = weights * moved / pair;
            split.Weights[index + 1] = weights - split.Weights[index];
            RelayoutSoon();
        };
        gap.PointerReleased += (_, e) =>
        {
            from = null;
            e.Pointer.Capture(null);
            ScheduleSave();
            line.Bind(Border.BackgroundProperty, this.GetResourceObservable("TrackEdge"));
        };
        return gap;
    }

    // EL MENÚ DE UN RECUADRO

    /// <summary>Abre el menú del panel que se ve en un recuadro, por grupos: partirlo, juntarlo con otro o girar su
    /// división; organizar todo el corcho en columnas, filas o cuadrícula; llevarlo a otro corcho o a una ventana suelta;
    /// y cerrarlo.</summary>
    private void Menu(Stack stack, Control anchor)
    {
        var pane = stack.Panes[stack.Selected];
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(Caption(Labelled(Mark(pane, 7), pane.Spec.Title)));

        // Lo del widget va primero: es de lo que enseña el recuadro, no de dónde está.
        if (pane.Widget is { } widget)
        {
            foreach (var item in widget.MenuItems()) menu.Items.Add(item);
            if (widget.InPanel) menu.Items.Add(Item("Abrir en el panel", "window", () => SessionActions.RequestSection(widget.Section)));
            menu.Items.Add(Item("Otro widget igual", "plus", () => Duplicate(pane)));
            menu.Items.Add(new Separator());
        }

        // Partir deja un hueco al lado, para montar la composición y llenarla después; una pestaña se saca arrastrándola.
        menu.Items.Add(Item("Partir a la derecha", "split-right", () => SplitHole(stack, Orientation.Horizontal)));
        menu.Items.Add(Item("Partir abajo", "split-down", () => SplitHole(stack, Orientation.Vertical)));
        var join = Item("Juntar en pestañas con", "tabs", null);
        foreach (var other in Stacks().Where(s => s != stack && !s.Hole))
        {
            var shown = other.Panes[other.Selected];
            join.Items.Add(Item(Marked(shown), null, () => JoinInto(pane, other)));
        }
        join.IsEnabled = join.Items.Count > 0;
        menu.Items.Add(join);
        if (stack.Parent is { } parent)
            menu.Items.Add(Item(parent.Orientation == Orientation.Horizontal ? "Poner uno debajo de otro" : "Poner uno al lado de otro", "rotate", () =>
            {
                parent.Orientation = parent.Orientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal;
                Rebuild();
            }));

        menu.Items.Add(new Separator());
        menu.Items.Add(Caption("Organizar el corcho"));
        menu.Items.Add(Arrangements(menu));

        menu.Items.Add(new Separator());
        var move = Item("Llevar a", "move", null);
        move.Items.Add(Item(Hinted(DisplayName, "aquí"), "pin", null, enabled: false));
        foreach (var cork in Corks.Where(c => c != this))
            move.Items.Add(Item(Hinted(cork.DisplayName, cork.Contents()), "pin", () => MoveTo(pane, cork)));
        move.Items.Add(Item("Un corcho nuevo", "plus", () => MoveTo(pane, Create())));
        move.Items.Add(new Separator());
        move.Items.Add(Item("Una ventana suelta", "window", () =>
        {
            Detach(pane, offBoard: true);
            Rebuild();
            PieceWindow.Hold(pane);
        }));
        menu.Items.Add(move);

        menu.Items.Add(new Separator());
        var empty = Item(Hinted(stack.Panes.Count > 1 ? "Vaciar el recuadro" : "Vaciar", "deja el hueco"), null, () => EmptyStack(stack));
        empty.Icon = HoleIcon();
        menu.Items.Add(empty);
        var danger = this.FindResource("Danger") as IBrush ?? Brushes.IndianRed;
        var close = Item("Cerrar", null, () => ClosePane(pane));
        close.Icon = CorkIcons.Make("close", 15, danger);
        close.Foreground = danger;
        menu.Items.Add(close);
        menu.Open(anchor);
    }

    /// <summary>Abre al lado una copia del widget de un panel, con lo mismo elegido, para cambiarla sin tocar el primero.</summary>
    private void Duplicate(PiecePane pane)
    {
        var copy = Pieces.Make(pane.Spec with { Path = Guid.NewGuid().ToString("N")[..8] });
        copy.WidgetState = pane.WidgetState;
        Add(copy);
    }

    /// <summary>Guarda los corchos dentro de un momento porque ha cambiado lo elegido en un widget.</summary>
    internal static void NotifyStateChanged() => ScheduleSave();

    /// <summary>Una opción del menú, con su icono si lo lleva; sin acción, es la que abre un submenú.</summary>
    private static MenuItem Item(object header, string? icon, Action? act, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (icon is not null) item.Icon = CorkIcons.Make(icon) is var shape && !enabled ? WithOpacity(shape, 0.45) : shape;
        if (act is not null) item.Click += (_, _) => act();
        return item;
    }

    /// <summary>Un control con la opacidad dada: el icono de una opción que no se puede pulsar, apagado como su texto.</summary>
    private static Control WithOpacity(Control control, double opacity)
    {
        control.Opacity = opacity;
        return control;
    }

    /// <summary>Un título de grupo del menú, que no se pulsa.</summary>
    private static MenuItem Caption(object header)
    {
        var caption = new MenuItem { Header = header };
        caption.Classes.Add("caption");
        return caption;
    }

    /// <summary>Un texto con algo delante, como el punto de una sesión antes de su nombre.</summary>
    private static Control Labelled(Control lead, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 8,
        Children = { lead, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
    };

    /// <summary>Un recuadro en una lista del menú: una raya a la izquierda del color de su repositorio (gris en una vista
    /// de la app), el punto de su estado y la parte; debajo, el repositorio y la carpeta.</summary>
    private Control Marked(PiecePane pane)
    {
        var name = new TextBlock { Text = PartName(pane.Spec), FontSize = 12.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var lines = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { Mark(pane, 7), name } });
        var place = new TextBlock { Text = PlaceOf(pane) ?? "vista de la app", FontSize = 10.5, Margin = new Thickness(14, 0, 0, 0) };
        place.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("MonoFont"));
        place.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgMuted"));
        lines.Children.Add(place);
        var rail = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 10, 2) };
        if (RepoOf(pane) is not null) rail.Background = EdgeBrush(pane);
        else rail.Bind(Border.BackgroundProperty, this.GetResourceObservable("TrackEdge"));
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { rail, lines } };
    }

    /// <summary>Un texto con una nota apagada a su derecha, como cuántos paneles tiene un corcho.</summary>
    private static Control Hinted(string text, string hint)
    {
        var note = new TextBlock { Text = hint, FontSize = 11, Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("FgFaint"));
        DockPanel.SetDock(note, Dock.Right);
        return new DockPanel { Children = { note, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
    }

    /// <summary>Lo que tiene un corcho, dicho en corto: cuántos paneles, o que está vacío.</summary>
    private string Contents() => Panes().Count() switch
    {
        0 => "vacío",
        1 => "1 panel",
        var n => $"{n} paneles",
    };

    /// <summary>La fila del menú con las tres maneras de organizar el corcho entero: columnas, filas y cuadrícula.</summary>
    /// <remarks>Con un solo recuadro no hay nada que organizar, y no se puede pulsar.</remarks>
    private MenuItem Arrangements(ContextMenu menu)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Width = 230 };
        var enabled = Stacks().Skip(1).Any();
        var choices = new[] { ("columns", "Columnas"), ("rows", "Filas"), ("grid", "Cuadrícula") };
        for (var i = 0; i < choices.Length; i++)
        {
            var (kind, label) = choices[i];
            var text = new TextBlock { Text = label, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center };
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("FgMuted"));
            var face = new Border
            {
                Padding = new Thickness(0, 7, 0, 5), Margin = new Thickness(i == 0 ? 0 : 2, 0, i == choices.Length - 1 ? 0 : 2, 0),
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                Child = new StackPanel { Spacing = 4, Children = { CorkIcons.Make(kind, 18), text } },
            };
            face.Bind(Border.BackgroundProperty, this.GetResourceObservable("ButtonFill"));
            face.Bind(Border.BorderBrushProperty, this.GetResourceObservable("TrackEdge"));
            var button = new Button { Theme = ThemeOf("PlainButton"), Content = face, IsEnabled = enabled };
            AutomationProperties.SetName(button, label);
            button.PointerEntered += (_, _) => face.Bind(Border.BackgroundProperty, this.GetResourceObservable("ButtonHover"));
            button.PointerExited += (_, _) => face.Bind(Border.BackgroundProperty, this.GetResourceObservable("ButtonFill"));
            button.Click += (_, _) =>
            {
                menu.Close();
                Organize(kind);
            };
            Grid.SetColumn(button, i);
            row.Children.Add(button);
        }
        var item = new MenuItem { Theme = this.FindResource("MenuCaption") as ControlTheme, Header = row, Padding = new Thickness(8, 2, 8, 6) };
        if (!enabled) item.Opacity = 0.5;
        return item;
    }

    /// <summary>Recoloca todos los recuadros del corcho, en el orden en que se ven, a partes iguales: en columnas, en filas
    /// o en una cuadrícula lo más cuadrada posible, con la última fila más ancha si no se llena.</summary>
    private void Organize(string kind)
    {
        var stacks = Stacks().ToList();
        if (stacks.Count < 2) return;
        if (kind == "grid")
        {
            var columns = (int)Math.Ceiling(Math.Sqrt(stacks.Count));
            var rows = stacks.Chunk(columns).Select(line => Line(line, Orientation.Horizontal)).ToList();
            _root = Line(rows, Orientation.Vertical);
        }
        else _root = Line(stacks, kind == "columns" ? Orientation.Horizontal : Orientation.Vertical);
        _root.Parent = null;
        Rebuild();
    }

    /// <summary>Una división a partes iguales con unos trozos en un sentido; un trozo solo va tal cual.</summary>
    private static Node Line(IReadOnlyList<Node> nodes, Orientation orientation)
    {
        if (nodes.Count == 1) return nodes[0];
        var split = new Split { Orientation = orientation };
        foreach (var node in nodes)
        {
            node.Parent = split;
            split.Children.Add(node);
            split.Weights.Add(1);
        }
        return split;
    }

    /// <summary>El icono de vaciar: el contorno discontinuo de un hueco.</summary>
    private Control HoleIcon()
    {
        var shape = new Rectangle { Width = 14, Height = 12, RadiusX = 2, RadiusY = 2, StrokeThickness = 1.6, StrokeDashArray = [2.2, 1.8] };
        shape.Bind(Shape.StrokeProperty, this.GetResourceObservable("FgMuted"));
        return shape;
    }

    /// <summary>Junta un panel en las pestañas de otro recuadro, y se queda a la vista.</summary>
    private void JoinInto(PiecePane pane, Stack target)
    {
        Detach(pane, offBoard: false);
        target.Panes.Add(pane);
        target.Selected = target.Panes.Count - 1;
        Rebuild();
    }

    /// <summary>Lleva un panel a otro corcho, en un recuadro nuevo.</summary>
    private void MoveTo(PiecePane pane, CorkWindow cork)
    {
        Detach(pane, offBoard: true);
        Rebuild();
        cork.Add(pane);
        cork.Bring();
    }

    /// <summary>Cierra un panel del todo.</summary>
    private void ClosePane(PiecePane pane)
    {
        Detach(pane, offBoard: true);
        pane.Close();
        Rebuild();
    }

    /// <summary>Quita un panel de su recuadro y lo cierra; si era el último, el recuadro se queda como un hueco, para llenarlo
    /// con otro o quitarlo con su cruz.</summary>
    private void EmptyPane(PiecePane pane)
    {
        if (StackOf(pane) is not { } stack) return;
        stack.Panes.Remove(pane);
        stack.Selected = Math.Clamp(stack.Selected, 0, Math.Max(0, stack.Panes.Count - 1));
        Board.Children.Remove(pane);
        pane.Close();
        Rebuild();
    }

    /// <summary>Un estilo de control de la app, por su clave.</summary>
    private ControlTheme? ThemeOf(string key) => this.FindResource(key) as ControlTheme;

    /// <summary>Minimiza la ventana.</summary>
    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Maximiza la ventana, o la devuelve a su tamaño si ya lo estaba.</summary>
    private void Maximize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>Cierra la ventana.</summary>
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
