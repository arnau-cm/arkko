using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Avalonia.Threading;
using Arkko.Core;

namespace Arkko.Desktop;

/// <summary>La lectura de todas las sesiones abiertas que comparten los widgets del corcho en directo: una foto de cada
/// una (estado, lo que espera de la persona y desde cuándo, sus últimas acciones, contexto, modelo y su última hora).</summary>
/// <remarks>
/// Solo lee mientras algún widget la escucha, y una vez para todos: cada transcript es el mismo <see cref="SessionFeed"/>
/// que usan Sesiones y Relojes. La foto se hace en JSON una sola vez por cambio y se reparte tal cual a cada página.
/// </remarks>
internal static class LiveSessions
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Las acciones que se mandan de cada sesión y los trozos de su última hora.
    private const int Actions = 6;
    private const int Buckets = 30;

    private static readonly DispatcherTimer Tick = new() { Interval = TimeSpan.FromSeconds(30) };
    private static readonly DispatcherTimer SoonTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private static readonly Dictionary<string, SessionFeed> Feeds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Action<string>> Listeners = [];

    // Cuántos de los que escuchan quieren también el reloj de cada sesión, que es lo que más pesa de la foto.
    private static int _faces;

    private static MainViewModel? _home;
    private static string? _last;

    static LiveSessions()
    {
        Tick.Tick += (_, _) => Follow();
        SoonTimer.Tick += (_, _) =>
        {
            SoonTimer.Stop();
            Send();
        };
    }

    /// <summary>Empieza a recibir la foto de las sesiones, ya mismo la última si la hay; hay que soltarla con lo que
    /// devuelve.</summary>
    /// <param name="faces">Si hace falta también el reloj de cada sesión, como el de Relojes.</param>
    public static IDisposable Listen(Action<string> onSnapshot, bool faces = false)
    {
        Listeners.Add(onSnapshot);
        if (faces) _faces++;
        if (Listeners.Count == 1) Start();
        else if (faces && _faces == 1) Send();
        else if (_last is not null) onSnapshot(_last);
        return new Subscription(onSnapshot, faces);
    }

    /// <summary>Devuelve las filas de las sesiones de Inicio en el orden de la foto: por orden de apertura.</summary>
    /// <param name="open">Los estados de las sesiones abiertas, si ya se han leído.</param>
    public static List<SessionRow> InOrder(IEnumerable<SessionRow> rows, IEnumerable<SessionState>? open = null)
    {
        var started = (open ?? SessionFeed.OpenSessions()).ToDictionary(s => s.SessionId, s => s.StartedUtc, StringComparer.OrdinalIgnoreCase);
        return rows
            .OrderBy(r => started.TryGetValue(r.SessionId, out var at) ? at : DateTime.MaxValue)
            .ThenBy(r => r.SessionId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Empieza a leer: se apunta a Inicio, lee las sesiones abiertas y las sigue.</summary>
    private static void Start()
    {
        _home = NativeViews.Home() as MainViewModel;
        if (_home is not null) _home.PropertyChanged += OnHomeChanged;
        RepoColors.Changed += Soon;
        Sync();
        Tick.Start();
        Send();
    }

    /// <summary>Deja de leer cuando no queda nadie escuchando: suelta las sesiones y lo calculado.</summary>
    private static void Stop()
    {
        Tick.Stop();
        SoonTimer.Stop();
        if (_home is not null) _home.PropertyChanged -= OnHomeChanged;
        RepoColors.Changed -= Soon;
        _home = null;
        foreach (var feed in Feeds.Values)
        {
            feed.Updated -= Soon;
            feed.Release();
        }
        Feeds.Clear();
        _last = null;
    }

    /// <summary>Cambian las sesiones de Inicio: se leen las nuevas, se sueltan las cerradas y se repinta; si cambian los
    /// límites, solo se repinta.</summary>
    private static void OnHomeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Limits))
        {
            Soon();
            return;
        }
        if (e.PropertyName != nameof(MainViewModel.Sessions)) return;
        Sync();
        Soon();
    }

    /// <summary>Deja leyéndose justo las sesiones abiertas.</summary>
    private static void Sync()
    {
        var open = SessionFeed.OpenSessions().Where(s => s.TranscriptPath is not null).ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);
        foreach (var id in Feeds.Keys.Where(id => !open.ContainsKey(id)).ToList())
        {
            Feeds[id].Updated -= Soon;
            Feeds[id].Release();
            Feeds.Remove(id);
        }
        foreach (var (id, state) in open)
        {
            if (Feeds.ContainsKey(id)) continue;
            var feed = SessionFeed.Acquire(id);
            feed.Updated += Soon;
            Feeds[id] = feed;
            _ = Read(feed, state.TranscriptPath);
        }
    }

    /// <summary>Lee una sesión por primera vez; avisa al acabar con <see cref="SessionFeed.Updated"/>.</summary>
    private static async Task Read(SessionFeed feed, string? path)
    {
        try
        {
            await feed.ReadAsync(path);
        }
        catch (Exception ex)
        {
            JsonStore.LogError("live-read", ex);
        }
    }

    /// <summary>Pone al día cada sesión con lo nuevo de su transcript y vuelve a mandar, para que cuenten las esperas.</summary>
    private static async void Follow()
    {
        try
        {
            foreach (var feed in Feeds.Values.ToList()) await feed.FollowAsync();
        }
        catch (Exception ex)
        {
            JsonStore.LogError("live-follow", ex);
        }
        Send();
    }

    /// <summary>Manda dentro de un momento, para juntar en uno los avisos de varias sesiones seguidas.</summary>
    private static void Soon()
    {
        if (Listeners.Count == 0) return;
        SoonTimer.Stop();
        SoonTimer.Start();
    }

    /// <summary>Hace la foto de las sesiones abiertas, por orden de apertura, y la reparte a quien escucha.</summary>
    private static void Send()
    {
        if (Listeners.Count == 0 || _home is null) return;
        var now = DateTime.UtcNow;
        var states = SessionFeed.OpenSessions().ToDictionary(s => s.SessionId, StringComparer.OrdinalIgnoreCase);
        var rows = InOrder(_home.Sessions, states.Values);
        var sessions = rows.Select(row =>
        {
            var trace = Feeds.TryGetValue(row.SessionId, out var feed) ? feed.Trace : null;
            var running = row.Working || row.Agents || row.Command || row.Waiting;
            var face = trace is null ? null : SessionClock.Build(trace, now, running);
            var state = ClocksView.StateOf(row);
            var color = RepoColors.Of(row.Title);
            var pending = states.TryGetValue(row.SessionId, out var st) ? SessionLive.Pending(st, trace, row.NeedsReview, row.Recap) : null;
            return new
            {
                id = row.SessionId,
                title = row.Title,
                worktree = row.Worktree,
                color = ClocksView.Hex(color),
                band = ClocksView.Hex(RepoColors.Band(color)),
                task = row.TaskKey,
                taskTitle = row.TaskTitle,
                state,
                status = row.Status,
                since = Ms(row.SinceUtc),
                doing = ClocksView.Doing(row, state, face),
                model = row.Model,
                effort = string.IsNullOrEmpty(row.EffortText) ? null : row.EffortText,
                context = row.ContextPct is { } pct ? (int)Math.Round(pct) : (int?)null,
                activity = face is null ? null : SessionLive.Activity(face, Buckets),
                agents = face?.Rings.Skip(1).SelectMany(r => r.Tags).OrderBy(t => t.At).Select(t => t.Letter).ToList(),
                commands = st is null ? null : st.Commands.Where(c => c.Awaited).Select(c => c.Description).ToList(),
                face = _faces > 0 ? face : null,
                actions = trace is null ? null : SessionLive.Recent(trace, Actions).Select(a => new { who = a.Who, hue = a.Hue, verb = a.Verb, target = a.Target, at = Ms(a.AtUtc) }),
                pending = pending is null ? null : new
                {
                    kind = pending.Kind,
                    text = pending.Text,
                    header = pending.Header,
                    options = pending.Options,
                    more = pending.More,
                    since = Ms(pending.SinceUtc),
                },
            };
        }).ToList();
        _last = JsonSerializer.Serialize(new { type = "live", now = Ms(now), sessions, limits = Limits(now) }, Json);
        foreach (var listener in Listeners.ToList()) listener(_last);
    }

    /// <summary>Devuelve los límites de la cuenta con su previsión al ritmo actual, la misma que da Inicio, y lo gastado del
    /// de 5 horas desde que empezó su ventana.</summary>
    private static List<object> Limits(DateTime now)
    {
        var snapshot = JsonStore.Read<LimitsSnapshot>(AppPaths.LimitsFile);
        if (snapshot is null) return [];
        return snapshot.Limits.Select(l =>
        {
            var pct = Math.Clamp(l.UsedPct, 0, 100);
            var projection = LimitForecast.Project(l, now);
            var reset = l.ResetsAtUtc?.ToUniversalTime();
            var start = reset is { } r && LimitForecast.Period(l.Key) is { } period ? r - period : (DateTime?)null;
            return (object)new
            {
                key = l.Key,
                label = l.Label,
                pct = (int)Math.Round(pct),
                color = Tone(MainViewModel.LimitBrush(pct)),
                pressing = pct >= HomeWidget.Pressing,
                start = start is { } s ? Ms(s) : (long?)null,
                resets = reset is { } x ? Ms(x) : (long?)null,
                full = projection?.ToFull is { } f ? Ms(now + f) : (long?)null,
                atReset = projection is null ? (int?)null : (int)Math.Round(Math.Min(100, projection.AtReset)),
                perHour = projection is null ? (double?)null : Math.Round(projection.PerHour, 1),
                trail = start is { } from && l.Key == "five_hour" ? Trail(l.Key, from, now, pct) : null,
            };
        }).ToList();
    }

    /// <summary>Devuelve el color de un pincel como «#rrggbb»: el suyo si es liso o el de su último tramo si es un degradado,
    /// como los de la escala de uso.</summary>
    private static string? Tone(IBrush brush) => brush switch
    {
        IGradientBrush { GradientStops: [.., var last] } => ClocksView.Hex(new SolidColorBrush(last.Color)),
        _ => ClocksView.Hex(brush),
    };

    /// <summary>Devuelve lo gastado de un límite desde el inicio de su ventana hasta ahora, como pares de instante y
    /// porcentaje, sacado del histórico de los límites.</summary>
    /// <remarks>Empieza en cero, que es lo que marca una ventana nueva, y acaba en el valor de ahora, aunque el histórico
    /// solo apunte los cambios.</remarks>
    private static List<long[]> Trail(string key, DateTime from, DateTime now, double pct)
    {
        DateTime[] days = from.ToLocalTime().Date == now.ToLocalTime().Date ? [now.ToLocalTime()] : [from.ToLocalTime(), now.ToLocalTime()];
        var points = new List<long[]> { new[] { Ms(from), 0L } };
        foreach (var sample in days.SelectMany(d => JsonStore.ReadLines<LimitsSample>(AppPaths.LimitsHistoryFile(d))).Where(s => s.WhenUtc >= from).OrderBy(s => s.WhenUtc))
            if (sample.Limits.FirstOrDefault(l => l.Key == key) is { } limit)
                points.Add([Ms(sample.WhenUtc), (long)Math.Round(Math.Clamp(limit.UsedPct, 0, 100))]);
        points.Add([Ms(now), (long)Math.Round(pct)]);
        return points;
    }

    /// <summary>Un instante en milisegundos desde 1970, tomándolo como UTC.</summary>
    private static long Ms(DateTime t) => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    /// <summary>La escucha de un widget: al soltarla, deja de recibir y, si era la última, se deja de leer.</summary>
    private sealed class Subscription(Action<string> listener, bool faces) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            Listeners.Remove(listener);
            if (faces) _faces--;
            if (Listeners.Count == 0) Stop();
        }
    }
}
