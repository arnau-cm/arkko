using System.Net;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>Convierte el texto de una descripción, en Markdown o en HTML sencillo, en controles con formato.</summary>
/// <remarks>
/// Entiende lo que suele llevar la descripción de una tarea: títulos, párrafos, listas (también de casillas),
/// citas, bloques de código, separadores, tablas como texto fijo, negrita, cursiva, código y enlaces. El HTML
/// se pasa antes a Markdown, para tener un solo camino. Los enlaces de una descripción solo abren direcciones https.
/// </remarks>
public static class MarkdownView
{
    private static readonly Regex Inline = new(
        @"\*\*(?<b>.+?)\*\*|__(?<b>.+?)__|(?<![\w*])\*(?<i>[^*\s][^*]*?)\*(?!\w)|(?<!\w)_(?<i>[^_\s][^_]*?)_(?!\w)|`(?<c>[^`]+)`" +
        @"|\[(?<t>[^\]]+)\]\((?<u>[^)\s]+)\)|(?<a>https?://[^\s<>()]+[^\s<>().,;:!?])",
        RegexOptions.Compiled);

    private static FontFamily Mono => Application.Current?.FindResource("MonoFont") as FontFamily ?? new FontFamily("Cascadia Mono, Consolas, monospace");

    /// <summary>Los bloques de la descripción, uno debajo de otro; con el texto vacío, un panel vacío.</summary>
    public static Control Render(string? text)
    {
        var panel = new StackPanel();
        if (string.IsNullOrWhiteSpace(text)) return panel;
        var markdown = LooksLikeHtml(text) ? FromHtml(text) : text;
        foreach (var block in Blocks(markdown.Replace("\r\n", "\n").Split('\n'))) panel.Children.Add(block);
        return panel;
    }

    /// <summary>Pone en un texto corto, como un paso de una lista, su negrita, cursiva, código y enlaces; acepta también HTML sencillo.</summary>
    /// <remarks>
    /// Los saltos de línea se quedan como tales: no hay títulos, listas ni bloques. Es para texto que escribe la
    /// persona, así que también abre enlaces http, como los de un servidor interno.
    /// </remarks>
    public static void Line(InlineCollection inlines, string? text)
    {
        inlines.Clear();
        if (string.IsNullOrEmpty(text)) return;
        AddInlines(inlines, LooksLikeHtml(text) ? FromHtml(text) : text.Replace("\r\n", "\n"), allowHttp: true);
    }

    private static bool LooksLikeHtml(string text) =>
        Regex.IsMatch(text, @"<\s*/?\s*(p|br|div|ul|ol|li|h[1-6]|strong|em|b|i|code|pre|a|span|blockquote|table)\b", RegexOptions.IgnoreCase);

    /// <summary>Pasa HTML sencillo a Markdown: lo que no entiende lo quita y deja su texto.</summary>
    internal static string FromHtml(string html)
    {
        var s = Regex.Replace(html, @"\r?\n", " ");
        s = Regex.Replace(s, @"<\s*pre[^>]*>\s*(<\s*code[^>]*>)?", "\n```\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(<\s*/\s*code\s*>)?\s*<\s*/\s*pre\s*>", "\n```\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*h([1-6])[^>]*>", m => "\n\n" + new string('#', int.Parse(m.Groups[1].Value)) + " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*/\s*h[1-6]\s*>", "\n\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*blockquote[^>]*>", "\n> ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*/\s*(p|div|ul|ol|blockquote|tr|table)\s*>", "\n\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*(strong|b)\b[^>]*>|<\s*/\s*(strong|b)\s*>", "**", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*(em|i)\b[^>]*>|<\s*/\s*(em|i)\s*>", "*", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*code[^>]*>|<\s*/\s*code\s*>", "`", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*a\b[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)<\s*/\s*a\s*>", "[$2]($1)", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t]+\n", "\n");
        return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
    }

    /// <summary>Qué marca lleva una lista: viñeta, número o ninguna, en las de casillas.</summary>
    private enum Marker { Disc, Decimal, None }

