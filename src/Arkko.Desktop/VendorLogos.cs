using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Un logo de una pieza del stack, con la nota de autor o de marca que su licencia pide.</summary>
public sealed record VendorLogo(IImage Image, string Credit);

/// <summary>Los logos de las piezas que su dueño deja usar para referirse a ellas; las demás van con sus letras.</summary>
/// <remarks>
/// Solo entran logos con una licencia o una política de marca que lo permitan, sin cambiar su forma ni sus colores:
/// Git (CC BY 3.0, de Jason Long), Python (política de marca de la PSF) y Node.js (la de la OpenJS Foundation). Los
/// trazos de Git y Node.js son los de sus SVG oficiales; Python va en su PNG oficial, que lleva una sombra con un
/// degradado que en vector cambiaría. Cada uno se dibuja una sola vez.
/// </remarks>
public static class VendorLogos
{
    private static readonly Lazy<VendorLogo?> Git = new(() => Vector(new Rect(0, 0, 78, 78),
        "Logo de Git, de Jason Long, con licencia CC BY 3.0.",
        (Color.Parse("#f03c2e"), GitTransform(),
         "M5,58c-2.76142,0 -5,-2.23858 -5,-5v-48c0,-2.76142 2.23858,-5 5,-5h33v12.54404c-2.06553,0.94801 -3.5,3.03446 -3.5,5.45596c0,0.73514 0.13221,1.43941 0.37415,2.09031l-15.28384,15.28384c-0.6509,-0.24194 -1.35517,-0.37415 -2.09031,-0.37415c-3.31371,0 -6,2.68629 -6,6c0,3.31371 2.68629,6 6,6c3.31371,0 6,-2.68629 6,-6c0,-0.73514 -0.13221,-1.43941 -0.37415,-2.09031l14.87415,-14.87415l0,11.50851c-2.06553,0.94801 -3.5,3.03446 -3.5,5.45596c0,3.31371 2.68629,6 6,6c3.31371,0 6,-2.68629 6,-6c0,-2.42149 -1.43447,-4.50795 -3.5,-5.45596l0,-12.08808c2.06553,-0.94801 3.5,-3.03446 3.5,-5.45596c0,-2.42149 -1.43447,-4.50795 -3.5,-5.45596l0,-12.54404h10c2.76142,0 5,2.23858 5,5v48c0,2.76142 -2.23858,5 -5,5z")));

    private static readonly Lazy<VendorLogo?> Node = new(() => Vector(new Rect(0, 0.5, 71, 79),
        "Node.js y su logo son marcas de la OpenJS Foundation. Arkko no tiene relación con ella.",
        (Color.Parse("#5FA04E"), Matrix.Identity,
         "M35.625 79.5c-1.081 0-2.09-.288-3.028-.792l-9.59-5.686c-1.442-.792-.721-1.08-.289-1.224 1.947-.648 2.308-.792 4.327-1.944.216-.144.504-.072.72.072l7.356 4.391c.288.144.649.144.865 0l28.77-16.628c.289-.144.433-.431.433-.791V23.714c0-.36-.144-.648-.432-.792L35.986 6.366c-.288-.144-.65-.144-.865 0L6.35 22.922c-.29.144-.434.504-.434.792v33.184c0 .287.145.647.433.791l7.86 4.535c4.254 2.16 6.922-.36 6.922-2.879V26.593c0-.432.36-.864.865-.864h3.678c.432 0 .865.36.865.864v32.752c0 5.687-3.1 8.998-8.509 8.998-1.658 0-2.956 0-6.633-1.8l-7.572-4.319A6.073 6.073 0 0 1 .798 56.97V23.786a6.073 6.073 0 0 1 3.028-5.255l28.77-16.628c1.804-1.008 4.255-1.008 6.058 0l28.77 16.628a6.073 6.073 0 0 1 3.029 5.255V56.97a6.073 6.073 0 0 1-3.029 5.254l-28.77 16.628c-.865.36-1.947.648-3.029.648Z"),
        (Color.Parse("#5FA04E"), Matrix.Identity,
         "M44.567 56.682c-12.62 0-15.215-5.759-15.215-10.654 0-.432.36-.864.865-.864h3.75c.433 0 .793.288.793.72.577 3.815 2.235 5.687 9.879 5.687 6.057 0 8.652-1.368 8.652-4.607 0-1.871-.72-3.24-10.167-4.175-7.86-.792-12.762-2.52-12.762-8.782 0-5.83 4.903-9.285 13.123-9.285 9.23 0 13.772 3.167 14.35 10.077 0 .216-.073.432-.217.648-.144.144-.36.288-.577.288h-3.822a.844.844 0 0 1-.793-.648c-.865-3.96-3.1-5.255-9.013-5.255-6.634 0-7.427 2.304-7.427 4.031 0 2.088.937 2.736 9.879 3.887 8.869 1.152 13.05 2.808 13.05 8.998 0 6.335-5.263 9.934-14.348 9.934Z")));

    private static readonly Lazy<VendorLogo?> Python = new(() => Raster("avares://Arkko/Assets/logos/python.png",
        "Python y su logo son marcas de la Python Software Foundation. Arkko no tiene relación con ella."));

    /// <summary>La transformación del trazo de Git en su SVG: «translate(10 10) rotate(-45 29 29)».</summary>
    private static Matrix GitTransform() =>
        Matrix.CreateTranslation(-29, -29) * Matrix.CreateRotation(-Math.PI / 4) * Matrix.CreateTranslation(29, 29) * Matrix.CreateTranslation(10, 10);

    /// <summary>El logo de una comprobación por su id; null si la pieza no tiene uno que se pueda usar.</summary>
    public static VendorLogo? For(StackCheck check) => check.Id.Split(':')[0] switch
    {
        "git" => Git.Value,
        "python" => Python.Value,
        "node" => Node.Value,
        _ => null,
    };

    /// <summary>Dibuja un logo de trazos con sus colores, encuadrado en su caja original para que no cambie de proporción.</summary>
    private static VendorLogo? Vector(Rect box, string credit, params (Color Fill, Matrix Transform, string Path)[] shapes)
    {
        try
        {
            var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(box) };
            // Un rectángulo transparente con la caja entera: así el dibujo mide lo que el SVG y el logo queda centrado.
            group.Children.Add(new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(box) });
            foreach (var (fill, transform, path) in shapes)
            {
                var geometry = Geometry.Parse(path);
                geometry.Transform = new MatrixTransform(transform);
                group.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(fill), Geometry = geometry });
            }
            return new VendorLogo(new DrawingImage(group), credit);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("vendor-logo", ex);
            return null;
        }
    }

    /// <summary>Carga un logo en PNG de los recursos de la app.</summary>
    private static VendorLogo? Raster(string uri, string credit)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return new VendorLogo(new Bitmap(stream), credit);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("vendor-logo", ex);
            return null;
        }
    }
}
