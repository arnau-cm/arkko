using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Un texto seleccionable con trozos de código que se copian al pulsarlos: el arreglo de una comprobación.</summary>
/// <remarks>El código se pinta siempre como texto, nunca se interpreta: el informe lo marca con comillas invertidas y nada más.</remarks>
public sealed class FixTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<IReadOnlyList<FixPart>?> PartsProperty =
        AvaloniaProperty.Register<FixTextBlock, IReadOnlyList<FixPart>?>(nameof(Parts));

    private const string CopyGlyph = "";
    private const string DoneGlyph = "";

    /// <summary>Los trozos del texto, cada uno de texto o de código.</summary>
    public IReadOnlyList<FixPart>? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    /// <summary>Rehace el texto cuando cambian los trozos.</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PartsProperty) Build();
    }

    /// <summary>Monta el texto: el texto tal cual y cada código en su ficha copiable.</summary>
    private void Build()
    {
        var inlines = new InlineCollection();
        foreach (var part in Parts ?? [])
            inlines.Add(part.IsCode ? new InlineUIContainer(CodeChip(part.Text)) { BaselineAlignment = BaselineAlignment.Center } : new Run(part.Text));
        Inlines = inlines;
    }

    /// <summary>La ficha de un código: en letra de código, con el icono de copiar, que pasa a una marca al copiarlo.</summary>
    private Control CodeChip(string code)
    {
        var glyph = new TextBlock { Text = CopyGlyph, FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        glyph.Classes.Add("icon");
        glyph.Bind(TextBlock.ForegroundProperty, glyph.GetResourceObservable("FgFaint"));
        var text = new TextBlock { Text = code, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.FontFamilyProperty, text.GetResourceObservable("MonoFont"));
        var chip = new Border
        {
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 0),
            Margin = new Thickness(1, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, glyph } },
        };
        chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("TrackFill"));
        chip.Bind(Border.BorderBrushProperty, chip.GetResourceObservable("TrackEdge"));
        ToolTip.SetTip(chip, "Copia solo este código");
        // Copia al pulsar y marca el clic como atendido: si sigue subiendo, el texto seleccionable que rodea la ficha
        // captura el puntero para seleccionar y la ficha no llega a recibir el soltar.
        chip.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
                await clipboard.SetTextAsync(code);
                glyph.Text = DoneGlyph;
                DispatcherTimer.RunOnce(() => glyph.Text = CopyGlyph, TimeSpan.FromSeconds(1.5));
            }
            catch (Exception ex)
            {
                JsonStore.LogError("vulnerabilities-copy", ex);
            }
        };
        return chip;
    }
}
