using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Cómo se dice y de qué color va lo que se sabe de un worktree, igual en Worktrees y en el Tablero.</summary>
public static class WorktreeTexts
{
    /// <summary>«PR #12 abierta» o «PR #12 mergeada»; null sin PR.</summary>
    public static string? Pr(WorktreeInfo item) =>
        item.Pr is { } n ? $"PR #{n} {(item.PrState == PrState.Merged ? "mergeada" : "abierta")}" : null;

    /// <summary>Color de la PR: morado si está mergeada y azul si sigue abierta.</summary>
    public static IBrush PrBrush(WorktreeInfo? item) => item?.PrState == PrState.Merged
        ? ThemeColors.Of("DataFable", "#C678DD")
        : ThemeColors.Of("DataSonnet", "#61AFEF");

    /// <summary>El fondo de la pastilla de la PR, en su color muy suave.</summary>
    public static IBrush PrFill(WorktreeInfo? item) => item?.PrState == PrState.Merged
        ? ThemeColors.Of("DataFable", "#C678DD", 0.14)
        : ThemeColors.Of("DataSonnet", "#61AFEF", 0.14);

    /// <summary>Lo que tiene sin commit y sin subir: «2 ficheros sin commit · 1 commit sin subir», o que está todo subido.</summary>
    public static string Local(WorktreeInfo item)
    {
        if (item.Missing) return "La carpeta ya no existe";
        var parts = new List<string>();
        if (item.Pending is int pending and > 0) parts.Add(pending == 1 ? "1 fichero sin commit" : $"{pending} ficheros sin commit");
        if (item.Branch is not null && item.Upstream is null && !item.IsBase) parts.Add("rama sin subir");
        else if (item.Ahead is int ahead and > 0) parts.Add(ahead == 1 ? "1 commit sin subir" : $"{ahead} commits sin subir");
        if (parts.Count > 0) return string.Join(" · ", parts);
        return item.Pending is null ? "No se ha podido leer cómo está" : "Todo commiteado y subido";
    }

    /// <summary>Lo local en ámbar si se perdería al cerrarlo, en rojo si la carpeta ya no está, y apagado si está todo subido.</summary>
    public static IBrush LocalBrush(WorktreeInfo item) => item.Missing ? ThemeColors.Of("Danger", "#E03E4C")
        : HasWork(item) ? ThemeColors.Of("DataFiveHour", "#E5C07B")
        : ThemeColors.Of("FgFaint", "#8A8D9C");

    /// <summary>Si tiene algo que se perdería al cerrarlo: cambios sin commit, commits sin subir o una rama sin subir.</summary>
    public static bool HasWork(WorktreeInfo item) =>
        !item.Missing && (item.Pending > 0 || item.Ahead > 0 || (item.Branch is not null && item.Upstream is null && !item.IsBase));

    /// <summary>Por qué sobra; null si no sobra.</summary>
    public static string? Leftover(WorktreeInfo item, TaskState? state)
    {
        if (!item.Leftover(state is TaskState.Done or TaskState.Discarded)) return null;
        if (item.Missing) return "Su carpeta ya no existe, pero git la sigue apuntando";
        if (item.PrState == PrState.Merged) return $"Su PR #{item.Pr} ya está mergeada";
        return state == TaskState.Done ? "Su tarea está hecha" : "Su tarea está descartada";
    }

    /// <summary>La orden que lo cierra, para pegarla en una sesión de Claude o en una terminal.</summary>
    /// <remarks>
    /// En un repositorio con un worktree por rama (.bare), la skill close-worktree, que comprueba el merge y retira
    /// carpeta y rama. En otro, git directamente. Una carpeta que ya no existe no se cierra: se olvida con un prune.
    /// </remarks>
    public static string CloseCommand(RepoWorktrees repo, WorktreeInfo item)
    {
        if (item.Missing) return "git worktree prune";
        return IsBareLayout(repo) ? $"/close-worktree {item.Folder}" : $"git worktree remove \"{item.Path}\"";
    }

