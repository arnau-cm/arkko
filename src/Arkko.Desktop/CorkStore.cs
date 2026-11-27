using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Los corchos tal como se guardan para el próximo arranque, en <c>data/corks.json</c>.</summary>
internal sealed class CorkState
{
    /// <summary>Los corchos abiertos, en el orden en que se usaron: el último es el que recibe lo que se clava.</summary>
    public List<CorkSaved> Corks { get; set; } = [];
}

/// <summary>Un corcho guardado: su número, dónde estaba su ventana y la forma de sus recuadros.</summary>
internal sealed class CorkSaved
{
    public int Number { get; set; }

    /// <summary>El nombre que le puso la persona; null si se llama por su número.</summary>
    public string? Name { get; set; }

    // Dónde estaba la última vez, con las pantallas que hubiera: lo que se usa con un juego de pantallas sin sitio propio.
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }

    /// <summary>Dónde va con cada juego de pantallas, por su huella (<see cref="Arkko.Core.ScreenSet.Key"/>).</summary>
    public Dictionary<string, CorkPlace>? Places { get; set; }

    /// <summary>La raíz de sus recuadros; null si estaba vacío.</summary>
    public CorkNodeSaved? Root { get; set; }
}

/// <summary>Dónde va la ventana de un corcho con un juego de pantallas: su esquina en píxeles, su tamaño sin maximizar
/// y si estaba maximizada.</summary>
internal sealed class CorkPlace
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}

/// <summary>Un trozo de la forma de un corcho: una división, con su sentido, sus hijos y sus pesos; o un recuadro, con
/// sus paneles y el que se veía.</summary>
internal sealed class CorkNodeSaved
{
    /// <summary>En una división: «horizontal» (uno al lado de otro) o «vertical» (uno debajo de otro); null en un recuadro.</summary>
    public string? Orientation { get; set; }
    public List<CorkNodeSaved>? Children { get; set; }
    public List<double>? Weights { get; set; }
    public List<PieceSaved>? Panes { get; set; }
    public int Selected { get; set; }

    /// <summary>Si es un hueco, un recuadro que se dejó vacío a propósito.</summary>
    public bool? Hole { get; set; }

    /// <summary>En un hueco que dejó un perfil, las partes que espera, a falta de elegir de qué sesión.</summary>
    public List<string>? Want { get; set; }
}

/// <summary>Un panel guardado: lo que enseña, como <see cref="PieceSpec"/>.</summary>
internal sealed class PieceSaved
{
    public string Kind { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Cwd { get; set; }
    public string? Scope { get; set; }
    public string? Path { get; set; }
    public string? Tab { get; set; }

    /// <summary>En el widget de una vista de la app, lo que se eligió en él: su columna, su periodo, sus filtros.</summary>
    public Dictionary<string, string>? State { get; set; }

    /// <summary>Lo guardado de un panel, con lo que guarda de sí mismo su widget, si lo tiene.</summary>
    public static PieceSaved Of(PieceSpec spec, IReadOnlyDictionary<string, string>? state = null) => new()
    {
        Kind = spec.Kind, SessionId = spec.SessionId, Title = spec.Title, Cwd = spec.Cwd, Scope = spec.Scope, Path = spec.Path, Tab = spec.Tab,
        State = state is { Count: > 0 } ? new Dictionary<string, string>(state) : null,
    };

    /// <summary>Lo que enseña el panel guardado; null si no es un panel que se sepa abrir. Una vista retirada abre la que
    /// la sustituye, con su nombre en el título.</summary>
    public PieceSpec? ToSpec()
    {
        if (NativeViews.Is(Kind)) return NativeViews.Spec(Kind) with { Path = Path };
        var kind = PieceSpec.Current(Kind);
        if (!(kind == "file" || PieceSpec.IsPart(kind)) || SessionId.Length == 0) return null;
        var title = Title;
        if (kind != Kind)
        {
            var session = Title.IndexOf(" · ", StringComparison.Ordinal);
            title = (kind == "parte" ? "Parte" : "Diagrama") + (session >= 0 ? Title[session..] : "");
        }
        return new PieceSpec(kind, SessionId, title, Cwd, Scope, Path, Tab);
    }
}

/// <summary>Los perfiles de corcho, en <c>data/cork-profiles.json</c>.</summary>
internal sealed class CorkProfileState
{
    public List<CorkProfile> Profiles { get; set; } = [];
}

/// <summary>Un perfil de corcho: la forma de sus recuadros y qué parte va en cada uno, sin sesión, que se elige al
/// abrirlo; las vistas de la app van tal cual.</summary>
internal sealed class CorkProfile
{
    public string Name { get; set; } = "";
    public CorkNodeSaved? Root { get; set; }
}

/// <summary>Lee y escribe los corchos guardados y sus perfiles.</summary>
internal static class CorkStore
{
    private static string File => System.IO.Path.Combine(AppPaths.Data, "corks.json");
    private static string ProfilesFile => System.IO.Path.Combine(AppPaths.Data, "cork-profiles.json");

    /// <summary>Los perfiles de corcho guardados; ninguno si no hay o el fichero no se lee.</summary>
    public static CorkProfileState LoadProfiles()
    {
        try
        {
            return JsonStore.Read<CorkProfileState>(ProfilesFile) ?? new CorkProfileState();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("cork-profiles", ex);
            return new CorkProfileState();
        }
    }

    /// <summary>Guarda los perfiles de corcho.</summary>
    public static void SaveProfiles(CorkProfileState state)
    {
        try
        {
            JsonStore.Write(ProfilesFile, state);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("cork-profiles", ex);
        }
    }

    /// <summary>Los corchos guardados; vacío si no hay ninguno o el fichero no se lee.</summary>
    public static CorkState Load()
    {
        try
        {
            return JsonStore.Read<CorkState>(File) ?? new CorkState();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("corks", ex);
            return new CorkState();
        }
    }

    /// <summary>Guarda los corchos.</summary>
    public static void Save(CorkState state)
    {
        try
        {
            JsonStore.Write(File, state);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("corks", ex);
        }
    }
}
