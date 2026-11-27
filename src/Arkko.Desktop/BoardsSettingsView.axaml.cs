using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Configuración → Tableros: las conexiones con los proveedores de tareas y cómo se reconoce la tarea de una sesión.</summary>
/// <remarks>
/// Todo se guarda al momento, como el resto de la configuración. Una conexión nueva existe desde que se
/// añade, pero no se ofrece en Tareas hasta que su prueba sale bien. Las claves nunca vuelven a la vista:
/// escritas, se cifran; de una variable de entorno, solo se guarda su nombre.
/// </remarks>
public partial class BoardsSettingsView : UserControl
{
    private readonly BoardsModel _model = new(new BoardStore());

    // La clave se guarda medio segundo después de dejar de escribir, como los demás campos.
    private readonly DispatcherTimer _secretDelay = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private (SettingField Field, TextBox Box)? _pendingSecret;

    /// <summary>Crea la vista con la configuración guardada.</summary>
    public BoardsSettingsView()
    {
        InitializeComponent();
        DataContext = _model;
        _secretDelay.Tick += (_, _) => SavePendingSecret();
        SubBoardPicker.Chosen += SubBoardPicker_Chosen;
        Reload();
    }

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    /// <summary>Vuelve a leer la configuración, al entrar en la categoría.</summary>
    public void Reload()
    {
        SavePendingSecret();
        _model.Load();
    }

    /// <summary>Si la pulsación que acaba es del botón principal.</summary>
    private static bool IsLeft(PointerReleasedEventArgs e) => e.InitialPressMouseButton == MouseButton.Left;