    /// <summary>Los bloques del documento, línea a línea.</summary>
    private static IEnumerable<Control> Blocks(string[] lines)
    {
        var paragraph = new List<string>();
        StackPanel? list = null;
        var kind = Marker.Disc;
        var number = 0;

        IEnumerable<Control> Flush()
        {
            if (paragraph.Count > 0)
            {
                yield return Paragraph(paragraph);
                paragraph.Clear();
            }
            if (list is not null)
            {
                yield return list;
                list = null;
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                foreach (var b in Flush()) yield return b;
                var code = new List<string>();
                while (++i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal)) code.Add(lines[i]);
                yield return Code(code);
                continue;
            }
            if (trimmed.Length == 0)
            {
                foreach (var b in Flush()) yield return b;
                continue;
            }
            if (Regex.Match(trimmed, @"^(#{1,6})\s+(.*)$") is { Success: true } h)
            {
                foreach (var b in Flush()) yield return b;
                // Un título hecho solo de guiones es un separador puesto a mano.
                yield return IsRule(h.Groups[2].Value) ? Rule() : Heading(h.Groups[1].Length, h.Groups[2].Value.TrimEnd('#', ' '));
                continue;
            }
            // Una línea solo de guiones o iguales: subraya el título de encima si es una sola línea, o separa.
            if (IsRule(trimmed))
            {
                if (paragraph.Count == 1 && trimmed[0] is '-' or '=')
                {
                    var title = paragraph[0];
                    paragraph.Clear();
                    yield return Heading(trimmed[0] == '=' ? 1 : 2, title);
                    continue;
                }
                foreach (var b in Flush()) yield return b;
                yield return Rule();
                continue;
            }
            if (Regex.Match(line, @"^(\s*)([-*+]|\d+[.)])\s+(.*)$") is { Success: true } item)
            {
                // Una lista de casillas no lleva viñeta: la casilla ya hace de marca.
                var itemKind = char.IsDigit(item.Groups[2].Value[0]) ? Marker.Decimal
                    : Regex.IsMatch(item.Groups[3].Value, @"^\[[ xX]\]\s") ? Marker.None
                    : Marker.Disc;
                if (paragraph.Count > 0) foreach (var b in Flush()) yield return b;
                if (list is not null && kind != itemKind) foreach (var b in Flush()) yield return b;
                kind = itemKind;
                if (list is null)
                {
                    list = new StackPanel { Margin = new Thickness(itemKind == Marker.None ? 2 : 4, 0, 0, 8) };
                    number = 0;
                }
                var indent = item.Groups[1].Value.Replace("\t", "    ").Length;
                list.Children.Add(ListItem(item.Groups[3].Value, indent, itemKind, ++number));
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                foreach (var b in Flush()) yield return b;
                var quote = new List<string> { trimmed.TrimStart('>').TrimStart() };
                while (i + 1 < lines.Length && lines[i + 1].Trim().StartsWith('>')) quote.Add(lines[++i].Trim().TrimStart('>').TrimStart());
                yield return Quote(quote);
                continue;
            }
            if (trimmed.StartsWith('|') && trimmed.EndsWith('|'))
            {
                foreach (var b in Flush()) yield return b;
                var rows = new List<string> { trimmed };
                while (i + 1 < lines.Length && lines[i + 1].Trim() is var next && next.StartsWith('|')) rows.Add(lines[++i].Trim());
                yield return Code(rows.Where(r => !Regex.IsMatch(r, @"^\|[\s:|-]+\|$")).ToList());
                continue;
            }
            if (list is not null) foreach (var b in Flush()) yield return b;
            paragraph.Add(trimmed);
        }
        foreach (var b in Flush()) yield return b;
    }

    private static bool IsRule(string text) => Regex.IsMatch(Regex.Replace(text, @"\s", ""), @"^[\p{Pd}─-╿−=_*]{3,}$");

    /// <summary>Un bloque de texto seleccionable, con el tamaño y el interlineado de la descripción.</summary>
    private static SelectableTextBlock Text(Thickness margin) => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        LineHeight = 18,
        Margin = margin,
        Inlines = [],
    };

    // Los saltos de línea dentro de un párrafo se respetan: en las descripciones de un panel, casi siempre son a propósito.
    private static Control Paragraph(List<string> lines)
    {
        var p = Text(new Thickness(0, 0, 0, 10));
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) p.Inlines!.Add(new LineBreak());
            AddInlines(p.Inlines!, lines[i]);
        }
        return p;
    }

    /// <summary>Un elemento de lista: su marca a la izquierda y su texto, con sangría si va anidado.</summary>
    private static Control ListItem(string text, int indent, Marker kind, int number)
    {
        var p = Text(new Thickness(0));
        if (Regex.Match(text, @"^\[([ xX])\]\s+(.*)$") is { Success: true } box)
        {
            var done = box.Groups[1].Value != " ";
            p.Inlines!.Add(new Run(done ? "☑ " : "☐ ") { Foreground = done ? ThemeColors.Of("DataHaiku", "#98C379") : ThemeColors.Of("FgMuted", "#B3B6C4") });
            text = box.Groups[2].Value;
        }
        AddInlines(p.Inlines!, text);
        var row = new DockPanel { Margin = new Thickness(indent >= 2 ? 16 : 0, 0, 0, 3) };
        if (kind != Marker.None)
        {
            var mark = new TextBlock
            {
                Text = kind == Marker.Decimal ? $"{number}." : "•",
                FontSize = 12,
                LineHeight = 18,
                MinWidth = 14,
                Margin = new Thickness(0, 0, 4, 0),
                Foreground = ThemeColors.Of("FgMuted", "#B3B6C4"),
            };
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
        }
        row.Children.Add(p);
        return row;
    }

    private static Control Heading(int level, string text)
    {
        var p = Text(new Thickness(0, level <= 2 ? 6 : 4, 0, 6));
        p.FontSize = level switch { 1 => 16, 2 => 14, _ => 13 };
        p.FontWeight = FontWeight.SemiBold;
        p.LineHeight = level == 1 ? 22 : 19;
        AddInlines(p.Inlines!, text);
        return p;
    }

    private static Control Code(List<string> lines) => new Border
    {
        Background = ThemeColors.Of("TrackFill", "#1FFFFFFF"),
        Padding = new Thickness(10, 8),
        Margin = new Thickness(0, 0, 0, 10),
        Child = new SelectableTextBlock
        {
            Text = string.Join("\n", lines),
            FontFamily = Mono,
            FontSize = 11,
            LineHeight = 16,
            TextWrapping = TextWrapping.Wrap,
        },
    };

    private static Control Quote(List<string> lines)
    {
        var p = (SelectableTextBlock)Paragraph(lines);
        p.Margin = new Thickness(0);
        p.Foreground = ThemeColors.Of("FgMuted", "#B3B6C4");
        return new Border
        {
            BorderBrush = ThemeColors.Of("TrackEdge", "#26FFFFFF"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, 10),
            Child = p,
        };
    }

    private static Control Rule() => new Border
    {
        Height = 1,
        Background = ThemeColors.Of("TrackEdge", "#26FFFFFF"),
        Margin = new Thickness(0, 4),
    };

    /// <summary>Añade el texto de una línea con su negrita, cursiva, código y enlaces.</summary>
    private static void AddInlines(InlineCollection inlines, string text, bool allowHttp = false)
    {
        var at = 0;
        foreach (Match m in Inline.Matches(text))
        {
            if (m.Index > at) inlines.Add(new Run(text[at..m.Index]));
            if (m.Groups["b"].Success)
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, m.Groups["b"].Value, allowHttp);
                inlines.Add(bold);
            }
            else if (m.Groups["i"].Success)
            {
                var italic = new Italic();
                AddInlines(italic.Inlines, m.Groups["i"].Value, allowHttp);
                inlines.Add(italic);
            }
            else if (m.Groups["c"].Success)
            {
                inlines.Add(new Run(m.Groups["c"].Value)
                {
                    FontFamily = Mono,
                    FontSize = 11,
                    Background = ThemeColors.Of("TrackFill", "#1FFFFFFF"),
                });
            }
            else if (m.Groups["t"].Success) inlines.Add(Link(m.Groups["t"].Value, m.Groups["u"].Value, allowHttp));
            else if (m.Groups["a"].Success) inlines.Add(Link(m.Groups["a"].Value, m.Groups["a"].Value, allowHttp));
            at = m.Index + m.Length;
        }
        if (at < text.Length) inlines.Add(new Run(text[at..]));
    }

    /// <summary>Enlace que abre su dirección en el navegador; si no es https (o http, si se admite), se queda en texto.</summary>
    /// <remarks>
    /// Las descripciones de las tareas vienen del panel y solo abren https; el texto de la persona, también http.
    /// Avalonia no tiene enlaces dentro de un texto: es un texto pulsable metido en la línea, que no se parte al final de ella.
    /// </remarks>
    private static Inline Link(string text, string url, bool allowHttp)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme == Uri.UriSchemeHttps || allowHttp && uri.Scheme == Uri.UriSchemeHttp))
            return new Run(text);
        var link = new TextBlock
        {
            Text = text,
            Foreground = ThemeColors.Of("DataSonnet", "#61AFEF"),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        ToolTip.SetTip(link, uri.AbsoluteUri);
        link.PointerEntered += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.PointerExited += (_, _) => link.TextDecorations = null;
        link.PointerReleased += async (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            try
            {
                if (TopLevel.GetTopLevel(link)?.Launcher is { } launcher) await launcher.LaunchUriAsync(uri);
            }
            catch (Exception ex)
            {
                JsonStore.LogError("tasks-link", ex);
            }
        };
        return new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.TextBottom };
    }
}

/// <summary>Texto con formato en un TextBlock: <c>local:MarkdownLine.Text="{Binding …}"</c>, con lo que entiende <see cref="MarkdownView.Line"/>.</summary>
public sealed class MarkdownLine : AvaloniaObject
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<MarkdownLine, TextBlock, string?>("Text");

    static MarkdownLine()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((block, e) =>
        {
            block.Inlines ??= [];
            MarkdownView.Line(block.Inlines, e.NewValue as string);
        });
    }

    public static string? GetText(TextBlock element) => element.GetValue(TextProperty);

    public static void SetText(TextBlock element, string? value) => element.SetValue(TextProperty, value);
}
