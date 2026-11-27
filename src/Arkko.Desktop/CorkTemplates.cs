namespace Arkko.Desktop;

/// <summary>Las plantillas de corcho que vienen con la app: composiciones ya montadas para seguir las sesiones en directo,
/// que se abren como un perfil y se pueden guardar como perfil propio después de tocarlas.</summary>
/// <remarks>
/// Los pesos son los de las composiciones del corcho en directo. Las partes de una sesión dejan huecos que esperan, como en
/// un perfil, y se llenan todos con una sesión desde la tecla de cualquiera de ellos.
/// </remarks>
internal static class CorkTemplates
{
    // Los carriles de «Una columna por sesión»: uno por sesión abierta, por orden de apertura.
    private const int Lanes = 5;

    /// <summary>Las plantillas, en el orden del menú, con lo que llevan dicho en corto.</summary>
    public static IReadOnlyList<(CorkProfile Profile, string Hint)> All { get; } =
    [
        (new CorkProfile
        {
            Name = "Directo · Lo que me toca",
            Root = Row(
                (1.35, Column((1.2, View("tetoca")), (1, View("avisos")))),
                (1.65, Column((0.8, View("pulso")), (1.2, View("todas")))),
                (1, Column((1, View("clocks")), (1, View("board", "worktrees", "diary"))))),
        }, "Te toca, Avisos, Pulso, Ahora, Relojes y Tablero"),
        (new CorkProfile
        {
            Name = "Directo · Una columna por sesión",
            Root = Row([.. Enumerable.Range(1, Lanes).Select(i => (1.0, Lane(i))), (0.8, Column((1, View("avisos")), (1, View("pulso"))))]),
        }, $"{Lanes} carriles, Avisos y Pulso"),
        (new CorkProfile
        {
            Name = "Directo · Mapa y detalle",
            Root = Row(
                (1, Column((1, View("clocks")), (1.2, View("tetoca")), (0.55, View("avisos")))),
                (2, Column((0.9, Row((1.2, Part("diagram")), (0.8, Part("parte")))), (1.1, Row((1.2, Part("live")), (0.8, Part("changes"))))))),
        }, "Relojes, Te toca, Avisos y una sesión a fondo"),
        (new CorkProfile
        {
            Name = "Lo que me toca",
            Root = Row((1.2, View("tetoca")), (1, Column((0.8, View("avisos")), (1.2, View("pulso"))))),
        }, "Te toca, Avisos y Pulso"),
        (new CorkProfile
        {
            Name = "Planificar",
            Root = Row((1.5, View("board")), (1, Column((1, View("tetoca")), (1, View("worktrees")))), (1, View("diary"))),
        }, "Tablero, Worktrees, Diario y Te toca"),
    ];

    /// <summary>Devuelve una división con sus hijos uno al lado de otro, cada uno con su peso.</summary>
    private static CorkNodeSaved Row(params (double Weight, CorkNodeSaved Node)[] children) => Split("horizontal", children);

    /// <summary>Devuelve una división con sus hijos uno debajo de otro, cada uno con su peso.</summary>
    private static CorkNodeSaved Column(params (double Weight, CorkNodeSaved Node)[] children) => Split("vertical", children);

    /// <summary>Devuelve una división en un sentido, con sus hijos y sus pesos.</summary>
    private static CorkNodeSaved Split(string orientation, (double Weight, CorkNodeSaved Node)[] children) => new()
    {
        Orientation = orientation, Children = children.Select(c => c.Node).ToList(), Weights = children.Select(c => c.Weight).ToList(),
    };

    /// <summary>Devuelve un recuadro con vistas de la app; con varias, en pestañas, la primera a la vista.</summary>
    private static CorkNodeSaved View(params string[] kinds) => new()
    {
        Panes = kinds.Select(k => new PieceSaved { Kind = k, Title = NativeViews.NameOf(k) }).ToList(),
    };

    /// <summary>Devuelve un recuadro con una parte de una sesión, que al abrirse queda como hueco que la espera.</summary>
    private static CorkNodeSaved Part(string kind) => new() { Panes = [new PieceSaved { Kind = kind }] };

    /// <summary>Devuelve un recuadro con el carril que sigue a la sesión de una posición; cada uno, una copia aparte.</summary>
    private static CorkNodeSaved Lane(int slot) => new()
    {
        Panes =
        [
            new PieceSaved
            {
                Kind = "carril", Title = NativeViews.NameOf("carril"), Path = slot == 1 ? null : $"carril{slot}",
                State = slot == 1 ? null : new Dictionary<string, string> { ["slot"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            },
        ],
    };
}