    /// <summary>Añade una conexión: con el único perfil, directamente; con varios, se elige en un menú.</summary>
    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        var profiles = _model.Profiles.Select(p => p.Profile).ToList();
        if (profiles.Count == 1)
        {
            _model.Add(profiles[0]);
            return;
        }
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = "¿Con qué perfil?", Classes = { "caption" }, IsHitTestVisible = false });
        foreach (var profile in profiles)
        {
            var item = new MenuItem { Header = profile.Name };
            item.Click += (_, _) => _model.Add(profile);
            menu.Items.Add(item);
        }
        menu.Open((Control)sender!);
    }

    /// <summary>Abre el cuadro para pegar un perfil.</summary>
    private void ImportProfile_Click(object? sender, RoutedEventArgs e) => _model.StartImport();

    /// <summary>Cierra el cuadro de importar sin guardar.</summary>
    private void CancelImport_Click(object? sender, RoutedEventArgs e) => _model.CancelImport();

    /// <summary>Guarda el perfil pegado; si ya hay uno con el mismo id, pregunta antes de sustituirlo.</summary>
    private async void SaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_model.ImportedId is { } id && _model.HasProfile(id) &&
            !await AppDialog.ConfirmAsync(Owner, "¿Sustituir el perfil?",
                $"Ya hay un perfil «{id}». Las conexiones que lo usan pasarán a usar el nuevo.", "Sustituir", DialogTone.Warning))
            return;
        _model.SaveImport();
    }

    /// <summary>Pide un fichero con el selector del sistema; null si se cancela.</summary>
    private async Task<IStorageFile?> PickFile(string title, string kind, params string[] patterns)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(kind) { Patterns = patterns }, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0] : null;
    }

    /// <summary>Lee entero un fichero elegido en el selector.</summary>
    private static async Task<byte[]> ReadAll(IStorageFile file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    /// <summary>Carga en el cuadro un perfil guardado en un fichero.</summary>
    private async void LoadProfileFile_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFile("Perfil de tablero", "Perfiles", "*.json") is not { } file) return;
        try
        {
            _model.ImportText = System.Text.Encoding.UTF8.GetString(await ReadAll(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await AppDialog.ShowAsync(Owner, "No se ha podido leer el fichero", ex.Message);
        }
    }

    /// <summary>Copia un perfil importado, para compartirlo.</summary>
    private void CopyProfile_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ProfileRow row && _model.ProfileText(row.Profile.Id) is { } text) Copy(text);
    }

    /// <summary>Elimina un perfil importado; si alguna conexión lo usa, lo dice y no lo elimina.</summary>
    private async void DeleteProfile_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ProfileRow row) return;
        if (_model.UsedBy(row.Profile.Id) is { Count: > 0 } users)
        {
            await AppDialog.ShowAsync(Owner, "Este perfil está en uso", $"Lo usan {string.Join(", ", users)}. Elimina antes esas conexiones.", DialogTone.Warning);
            return;
        }
        if (!await AppDialog.ConfirmAsync(Owner, $"¿Eliminar el perfil «{row.Name}»?", "Se podrá volver a importar cuando haga falta.", "Eliminar",
                DialogTone.Warning))
            return;
        _model.DeleteProfile(row.Profile.Id);
    }

    /// <summary>Abre o cierra la ayuda de cómo se hace un perfil.</summary>
    private void Help_Click(object? sender, RoutedEventArgs e) => _model.HelpOpen = !_model.HelpOpen;

    /// <summary>Copia la plantilla comentada de un perfil.</summary>
    private void CopyTemplate_Click(object? sender, RoutedEventArgs e)
    {
        Copy(BoardProfileHelp.Template);
        _model.CopyNote = "Plantilla copiada";
    }

    /// <summary>Copia el texto para pedirle el perfil a Claude Code.</summary>
    private void CopyPrompt_Click(object? sender, RoutedEventArgs e)
    {
        Copy(BoardProfileHelp.ClaudePrompt);
        _model.CopyNote = "Texto copiado: añade la documentación de tu API al final";
    }

    /// <summary>Copia un texto al portapapeles; si falla, lo anota sin interrumpir.</summary>
    private async void Copy(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("boards-copy", ex);
        }
    }

    /// <summary>Abre el editor de una conexión.</summary>
    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ConnectionRow row) _model.Edit(row.Connection);
    }

    /// <summary>Elimina una conexión y sus claves, tras confirmarlo.</summary>
    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ConnectionRow row) return;
        if (!await AppDialog.ConfirmAsync(Owner, $"¿Eliminar «{row.Name}»?",
                "Se borran la conexión y su clave guardada. Tus tareas no cambian: la app solo las leía.", "Eliminar", DialogTone.Warning))
            return;
        _model.Delete(row.Connection);
    }

    /// <summary>Cierra el editor y vuelve a las fichas.</summary>
    private void Done_Click(object? sender, RoutedEventArgs e)
    {
        SavePendingSecret();
        _model.CloseEditor();
    }

    // Subtablero al que se le está eligiendo color.
    private BoardChoice? _colorTarget;

    /// <summary>Abre el selector de color de un subtablero, en su color de ahora.</summary>
    private void SubBoardColor_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsLeft(e) || (sender as Control)?.DataContext is not BoardChoice choice) return;
        _colorTarget = choice;
        SubBoardPicker.SetColor(choice.Hex);
        SubBoardColorPopup.PlacementTarget = (Control)sender!;
        SubBoardColorPopup.IsOpen = true;
    }

    private void SubBoardPicker_Chosen(string hex)
    {
        _colorTarget?.SetColor(hex);
        SubBoardColorPopup.IsOpen = false;
    }

    private void SubBoardColorReset_Click(object? sender, RoutedEventArgs e)
    {
        _colorTarget?.SetColor(null);
        SubBoardColorPopup.IsOpen = false;
    }

    /// <summary>Elige el color de la conexión.</summary>
    private void Color_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (IsLeft(e) && (sender as Control)?.DataContext is ColorChoice choice) _model.Editor?.SetColor(choice.Hex);
    }

    /// <summary>Al escribir una clave, la guarda cifrada en cuanto se deja de escribir.</summary>
    private void Secret_Changed(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: SettingField field } box || string.IsNullOrEmpty(box.Text)) return;
        _pendingSecret = (field, box);
        _secretDelay.Stop();
        _secretDelay.Start();
    }

    /// <summary>Guarda la clave que se estaba escribiendo, si hay alguna, y vacía su campo.</summary>
    private void SavePendingSecret()
    {
        _secretDelay.Stop();
        if (_pendingSecret is not { } pending) return;
        _pendingSecret = null;
        if (string.IsNullOrEmpty(pending.Box.Text)) return;
        pending.Field.SaveSecret(pending.Box.Text);
        pending.Box.Text = "";
    }

    /// <summary>Deja escribir otra clave en lugar de la guardada.</summary>
    private void ChangeSecret_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SettingField field) field.Changing = true;
    }

    /// <summary>Prueba la conexión: el certificado del servidor y, si se confía en él, la clave.</summary>
    private async void Test_Click(object? sender, RoutedEventArgs e)
    {
        SavePendingSecret();
        if (_model.Editor is { } editor) await editor.TestAsync();
        _model.RefreshRows();
    }

    /// <summary>Confía en el certificado que ha enseñado el servidor, por su huella, y vuelve a probar.</summary>
    private async void TrustCertificate_Click(object? sender, RoutedEventArgs e)
    {
        if (_model.Editor is not { } editor) return;
        editor.TrustSeenCertificate();
        await editor.TestAsync();
        _model.RefreshRows();
    }

    /// <summary>Confía en un certificado raíz elegido de un fichero, y vuelve a probar.</summary>
    private async void TrustRoot_Click(object? sender, RoutedEventArgs e)
    {
        if (_model.Editor is not { } editor) return;
        if (await PickFile("Certificado raíz de confianza", "Certificados", "*.cer", "*.crt", "*.pem") is not { } file) return;
        try
        {
            editor.TrustRoot(await ReadAll(file), file.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            await AppDialog.ShowAsync(Owner, "No se ha podido usar ese certificado", ex.Message);
            return;
        }
        await editor.TestAsync();
        _model.RefreshRows();
    }

    /// <summary>Deja de confiar en el certificado o la raíz propia de la conexión.</summary>
    private void Untrust_Click(object? sender, RoutedEventArgs e) => _model.Editor?.Untrust();
}
