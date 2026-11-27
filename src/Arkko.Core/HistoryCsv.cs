namespace Arkko.Core;

/// <summary>Exporta el histórico de prompts a CSV, pensado para abrirlo con Excel en castellano.</summary>
/// <remarks>
/// Separado por «;» y en UTF-8 con BOM: es lo que Excel en castellano abre bien a la primera, con
/// columnas y acentos. El texto de los prompts no se exporta: el CSV se comparte para informes y
/// un prompt puede llevar datos que no deben salir del equipo.
/// </remarks>
public static class HistoryCsv
{
    private static readonly string[] Header =
    [
        "Fecha", "Hora", "Proyecto", "Worktree", "Sesión", "Nombre de la sesión", "Modelo", "Tokens nuevos",
        "Entrada", "Salida", "Escritura en caché", "Lectura de caché", "Llamadas a la API", "Duración del prompt (s)",
    ];

    /// <summary>Una fila por prompt y modelo, de la más antigua a la más reciente.</summary>
    public static string Build(IEnumerable<PromptRecord> records)
    {
        var lines = new List<string> { Row(Header) };
        foreach (var r in records.OrderBy(r => r.WhenUtc))
        {
            var local = r.WhenUtc.ToLocalTime();
            var first = true;
            foreach (var (model, t) in r.Usage.ByModel.OrderByDescending(m => PromptSummary.NewTokens(m.Value)))
            {
                lines.Add(Row(
                [
                    local.ToString("yyyy-MM-dd"), local.ToString("HH:mm:ss"), r.Project is null ? "" : History.ProjectOf(r), History.WorktreeOf(r) ?? "", r.SessionId, r.SessionName ?? "",
                    Format.ModelName(model), PromptSummary.NewTokens(t).ToString(), t.Input.ToString(), t.Output.ToString(),
                    t.CacheWrite.ToString(), t.CacheRead.ToString(), t.ApiCalls.ToString(),
                    // La duración es del prompt entero: va solo en la primera fila, para que sumar la columna no la repita.
                    first ? (r.Usage.DurationMs / 1000.0).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("es-ES")) : "",
                ]));
                first = false;
            }
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Guarda el CSV en UTF-8 con BOM.</summary>
    public static void Write(string path, IEnumerable<PromptRecord> records) =>
        File.WriteAllText(path, Build(records), new System.Text.UTF8Encoding(true));

    /// <summary>Une los campos con «;», entre comillas los que llevan separador, comillas o saltos de línea.</summary>
    private static string Row(IEnumerable<string> fields) => string.Join(";", fields.Select(Quote));

    /// <summary>Entrecomilla un campo si hace falta, doblando sus comillas.</summary>
    private static string Quote(string field) =>
        field.IndexOfAny([';', '"', '\r', '\n']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
}