    /// <summary>Dónde se pega la orden de cerrar: «Pégalo en una sesión de Claude abierta en develop/ de tienda…».</summary>
    public static string PasteHint(RepoWorktrees repo, WorktreeInfo item)
    {
        var where = item.Missing ? "en una terminal, dentro de cualquier carpeta del repositorio"
            : IsBareLayout(repo) ? $"en una sesión de Claude abierta en {repo.BaseBranch ?? "develop"}/ de {repo.Name}, no en la carpeta que se cierra"
            : "en una terminal, en la carpeta principal del repositorio";
        return $"Pégalo {where}. La app no borra nada.";
    }

    /// <summary>Aviso de lo que se perdería, si cerrarlo no es aún seguro; null si se puede cerrar sin perder nada.</summary>
    public static string? CloseWarning(WorktreeInfo item)
    {
        if (item.Missing) return null;
        if (item.Pending > 0) return "Tiene cambios sin commit: antes de cerrarlo, commitéalos o descártalos.";
        if (item.Ahead > 0) return "Tiene commits sin subir: antes de cerrarlo, súbelos.";
        if (item.Branch is not null && item.Upstream is null && !item.IsBase)
            return "Su rama no se ha subido nunca: mira que no tenga nada que guardar antes de cerrarlo.";
        return null;
    }

    private static bool IsBareLayout(RepoWorktrees repo) => Path.GetFileName(repo.CommonDir).Equals(".bare", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Un worktree en la lista de Worktrees: su carpeta, su tarea, su PR, lo que tiene pendiente y si sobra.</summary>
public sealed class WorktreeRow(RepoWorktrees repo, WorktreeInfo item, string? key, BoardTask? task) : INotifyPropertyChanged
{
    private bool _copied;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorktreeInfo Item => item;
    public string Folder => item.Folder;
    public string Path => item.Path;

    /// <summary>La rama, si no se llama como la carpeta: «rama feature/DEVLAB-3».</summary>
    public string? BranchText => item.Branch is { } b && !b.Equals(item.Folder, StringComparison.OrdinalIgnoreCase) ? $"rama {b}"
        : item.Branch is null && !item.Missing ? "sin rama: en un commit suelto" : null;
    public bool BranchVisible => BranchText is not null;

    public bool IsBase => item.IsBase;
    public string BaseTip => "Rama base: no se cierra nunca";

    // Su tarea, si su carpeta lleva una clave: la clave siempre y, si algún tablero leído la tiene, su título y su columna.
    public string? Key => key;
    public bool KeyVisible => key is not null;
    public string TaskTitle => task?.Title ?? (key is null ? "" : "No está en los tableros leídos");
    public bool StateVisible => task is not null;
    public string StateText => task is null ? "" : TaskStates.Name(task.State);
    public IBrush StateBrush => task is null ? Brushes.Transparent : TaskStates.ColorOf(task.State);

    public string? PrText => WorktreeTexts.Pr(item);
    public bool PrVisible => item.Pr is not null;

    /// <summary>La página web de su PR; null si no tiene o no se sabe cómo es la web de su remoto.</summary>
    public string? PrUrl => item.Pr is { } n ? repo.PullUrl(n) : null;
    public IBrush PrBrush => WorktreeTexts.PrBrush(item);
    public IBrush PrFill => WorktreeTexts.PrFill(item);

    public string LocalText => WorktreeTexts.Local(item);

    public IBrush LocalBrush => WorktreeTexts.LocalBrush(item);

    // Lo local como pastilla cuando importa (algo que se perdería, o la carpeta borrada); si está todo subido, discreto.
    public bool LocalPillVisible => item.Missing || WorktreeTexts.HasWork(item);
    public IBrush LocalPillFill => item.Missing ? ThemeColors.Of("Danger", "#E03E4C", 0.14) : ThemeColors.Of("DataFiveHour", "#E5C07B", 0.14);
    public bool CleanVisible => !LocalPillVisible;

    /// <summary>El color de su repositorio, para la raya de la fila.</summary>
    public IBrush RepoBrush => RepoColors.Of(repo.Name);

    public string? LeftoverReason { get; } = WorktreeTexts.Leftover(item, task?.State);
    public bool Leftover => LeftoverReason is not null;

    /// <summary>La pastilla de que se puede cerrar, con el motivo corto: «Se puede cerrar · PR mergeada».</summary>
    public string LeftoverPill => "Se puede cerrar · " + (item.Missing ? "carpeta borrada"
        : item.PrState == PrState.Merged ? "PR mergeada"
        : task?.State == TaskState.Done ? "tarea hecha" : "tarea descartada");
    public string CloseCommand => WorktreeTexts.CloseCommand(repo, item);
    public string? CloseWarning => Leftover ? WorktreeTexts.CloseWarning(item) : null;
    public bool CloseWarningVisible => CloseWarning is not null;

    /// <summary>Ficha del botón de copiar: qué copia, dónde se pega y, si lo hay, qué se perdería.</summary>
    public string CopyTitle => $"Copiar «{CloseCommand}»";
    public string CopyWhere => WorktreeTexts.PasteHint(repo, item);

    /// <summary>El texto del botón de copiar: «Copiado» un momento después de pulsarlo.</summary>
    public string CopyText => _copied ? "Copiado" : "Copiar cierre";

    public bool Copied
    {
        get => _copied;
        set
        {
            if (_copied == value) return;
            _copied = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CopyText)));
        }
    }

    public bool CanOpen => !item.Missing;

    /// <summary>El repositorio, para pedir lo publicado desde esta carpeta.</summary>
    public string RepoName => repo.Name;

    // Lo publicado en claude.ai desde esta carpeta, que no se ha quitado de la lista.
    private readonly int _artifacts = ArtifactMonitor.Instance.Items.Count(a => !a.Removed && !a.Deleted &&
        string.Equals(a.Group, repo.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Project, item.Folder, StringComparison.OrdinalIgnoreCase));

    /// <summary>«2 artifacts»: lo publicado desde esta carpeta.</summary>
    public string ArtifactsText => ArtifactTexts.Count(_artifacts);
    public bool ArtifactsVisible => _artifacts > 0;
}

