using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Arkko.Desktop;

/// <summary>Dibuja una ventana a un PNG sin pasar por la pantalla, para revisar su aspecto.</summary>
/// <remarks>
/// Solo actúa con la variable AGENTPULSE_SNAPSHOT, que dice en qué carpeta dejar las imágenes. Una captura de
/// pantalla recogería también lo que haya detrás o encima de la ventana; esto solo dibuja la ventana.
/// </remarks>
internal static class Snapshot
{
    /// <summary>Carpeta donde dejar las imágenes; null si no se han pedido.</summary>
    public static string? Folder => Environment.GetEnvironmentVariable("AGENTPULSE_SNAPSHOT") is { Length: > 0 } dir ? dir : null;

    /// <summary>Guarda la ventana poco después de abrirse, con un nombre, si se han pedido imágenes.</summary>
    public static void WhenOpened(Window window, string name)
    {
        if (Folder is null) return;
        window.Opened += async (_, _) =>
        {
            await Task.Delay(1500);
            // Se dibuja el contenido y no la ventana: la ventana entera no llega a pintarse en el bitmap.
            Save(window.Content as Visual ?? window, name);
        };
    }

    /// <summary>Pasos del recorrido pedido en AGENTPULSE_TOUR, separados por comas («stats,config:4»); vacío si no hay.</summary>
    public static IReadOnlyList<string> Tour =>
        Folder is not null && Environment.GetEnvironmentVariable("AGENTPULSE_TOUR") is { Length: > 0 } tour
            ? tour.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

    /// <summary>Recorre unos pasos tras abrirse la ventana: en cada uno, lo pone a la vista y guarda su imagen.</summary>
    /// <param name="show">Pone a la vista un paso; devuelve false si no lo conoce.</param>
    public static void RunTour(Window window, Func<string, bool> show)
    {
        var steps = Tour;
        if (steps.Count == 0) return;
        window.Opened += async (_, _) =>
        {
            foreach (var step in steps)
            {
                // «wait:5000» espera esos milisegundos antes del paso siguiente, sin guardar imagen.
                if (step.StartsWith("wait:", StringComparison.Ordinal) && int.TryParse(step[5..], out var ms))
                {
                    await Task.Delay(ms);
                    continue;
                }
                Directory.CreateDirectory(Folder!);
                File.AppendAllText(Path.Combine(Folder!, "tour-steps.txt"), $"{DateTime.Now:HH:mm:ss.f} {step}\n");
                if (!show(step)) continue;
                await Task.Delay(1200);
                Save(window.Content as Visual ?? window, "tour-" + step.Replace(':', '-'));
            }
        };
    }

    /// <summary>Dibuja un elemento a un PNG de la carpeta de imágenes, al doble de resolución para que se lea bien.</summary>
    public static void Save(Visual visual, string name)
    {
        if (Folder is not { } folder) return;
        try
        {
            Directory.CreateDirectory(folder);
            var size = visual.Bounds.Size;
            var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * 2)), Math.Max(1, (int)Math.Ceiling(size.Height * 2)));
            using var bitmap = new RenderTargetBitmap(pixels, new Vector(192, 192));
            // Con el fondo de la ventana debajo, como en pantalla: sobre transparente, el suavizado de la letra deja halos.
            var panel = visual as Panel;
            var previous = panel?.Background;
            if (panel is { Background: null } && TopLevel.GetTopLevel(visual) is Window { Background: { } background }) panel.Background = background;
            bitmap.Render(visual);
            if (panel is not null) panel.Background = previous;
            bitmap.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
        }
        catch (Exception ex)
        {
            Arkko.Core.JsonStore.LogError("snapshot", ex);
        }
    }
}
