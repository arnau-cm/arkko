using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección Artifacts: lo que los agentes han publicado desde las sesiones de este equipo, en una galería por repositorio;
/// lo que se abre se ve en el navegador integrado, con la lista compacta al lado.</summary>
/// <remarks>
/// Solo lee y abre: lo publicado vive en la web del agente, y la app no tiene acceso a la cuenta. «Quitar de la lista», el
/// orden y lo que se le pone a mano a lo de la galería solo cambian Arkko.
/// </remarks>
public partial class ArtifactsView : UserControl
{
    // Por debajo de este ancho no caben la lista y lo abierto a la vez: se ve solo lo abierto, como en un recuadro del corcho.
    private const double TwoPanes = 760;

    // El ancho mínimo de una tarjeta de la galería y el hueco entre tarjetas.
    private const double CardMin = 270;
    private const double CardGap = 8;

    private readonly ArtifactsModel _model = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private BrowserPane? _browser;
    private bool _active;
    private bool _narrow;

    /// <summary>Crea la sección; no enseña nada hasta que se entra en ella.</summary>
    public ArtifactsView()
    {
        InitializeComponent();
        DataContext = _model;
        _model.SelectedChanged += Show;
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ArtifactsModel.Selected)) Fit();
        };
        SizeChanged += (_, e) =>
        {
            _narrow = e.NewSize.Width < TwoPanes;
            Fit();
        };
        _clock.Tick += (_, _) => _model.Arrange();
        // Los avisos se escuchan mientras la vista está en una ventana: puede haber varias copias (el panel y los corchos).
        AttachedToVisualTree += (_, _) =>
        {
            ArtifactMonitor.Instance.Changed += OnChanged;
            RepoColors.Changed += OnChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            ArtifactMonitor.Instance.Changed -= OnChanged;
            RepoColors.Changed -= OnChanged;
        };
    }

    /// <summary>Ha cambiado lo publicado o los colores: se recoloca si se ve.</summary>
    private void OnChanged()
    {
        if (_active) _model.Arrange();
    }

    /// <summary>Al entrar en la sección: enseña lo último leído y pone en marcha el «hace…».</summary>
    public void Activate()
    {
        _active = true;
        _model.Arrange();
        _clock.Start();
        ArtifactMonitor.Instance.ScanIfDue();
    }

    /// <summary>Al salir de la sección o esconder el panel.</summary>
    public void Deactivate()
    {
        _active = false;
        _clock.Stop();
    }

    /// <summary>Deja ver solo lo de un repositorio o una carpeta, como al llegar desde Worktrees.</summary>
    public void Only(string? group, string? project = null) => _model.Only(group, project);

    /// <summary>Abre un artifact por su enlace, como al llegar desde Sesiones o el Diario; si no está en la lista, porque se quitó,
    /// se abre en el navegador del sistema.</summary>
    public void Open(string url)
    {
        if (!_model.Open(url)) SessionsView.OpenLink(url);
    }

    // GALERÍA, LISTA Y DETALLE

    /// <summary>Abre lo elegido en el navegador integrado, que se crea la primera vez.</summary>
    private void Show(ArtifactRow? row)
    {
        if (row is null) return;
        if (_browser is null)
        {
            _browser = new BrowserPane("Abriendo…");
            BrowserHost.Children.Add(_browser);
        }
        _browser.Navigate(row.Item.Url);
    }

    /// <summary>Reparte el ancho con algo abierto: con sitio, la lista compacta a la izquierda y lo abierto a la derecha; en
    /// estrecho, solo lo abierto.</summary>
    private void Fit()
    {
        var columns = Split.ColumnDefinitions;
        if (!_narrow)
        {
            if (columns[0].Width.Value < 1) columns[0].Width = new GridLength(320);
            columns[1].Width = new GridLength(10);
            ListPane.IsVisible = Splitter.IsVisible = true;
            return;
        }
        columns[0].Width = columns[1].Width = new GridLength(0);
        ListPane.IsVisible = Splitter.IsVisible = false;
    }

    /// <summary>Ajusta el ancho de las tarjetas de la galería para que cada fila la llene entera, con tantas columnas como quepan.</summary>
    private void GalleryBody_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width < 1) return;
        // Cada tarjeta ocupa su ancho más el hueco a su derecha, también la última de la fila.
        var columns = Math.Max(1, (int)(width / (CardMin + CardGap)));
        Resources["ArtCardWidth"] = Math.Floor(width / columns - CardGap);
    }

    /// <summary>Elige el primero de la lista, para el recorrido de capturas.</summary>
    internal void SelectFirst() => _model.Select(_model.Groups.FirstOrDefault()?.Rows.FirstOrDefault());

    /// <summary>Cierra lo abierto y vuelve a la galería, donde se había dejado.</summary>
    private void Close_Click(object? sender, RoutedEventArgs e) => _model.Select(null);

    /// <summary>Abre lo pulsado, salvo si se ha pulsado un botón.</summary>
    private void Row_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || OnButton(e)) return;
        if ((sender as Control)?.DataContext is ArtifactRow row) _model.Select(row);
    }

    private void Row_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not ArtifactRow row) return;
        Menu(row).Open(control);
        e.Handled = true;
    }

    /// <summary>Abre en Sesiones la sesión de lo elegido.</summary>
    private void Session_Click(object? sender, RoutedEventArgs e)
    {
        if (_model.Selected is { } row) SessionActions.RequestSession(row.Item.SessionId);
    }

    /// <summary>El menú de lo elegido, el mismo que el de su fila.</summary>
    private void DetailMore_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control button && _model.Selected is { } row) Menu(row).Open(button);
    }

    /// <summary>Pliega o despliega un repositorio al pulsar su cabecera, salvo si se ha pulsado una de sus flechas.</summary>
    private void Group_Released(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && !OnButton(e) && (sender as Control)?.DataContext is ArtifactGroup group)
            _model.ToggleFold(group.Name);
    }

    private void Up_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ArtifactGroup group) _model.Move(group.Name, -1);
    }

    private void Down_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ArtifactGroup group) _model.Move(group.Name, 1);
    }

    /// <summary>Si el clic ha caído en un botón: lo atiende el botón, no lo que lo contiene.</summary>
    private static bool OnButton(PointerReleasedEventArgs e) =>
        e.Source is Control source && source.FindAncestorOfType<Button>(includeSelf: true) is not null;

    /// <summary>Vuelve a ver todo, sin el filtro de procedencia.</summary>
    private void Scope_Click(object? sender, RoutedEventArgs e) => _model.Only(null);

    /// <summary>Devuelve a la lista algo quitado.</summary>
    private void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ArtifactRow row) ArtifactMonitor.Instance.SetRemoved(row.Item, false);
    }

    // GALERÍAS

    /// <summary>Abre bajo el botón la lista de las galerías de los agentes, donde está todo lo publicado, también lo que aquí no sale.</summary>
    private void Galleries_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control button) return;
        var menu = new ContextMenu();
        menu.Items.Add(Caption("Abrir la galería de"));
        foreach (var gallery in ArtifactTexts.Galleries)
        {
            var item = new MenuItem { Header = gallery.Name, Icon = AgentMark(gallery.Agent) };
            item.Click += (_, _) => SessionsView.OpenLink(gallery.Url);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Traer aquí la lista de Claude…", "", () => _ = AskForGallery()));
        menu.Open(button);
    }

    /// <summary>Copia la petición que trae a Arkko la lista de la galería de Claude, y dice dónde pegarla.</summary>
    /// <remarks>
    /// La app no tiene acceso a la cuenta: la lista la lee una sesión de Claude Code, que ya tiene la suya, con la herramienta
    /// Artifact, y la deja en el fichero que la app junta con su índice.
    /// </remarks>
    private async Task AskForGallery()
    {
        var file = ArtifactMonitor.Instance.GalleryFile;
        var ask = $"Lista mis artifacts de claude.ai con la herramienta Artifact (action: list, scope: mine, limit: 200) y guarda en «{file}» " +
                  "un JSON en UTF-8 sin BOM con solo los míos, con este formato y nada más: " +
                  "{\"fetched_utc\": \"<ahora, ISO 8601 en UTC>\", \"items\": [{\"url\": \"https://claude.ai/artifact/…\", \"title\": \"…\", " +
                  "\"updated_utc\": \"<su última versión, ISO 8601 en UTC>\"}]}. No cambies nada más.";
        await Copy(ask);
        await AppDialog.ShowAsync(TopLevel.GetTopLevel(this) as Window, "Petición copiada",
            "Pégala en cualquier sesión de Claude Code. Claude leerá la lista de tu cuenta y la dejará en Arkko; en un minuto sale aquí, en «De claude.ai», lo que no esté ya en la lista.",
            DialogTone.Question, note: file);
    }

    /// <summary>La marca de un agente para el menú de galerías; su logo si lo hay.</summary>
    private static Control AgentMark(string agent) => agent switch
    {
        "claude" => new ClaudeLogo { Width = 14, Height = 14 },
        _ => new TextBlock { Text = "", FontSize = 12, Classes = { "icon" } },
    };

    // MENÚ DE UNA TARJETA

    /// <summary>El menú de una tarjeta: abrir, copiar el enlace, ir a la sesión, darle repositorio y clase si es de la galería,
    /// ir al fichero y quitar de la lista.</summary>
    private ContextMenu Menu(ArtifactRow row)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Abrir en el navegador del sistema", "", () => SessionsView.OpenLink(row.Item.Url)));
        menu.Items.Add(Item("Copiar el enlace", "", () => _ = Copy(row.Item.Url)));
        if (!row.Item.FromGallery) menu.Items.Add(Item("Ir a la sesión", "", () => SessionActions.RequestSession(row.Item.SessionId)));
        // Lo de la galería no sabe de dónde viene: la persona le puede dar repositorio y clase.
        if (row.Item.FromGallery)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(RepoChoices(row));
            menu.Items.Add(KindChoices(row));
        }
        if (row.CanReveal) menu.Items.Add(Item("Abrir la carpeta del fichero", "", () => Folders.Reveal(row.Item.File!)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quitar de la lista", "", () => _ = Remove(row)));
        return menu;
    }

    /// <summary>«Mover a un repositorio»: cada repositorio como una pastilla de su color, con cuántos tiene ya, y el de ahora marcado.</summary>
    private static MenuItem RepoChoices(ArtifactRow row)
    {
        var menu = Item("Mover a un repositorio", "", () => { });
        var counts = ArtifactMonitor.Instance.Items.Where(a => !a.Removed && !a.Deleted)
            .GroupBy(ArtifactTexts.Home, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        menu.Items.Add(Caption("Repositorios"));
        foreach (var repo in KnownRepos())
            menu.Items.Add(Choice(RepoPill(repo, RepoColors.Of(repo), counts.GetValueOrDefault(repo)), row.Item.Repo == repo, () => Change(row, a => a.Repo = repo)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Choice(RepoPill("Sin repositorio", ArtifactTexts.HomeBrush(ArtifactTexts.FromGallery), null, "Vuelve a «De claude.ai»"), row.Item.Repo is null,
            () => Change(row, a => a.Repo = null)));
        return menu;
    }

    /// <summary>«Cambiar la clase»: cada clase con su baldosa de color, y la de ahora marcada.</summary>
    private static MenuItem KindChoices(ArtifactRow row)
    {
        var menu = Item("Cambiar la clase", "", () => { });
        foreach (var kind in Enum.GetValues<ArtifactKind>().Where(k => k != ArtifactKind.Other))
            menu.Items.Add(Choice(KindLabel(kind), row.Item.Kind == kind, () => Change(row, a => a.Kind = kind)));
        return menu;
    }

    /// <summary>Una pastilla de repositorio para un menú: borde izquierdo y fondo tenue de su color, su nombre y, a la derecha,
    /// cuántos tiene o una nota.</summary>
    private static Control RepoPill(string name, IBrush color, int? count, string? note = null)
    {
        var tint = color is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.12) : (IBrush)Brushes.Transparent;
        var row = new DockPanel { MinWidth = 200 };
        var right = DiaryInk.Text(count is { } n ? (n == 1 ? "1 artifact" : $"{n} artifacts") : note ?? "", 10.5, DiaryInk.Faint);
        right.Margin = new Thickness(14, 0, 0, 0);
        right.VerticalAlignment = VerticalAlignment.Center;
        if (count is not 0) DockPanel.SetDock(right, Dock.Right);
        if (count is not 0) row.Children.Add(right);
        var title = DiaryInk.Text(name, 12.5, bold: true);
        title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(title);
        return new Border
        {
            BorderBrush = color, BorderThickness = new Thickness(3, 0, 0, 0), Background = tint, CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 4, 10, 5), Child = row,
        };
    }

    /// <summary>Una clase para un menú: su baldosa de color con su icono, o la chispa de Claude si es la de sin clase, y su nombre.</summary>
    private static Control KindLabel(ArtifactKind kind)
    {
        Control mark = kind == ArtifactKind.Unknown
            ? new ClaudeLogo { Width = 11, Height = 11 }
            : new TextBlock { Text = ArtifactTexts.Icon(kind), FontSize = 11, Foreground = ArtifactTexts.Color(kind), Classes = { "icon", "ownColor" } };
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        var tile = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = ArtifactTexts.Color(kind, 0.16), Child = mark };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        label.Children.Add(tile);
        var text = DiaryInk.Text(kind == ArtifactKind.Unknown ? "Sin clase" : ArtifactTexts.Kind(kind), 12.5);
        text.VerticalAlignment = VerticalAlignment.Center;
        label.Children.Add(text);
        return label;
    }

    /// <summary>Una opción de un submenú con su propio dibujo, con una marca si es la que está puesta.</summary>
    private static MenuItem Choice(Control header, bool on, Action act)
    {
        var check = new TextBlock { Text = on ? "" : "", FontSize = 11, Foreground = ThemeColors.Of("Accent", "#61AFEF"), Classes = { "icon", "ownColor" } };
        var item = new MenuItem { Header = header, Icon = check, Padding = new Thickness(8, 4) };
        item.Click += (_, _) => act();
        return item;
    }

    /// <summary>Un título de grupo del menú, que no se pulsa.</summary>
    private static MenuItem Caption(string text)
    {
        var caption = new MenuItem { Header = text };
        caption.Classes.Add("caption");
        return caption;
    }

    /// <summary>Le cambia algo a lo de la galería y la lista se recoloca.</summary>
    private static void Change(ArtifactRow row, Action<AgentArtifact> change) => ArtifactMonitor.Instance.Change(row.Item, change);

    /// <summary>Los repositorios que conoce la app: los de lo publicado, los de Worktrees, los de las sesiones abiertas y los
    /// de las carpetas de todas las sesiones de Claude Code y sus vecinos.</summary>
    private static List<string> KnownRepos() => ArtifactMonitor.Instance.Items.Where(a => !a.FromGallery).Select(ArtifactTexts.Home)
        .Concat(WorktreeMonitor.Instance.Repos.Select(r => r.Name))
        .Concat(Pieces.Sessions().Select(s => s.Repo ?? s.Project).OfType<string>())
        .Concat(ArtifactTexts.SessionRepos())
        .Where(n => n is not (ArtifactTexts.NoRepo or ArtifactTexts.FromGallery))
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Una opción del menú con su icono.</summary>
    private static MenuItem Item(string text, string icon, Action act)
    {
        var glyph = new TextBlock { Text = icon, FontSize = 12 };
        glyph.Classes.Add("icon");
        var item = new MenuItem { Header = text, Icon = glyph };
        item.Click += (_, _) => act();
        return item;
    }

    /// <summary>Copia un enlace al portapapeles.</summary>
    private async Task Copy(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("artifacts", ex);
        }
    }

    /// <summary>Quita algo de la lista de Arkko, después de decir que en claude.ai sigue existiendo.</summary>
    private async Task Remove(ArtifactRow row)
    {
        var ok = await AppDialog.ConfirmAsync(TopLevel.GetTopLevel(this) as Window, "Quitar de la lista",
            $"«{row.Title}» deja de salir en Arkko. En claude.ai sigue existiendo: para borrarlo de verdad, hazlo allí o pídeselo a Claude en una sesión.",
            "Quitar", note: "Lo quitado se puede devolver a la lista desde «Quitados», al final.");
        if (ok) ArtifactMonitor.Instance.SetRemoved(row.Item, true);
    }
}