/// <summary>Un repositorio en la lista de Worktrees: su color, sus ramas base aparte, sus worktrees de trabajo y si no se ha podido preguntar al remoto.</summary>
public sealed record WorktreeGroup(string Name, string Summary, string? RemoteError, List<WorktreeRow> Bases, List<WorktreeRow> Rows)
{
    /// <summary>El color de la franja de la cabecera, con el nombre en blanco encima.</summary>
    public IBrush Brush => RepoColors.Band(RepoColors.Of(Name));

    /// <summary>El nombre del repositorio tal como se enseña en la cabecera: en mayúsculas, como en Sesiones.</summary>
    public string Title => Name.ToUpperInvariant();

    public bool BasesVisible => Bases.Count > 0;
    public bool RowsVisible => Rows.Count > 0;
    public bool NoRowsVisible => Rows.Count == 0;
    public bool RemoteErrorVisible => RemoteError is not null;
    public string RemoteErrorText => $"No se sabe la PR de sus ramas: {RemoteError}";
}

/// <summary>Estado de la sección Worktrees: los repositorios con sus worktrees, cuántos sobran y si solo se ven esos.</summary>
public sealed class WorktreesModel : INotifyPropertyChanged
{
    private readonly WorktreeMonitor _monitor = WorktreeMonitor.Instance;
    private bool _onlyLeftover;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WorktreeGroup> Groups { get; } = [];

    /// <summary>Solo los worktrees que se pueden cerrar.</summary>
    public bool OnlyLeftover
    {
        get => _onlyLeftover;
        set
        {
            if (_onlyLeftover == value) return;
            _onlyLeftover = value;
            OnChanged();
            Arrange();
        }
    }

    private int _total;
    private int _leftover;
    private int _repos;

    /// <summary>«14 worktrees en 3 repositorios».</summary>
    public string Summary => _total == 0 ? "" :
        $"{_total} {(_total == 1 ? "worktree" : "worktrees")} en {_repos} {(_repos == 1 ? "repositorio" : "repositorios")}";

    /// <summary>El filtro con cuántos se pueden cerrar: «Se pueden cerrar · 2».</summary>
    public string FilterText => _leftover == 0 ? "Se pueden cerrar" : $"Se pueden cerrar · {_leftover}";

    /// <summary>Sin ninguno que se pueda cerrar, el filtro no se pulsa: solo dejaría la lista vacía.</summary>
    public bool CanFilter => _leftover > 0;

    /// <summary>Lo que dice la ficha del filtro bajo su título: cuántos hay ahora.</summary>
    public string FilterNow => _leftover switch
    {
        0 => "Ahora no se puede cerrar ninguno.",
        1 => "Ahora se puede cerrar 1.",
        var n => $"Ahora se pueden cerrar {n}.",
    };

