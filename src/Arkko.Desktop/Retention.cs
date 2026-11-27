using System.ComponentModel;

namespace Arkko.Desktop;

/// <summary>Un plazo elegible de una categoría; también un umbral de aviso de los límites.</summary>
public sealed class RetentionOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public RetentionOption(string group, string label, int? days, bool selected)
    {
        Group = group;
        Label = label;
        Days = days;
        _isSelected = selected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Group { get; }
    public string Label { get; }
    public int? Days { get; }

    /// <summary>Se dispara cuando la persona elige este plazo.</summary>
    public event Action? Chosen;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var chosen = value && !_isSelected;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            if (chosen) Chosen?.Invoke();
        }
    }
}

/// <summary>Una categoría de datos con sus plazos.</summary>
/// <param name="Enabled">False si el plazo no se puede cambiar aquí, como uno que fija la empresa.</param>
public sealed record RetentionCategory(string Name, string Detail, IReadOnlyList<RetentionOption> Options, bool Enabled = true)
{
    /// <summary>Plazo elegido; si no hay ninguno marcado, «siempre».</summary>
    public int? Days => Options.FirstOrDefault(o => o.IsSelected)?.Days;

    /// <summary>Monta una categoría con sus plazos; si el guardado no es uno de ellos, se añade tal cual.</summary>
    /// <param name="group">Agrupa sus chips, para que marcar uno desmarque los demás.</param>
    /// <param name="chosen">Se llama cuando la persona elige cualquiera de sus plazos.</param>
    public static RetentionCategory Of(string group, string name, string detail, (string Label, int? Days)[] terms, int? selected,
        Action chosen, bool enabled = true)
    {
        var options = terms.Select(t => new RetentionOption(group, t.Label, t.Days, t.Days == selected)).ToList();
        if (options.All(o => !o.IsSelected) && selected is { } days)
            options.Insert(options.Count - 1, new RetentionOption(group, $"{days} días", days, true));
        foreach (var option in options) option.Chosen += chosen;
        return new RetentionCategory(name, detail, options, enabled);
    }
}
