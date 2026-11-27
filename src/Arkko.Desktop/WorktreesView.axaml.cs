using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Sección Worktrees: los worktrees de los repositorios de las sesiones, con su estado, su tarea, su PR y si sobran.</summary>
/// <remarks>Solo lee y sugiere: cerrar uno es copiar la orden y pegarla donde toca. La app no borra nada.</remarks>
public partial class WorktreesView : UserControl
{
    private readonly WorktreesModel _model = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _active;

    /// <summary>Pide abrir una tarea en el Tablero, por su clave.</summary>
    public event Action<string>? TaskRequested;

    /// <summary>Crea la sección; no lee nada hasta que se entra en ella.</summary>
    public WorktreesView()
    {
        InitializeComponent();
        DataContext = _model;
        _clock.Tick += (_, _) => _model.Tick();
        // Los avisos de la app se escuchan mientras la vista está en una ventana: puede haber varias copias (el panel y los
        // corchos), y la que se cierra no debe quedarse viva escuchándolos.
        AttachedToVisualTree += (_, _) =>
        {
            WorktreeMonitor.Instance.Changed += OnChanged;
            TaskDirectory.Instance.Changed += OnChanged;
            RepoColors.Changed += OnChanged;
            ArtifactMonitor.Instance.Changed += OnChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            WorktreeMonitor.Instance.Changed -= OnChanged;
            TaskDirectory.Instance.Changed -= OnChanged;
            RepoColors.Changed -= OnChanged;
            ArtifactMonitor.Instance.Changed -= OnChanged;
        };
    }

    /// <summary>Ha cambiado algo de lo que se enseña (los worktrees, las tareas o los colores): se recoloca si se ve.</summary>
    private void OnChanged()
    {
        if (_active) _model.Arrange();
    }

    /// <summary>Al entrar en la sección: enseña lo último leído y vuelve a mirar si tiene más de cinco minutos.</summary>
    public async void Activate()
    {
        _active = true;
        _model.Arrange();
        _clock.Start();
        await WorktreeMonitor.Instance.RefreshAsync(WorktreeMonitor.Stale);
    }

    /// <summary>Al salir de la sección o esconder el panel.</summary>
    public void Deactivate()
    {
        _active = false;
        _clock.Stop();
    }

    /// <summary>Vuelve a mirar todo, preguntando también al remoto.</summary>
    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await WorktreeMonitor.Instance.RefreshAsync(fresh: true);

    /// <summary>Copia la orden que cierra el worktree y lo dice un momento en el botón.</summary>
    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not WorktreeRow row) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(row.CloseCommand);
            row.Copied = true;
            DispatcherTimer.RunOnce(() => row.Copied = false, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            JsonStore.LogError("worktrees", ex);
        }
    }

    /// <summary>Abre la carpeta del worktree en el explorador de ficheros.</summary>
    private void Open_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is WorktreeRow { CanOpen: true } row) Folders.Open(row.Path);
    }

    /// <summary>Abre la carpeta de una rama base al pulsar su pastilla.</summary>
    private void OpenBase_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && (sender as Control)?.DataContext is WorktreeRow { CanOpen: true } row) Folders.Open(row.Path);
    }

    /// <summary>Abre Artifacts con lo publicado desde la carpeta del worktree.</summary>
    private void Artifacts_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && (sender as Control)?.DataContext is WorktreeRow row)
            SessionActions.RequestArtifacts(row.RepoName, row.Folder);
    }

    /// <summary>Abre la tarea del worktree en el Tablero.</summary>
    private void Task_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && (sender as Control)?.DataContext is WorktreeRow { Key: { } key }) TaskRequested?.Invoke(key);
    }
}