    public string UpdatedText => _monitor.AskingRemote ? "Preguntando por las PR…"
        : _monitor.Reading ? "Leyendo…"
        : _monitor.ReadUtc is { } at ? $"Actualizado {TasksModel.Ago(at)}" : "";
    public bool CanRefresh => !_monitor.Reading;

    /// <summary>La primera lectura aún no ha acabado: se ve la rueda en lugar de la lista.</summary>
    public bool LoadingVisible => _monitor.ReadUtc is null;
    public bool ListVisible => !LoadingVisible && Groups.Count > 0;
    public bool EmptyVisible => !LoadingVisible && Groups.Count == 0;
    public string EmptyText => "Ningún repositorio de tus sesiones tiene worktrees enlazados.";

    /// <summary>Rehace la lista con lo último leído, las tareas de los tableros y el filtro.</summary>
    /// <remarks>Si ya no queda ninguno que se pueda cerrar, el filtro se apaga solo: con él, la lista quedaría vacía.</remarks>
    public void Arrange()
    {
        var settings = TaskDirectory.Instance.Settings;
        var repos = _monitor.Repos.Select(repo => (Repo: repo, Rows: repo.Worktrees.Select(w =>
        {
            var (key, task) = TaskOf(settings, w);
            return new WorktreeRow(repo, w, key, task);
        }).ToList())).ToList();
        _repos = repos.Count;
        _total = repos.Sum(r => r.Rows.Count);
        _leftover = repos.Sum(r => r.Rows.Count(row => row.Leftover));
        if (_leftover == 0 && _onlyLeftover)
        {
            _onlyLeftover = false;
            OnChanged(nameof(OnlyLeftover));
        }

        Groups.Clear();
        foreach (var (repo, rows) in repos)
        {
            // Las ramas base van aparte, en una línea: no se cierran nunca, y con el filtro no se ven.
            var bases = _onlyLeftover ? [] : rows.Where(r => r.IsBase).ToList();
            var work = rows.Where(r => !r.IsBase && (!_onlyLeftover || r.Leftover)).ToList();
            if (bases.Count == 0 && work.Count == 0) continue;
            var closeable = rows.Count(r => r.Leftover);
            var tasks = rows.Count(r => !r.IsBase);
            // Sin ninguno de trabajo, la cabecera calla: ya lo dice el cuerpo de la tarjeta.
            var summary = (tasks == 0 ? "" : tasks == 1 ? "1 worktree de trabajo" : $"{tasks} worktrees de trabajo") +
                          (closeable == 0 ? "" : closeable == 1 ? " · 1 se puede cerrar" : $" · {closeable} se pueden cerrar");
            Groups.Add(new WorktreeGroup(repo.Name, summary, repo.RemoteError, bases, work));
        }
        foreach (var name in new[] { nameof(Summary), nameof(FilterText), nameof(CanFilter), nameof(FilterNow), nameof(UpdatedText), nameof(CanRefresh),
                     nameof(LoadingVisible), nameof(ListVisible), nameof(EmptyVisible), nameof(EmptyText) })
            OnChanged(name);
    }

    /// <summary>Cuántos worktrees se pueden cerrar según lo último leído; null si aún no se ha leído.</summary>
    public static int? LeftoverCount()
    {
        var monitor = WorktreeMonitor.Instance;
        if (monitor.ReadUtc is null) return null;
        var settings = TaskDirectory.Instance.Settings;
        return monitor.Repos.SelectMany(r => r.Worktrees)
            .Count(w => w.Leftover(TaskOf(settings, w).Task?.State is TaskState.Done or TaskState.Discarded));
    }

    /// <summary>La clave de la tarea de un worktree, por el nombre de su carpeta, y la tarea si algún tablero leído la tiene.</summary>
    private static (string? Key, BoardTask? Task) TaskOf(BoardSettings settings, WorktreeInfo item)
    {
        var key = item.IsBase ? null : TaskLinks.KeyOf(settings, item.Folder);
        return (key, key is null ? null : TaskDirectory.Instance.Find(key)?.Task);
    }

    /// <summary>Pone al día el «Actualizado hace…» y si se puede volver a leer.</summary>
    public void Tick()
    {
        OnChanged(nameof(UpdatedText));
        OnChanged(nameof(CanRefresh));
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
