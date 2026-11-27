using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>El widget de Worktrees: una lista por repositorio, con una fila por worktree que se despliega en el sitio, y
/// arriba tres contadores que también filtran (sin subir, se pueden cerrar, PR abiertas).</summary>
/// <remarks>
/// Como la sección, solo lee y sugiere: cerrar uno es copiar la orden y pegarla donde toca. Los que ya se pueden cerrar
/// (la PR mergeada, la tarea hecha) van arriba de su repositorio, con una pastilla verde y la orden a mano. Las ramas base
/// solo salen si tienen algo sin subir, como aviso; en L, en su línea, como en el panel.
/// </remarks>
internal sealed class WorktreesWidget : CorkWidget
{
    // Los filtros de los contadores: el nombre que se guarda, cómo se dice y qué worktrees deja.
    private static readonly (string Id, string Name, Func<WorktreeRow, bool> Keeps)[] Filters =
    [
        ("pending", "Sin subir", r => r.LocalPillVisible),
        ("leftover", "Se pueden cerrar", r => r.Leftover),
        ("pr", "PR abiertas", r => r.Item.Pr is not null && r.Item.PrState == PrState.Open),
    ];

    private readonly WorktreesModel _model = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);
    private string? _copied;

    /// <summary>Crea el widget; no lee nada hasta que se ve.</summary>
    public WorktreesWidget()
    {
        _clock.Tick += (_, _) => Render();
    }

    public override PanelSection Section => PanelSection.Worktrees;

    /// <summary>Ámbar si algo está sin subir, verde si alguno se puede cerrar; nada si está todo al día.</summary>
    public override IBrush? Mood
    {
        get
        {
            var rows = _model.Groups.SelectMany(g => g.Rows.Concat(g.Bases)).ToList();
            if (rows.Any(r => r.LocalPillVisible)) return Brushes_.Waiting;
            return rows.Any(r => r.Leftover) ? Brushes_.Done : null;
        }
    }

    /// <summary>Cuántos se pueden cerrar o, si ninguno, cuántos hay.</summary>
    public override string? Headline
    {
        get
        {
            var rows = _model.Groups.SelectMany(g => g.Rows).ToList();
            var closeable = rows.Count(r => r.Leftover);
            if (closeable > 0) return closeable == 1 ? "1 para cerrar" : $"{closeable} para cerrar";
            return rows.Count == 1 ? "1 worktree" : $"{rows.Count} worktrees";
        }
    }

    protected override void Attached()
    {
        WorktreeMonitor.Instance.Changed += OnChanged;
        TaskDirectory.Instance.Changed += OnChanged;
        RepoColors.Changed += OnChanged;
        ArtifactMonitor.Instance.Changed += OnChanged;
    }

    protected override void Detached()
    {
        WorktreeMonitor.Instance.Changed -= OnChanged;
        TaskDirectory.Instance.Changed -= OnChanged;
        RepoColors.Changed -= OnChanged;
        ArtifactMonitor.Instance.Changed -= OnChanged;
        _clock.Stop();
    }

    protected override async void Started()
    {
        _clock.Start();
        _model.Arrange();
        await WorktreeMonitor.Instance.RefreshAsync(WorktreeMonitor.Stale);
    }

    protected override void Stopped() => _clock.Stop();

    /// <summary>Ha cambiado algo de lo que se enseña: se recoloca y se repinta si se ve.</summary>
    private void OnChanged()
    {
        if (!Active) return;
        Refreshed();
        _model.Arrange();
        Render();
    }

    public override IEnumerable<Control> MenuItems()
    {
        yield return Ui.Item("Volver a mirar los worktrees", () => _ = WorktreeMonitor.Instance.RefreshAsync(fresh: true));
    }

    protected override Control Build()
    {
        if (_model.LoadingVisible) return Ui.Empty("Mirando los worktrees…");
        if (_model.Groups.Count == 0) return Ui.Empty(_model.EmptyText);
        var all = _model.Groups.SelectMany(g => g.Rows).ToList();
        var filter = Filters.FirstOrDefault(f => f.Id == Get("filter"));
        if (filter.Id is not null && !all.Any(filter.Keeps)) filter = default;

        var dock = new DockPanel();
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var refresh = Ui.IconButton("", () => _ = WorktreeMonitor.Instance.RefreshAsync(fresh: true),
            DiaryInk.Card("Volver a mirar", "Los worktrees y, en el remoto, sus PR."));
        DockPanel.SetDock(refresh, Dock.Right);
        bar.Children.Add(refresh);
        if (Size != WidgetSize.S)
        {
            var updated = Ui.Text(_model.UpdatedText, 11, Ui.Faint);
            updated.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(updated, Dock.Right);
            bar.Children.Add(updated);
        }
        var repos = _model.Groups.Count;
        bar.Children.Add(Ui.Text(Size == WidgetSize.S ? $"{all.Count} en {repos} {(repos == 1 ? "repo" : "repos")}" : _model.Summary, 12, Ui.Muted));
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        var counters = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var f in Filters)
        {
            var count = all.Count(f.Keeps);
            var on = f.Id == filter.Id;
            var chip = Ui.Chip(Ui.Row(6, Ui.Text(f.Name, 11.5), Ui.Text(count.ToString(), 11, on ? Ui.Fg : Ui.Faint, bold: true, mono: true)), on, () =>
            {
                Set("filter", on ? null : f.Id);
                Render();
            }, DiaryInk.Card(f.Name, on ? "Pulsa otra vez para ver todos." : "Pulsa para ver solo estos."));
            chip.IsEnabled = count > 0 || on;
            chip.Margin = new Thickness(0, 0, 6, 0);
            counters.Children.Add(chip);
        }
        DockPanel.SetDock(counters, Dock.Top);
        dock.Children.Add(counters);

        var groups = _model.Groups.Select(g => Group(g, filter.Keeps)).Where(c => c is not null).Cast<Control>().ToList();
        if (groups.Count == 0)
        {
            dock.Children.Add(Ui.Empty("Ninguno con este filtro."));
            return dock;
        }
        Control body = Size == WidgetSize.L ? Ui.Columns(2, 12, Halves(groups)) : Ui.Column(12, groups.ToArray());
        dock.Children.Add(Scroll("list", body));
        return dock;
    }

    /// <summary>Reparte los repositorios en dos columnas, alternando, para la forma de L.</summary>
    private static IEnumerable<Control> Halves(List<Control> groups)
    {
        var left = new StackPanel { Spacing = 12 };
        var right = new StackPanel { Spacing = 12 };
        for (var i = 0; i < groups.Count; i++) (i % 2 == 0 ? left : right).Children.Add(groups[i]);
        return [left, right];
    }

    /// <summary>Un repositorio: su nombre en su color, su aviso de ramas base si toca y sus worktrees, primero los que piden algo;
    /// plegado, solo su nombre.</summary>
    private Control? Group(WorktreeGroup group, Func<WorktreeRow, bool>? keeps)
    {
        var rows = group.Rows.Where(r => keeps is null || keeps(r))
            .OrderByDescending(r => r.Leftover).ThenByDescending(r => r.LocalPillVisible).ToList();
        var dirty = group.Bases.Where(b => b.LocalPillVisible).ToList();
        if (rows.Count == 0 && (keeps is not null || (dirty.Count == 0 && Size != WidgetSize.L))) return null;

        var list = new StackPanel { Spacing = 5 };
        var head = FoldHead(group.Name, new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = RepoColors.Of(group.Name) },
            Ui.Text(group.Title, 11, Ui.Fg, bold: true), Ui.Text(rows.Count == 0 ? "solo ramas base" : rows.Count.ToString(), 11, Ui.Faint));
        list.Children.Add(head);
        if (Folded(group.Name))
        {
            foreach (var row in rows) Note("trees", row.Path, Signature(row), group.Name);
            return list;
        }
        if (group.RemoteErrorVisible) list.Children.Add(Ui.Text(group.RemoteErrorText, 11, Ui.Wait, wrap: true));
        if (Size == WidgetSize.L && keeps is null && group.BasesVisible)
        {
            var bases = Ui.Row(6, Ui.Text("BASE", 10, Ui.Faint, bold: true));
            foreach (var b in group.Bases)
                bases.Children.Add(Ui.Pill(b.LocalPillVisible ? $"{b.Folder} · {b.LocalText}" : b.Folder, b.LocalPillVisible ? b.LocalBrush : Ui.Muted,
                    b.LocalPillVisible ? b.LocalPillFill : null));
            list.Children.Add(bases);
        }
        else if (keeps is null)
            foreach (var b in dirty)
            {
                var warning = new Border
                {
                    Background = ThemeColors.Of("DataFiveHour", "#E5C07B", 0.12), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4),
                    Child = Ui.Text($"⚠ {b.Folder} · {b.LocalText}", 11, b.LocalBrush),
                };
                DiaryInk.Tip(warning, DiaryInk.Card("Rama base con algo sin subir", b.Path, footer: "Las ramas base no se cierran nunca."));
                list.Children.Add(warning);
            }
        foreach (var row in rows) list.Children.Add(Watch(Row(row), "trees", row.Path, Signature(row), row.Leftover ? Brushes_.Done : Ui.Wait, group.Name));
        return list;
    }

    /// <summary>Lo que, si cambia en un worktree, cuenta como novedad: si se puede cerrar, lo que tiene sin subir y su PR.</summary>
    private static string Signature(WorktreeRow row) => $"{row.Leftover}:{row.LocalText}:{row.Item.PrState}";

    /// <summary>La fila de un worktree: su clave, el título de su tarea y la pastilla que más pide; desplegada, lo demás.</summary>
    private Control Row(WorktreeRow row)
    {
        var id = row.Path;
        var open = _open.Contains(id);
        var line = new DockPanel();
        var folder = Ui.IconButton("", () => Folders.Open(row.Path), DiaryInk.Card("Abrir la carpeta", row.Path));
        folder.IsEnabled = row.CanOpen;
        folder.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(folder, Dock.Right);
        line.Children.Add(folder);
        if (Pressing(row) is { } pill)
        {
            pill.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(pill, Dock.Right);
            line.Children.Add(pill);
        }
        var key = Ui.Text(row.Key ?? row.Folder, 11, Ui.Muted, bold: true, mono: true);
        key.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(key, Dock.Left);
        line.Children.Add(key);
        line.Children.Add(Ui.Text(row.KeyVisible ? row.TaskTitle : row.BranchText ?? "Sin tarea", 12));

        var body = Ui.Column(4, line);
        if (Size != WidgetSize.S && !open)
            body.Children.Add(Ui.Text(string.Join(" · ", new[] { row.StateVisible ? row.StateText : null, row.PrText, row.LocalText }.Where(t => t is not null)), 11, Ui.Faint));
        // Lo que se puede cerrar lleva la orden a mano, sin tener que desplegarlo.
        if (row.Leftover && !open) body.Children.Add(Command(row));
        if (open) body.Children.Add(Unfolded(row));
        return Ui.Card(body, () =>
        {
            if (!_open.Remove(id)) _open.Add(id);
            Render();
        }, stripe: row.RepoBrush, fill: open ? Ui.Section : null);
    }

    /// <summary>La pastilla que más pide de un worktree: lo que tiene sin subir, que ya sobra, su PR o la columna de su tarea.</summary>
    private static Control? Pressing(WorktreeRow row)
    {
        if (row.LocalPillVisible) return Ui.Pill(row.LocalText, row.LocalBrush, row.LocalPillFill);
        if (row.Leftover) return Ui.Pill(row.Item.PrState == PrState.Merged ? "PR mergeada" : "Lista para cerrar", Brushes_.Done, Soft(Brushes_.Done));
        if (row.PrVisible && row.PrText is { } pr) return Ui.Pill(pr, row.PrBrush, row.PrFill);
        if (row.StateVisible) return Ui.Pill(row.StateText, row.StateBrush);
        return null;
    }

    /// <summary>Lo que se ve al desplegar una fila: su tarea, su PR, lo local, lo publicado y, si sobra, la orden que la cierra.</summary>
    private Control Unfolded(WorktreeRow row)
    {
        var list = new StackPanel { Spacing = 5, Margin = new Thickness(0, 4, 0, 0) };
        if (row.KeyVisible)
        {
            var task = Ui.Row(6, Ui.Text("Tarea", 11, Ui.Faint), row.StateVisible ? Ui.Row(4, Ui.Dot(row.StateBrush, 6), Ui.Text(row.StateText, 11.5)) : Ui.Text("sin leer", 11.5, Ui.Faint));
            if (row.Key is { } key) task.Children.Add(Ui.Link("Ver tarea ↗", () => SessionActions.RequestTask(key)));
            list.Children.Add(task);
        }
        if (row.BranchVisible) list.Children.Add(Ui.Row(6, Ui.Text("Rama", 11, Ui.Faint), Ui.Text(row.BranchText, 11.5, mono: true)));
        if (row.PrVisible && row.PrText is { } pr) list.Children.Add(Ui.Row(6, Ui.Text("PR", 11, Ui.Faint), BoardWidget.PrLine(pr, row.PrBrush, row.PrFill, row.PrUrl)));
        list.Children.Add(Ui.Row(6, Ui.Text("Local", 11, Ui.Faint), Ui.Text(row.LocalText, 11.5, row.LocalBrush)));
        if (row.ArtifactsVisible)
            list.Children.Add(Ui.Row(6, Ui.Text("Publicado", 11, Ui.Faint), Ui.Link($"{row.ArtifactsText} ↗", () => SessionActions.RequestArtifacts(row.RepoName, row.Folder))));
        if (row.Leftover)
        {
            list.Children.Add(Ui.Text(row.LeftoverReason, 11, Ui.Muted, wrap: true));
            list.Children.Add(Command(row));
            if (row.CloseWarningVisible) list.Children.Add(Ui.Text(row.CloseWarning, 11, Ui.Wait, wrap: true));
        }
        return list;
    }

    /// <summary>La orden que cierra un worktree, en letra de código, con «Copiar» al lado y dónde pegarla en su ficha.</summary>
    private Control Command(WorktreeRow row)
    {
        var copy = Ui.Link(_copied == row.Path ? "Copiado" : "Copiar", () => Copy(row));
        var command = new DockPanel();
        DockPanel.SetDock(copy, Dock.Right);
        command.Children.Add(copy);
        var text = Ui.Text(row.CloseCommand, 11, mono: true);
        text.Margin = new Thickness(0, 0, 8, 0);
        command.Children.Add(text);
        DiaryInk.Tip(command, DiaryInk.Card(row.CopyTitle, row.CopyWhere));
        return new Border { Background = Ui.Track, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4), Child = command };
    }

    /// <summary>El color dado, muy suave, para el fondo de su pastilla.</summary>
    private static IBrush Soft(IBrush brush) => brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.16) : Ui.Track;

    /// <summary>Copia la orden que cierra un worktree y lo dice un momento en su enlace.</summary>
    private async void Copy(WorktreeRow row)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(row.CloseCommand);
            _copied = row.Path;
            Render();
            DispatcherTimer.RunOnce(() =>
            {
                _copied = null;
                Render();
            }, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("worktrees", ex);
        }
    }
}
