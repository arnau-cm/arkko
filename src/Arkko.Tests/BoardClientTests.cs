using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Arkko.Core;

namespace Arkko.Tests;

public class BoardClientTests
{
    /// <summary>Servidor falso: contesta en orden las respuestas dadas y guarda lo que se le ha pedido.</summary>
    private sealed class FakeServer(params string[] responses) : HttpMessageHandler
    {
        private int _next;
        public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Requests { get; } = [];
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            var text = _next < responses.Length ? responses[_next++] : "{}";
            return new HttpResponseMessage(Status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static BoardClient Client(BoardProfile profile, FakeServer server, Dictionary<string, string> values) =>
        new(profile, values, new HttpClient(server));

    private static BoardProfile PanelProfile() => BoardProfile.Parse(TestProfiles.Panel);

    private static readonly Dictionary<string, string> PanelValues = new()
    {
        ["baseUrl"] = "https://panel.example.test",
        ["apiKey"] = "clave-de-prueba",
    };

    // Una tarea con la forma de la del panel, con los datos inventados.
    private static string PanelTask(string key, string column, bool done = false, string? assignee = null)
    {
        var person = assignee is null ? "null" : "{ \"id\": \"u1\", \"name\": \"" + assignee + "\" }";
        return $$"""
            {
              "id": "id-{{key}}", "key": "{{key}}", "title": "Tarea {{key}}", "description": "Texto", "type": "TASK", "priority": "LOW",
              "estimatedHours": 1.5, "url": "https://panel.example.test/issues/{{key}}",
              "createdAt": "2026-10-13T08:00:00.000Z", "updatedAt": "2026-10-14T09:30:00.000Z", "dueDate": null,
              "column": { "id": "c1", "name": "{{column}}", "isDone": {{(done ? "true" : "false")}} },
              "assignee": {{person}},
              "labels": [ { "id": "l1", "name": "backend" }, { "id": "l2", "name": "millora" } ]
            }
            """;
    }

    /// <summary>Una página de list_issues con sus tareas y su cursor.</summary>
    private static string PanelPage(string? next, params string[] tasks) =>
        "{ \"ok\": true, \"result\": [" + string.Join(",", tasks) + "], \"pagination\": { \"limit\": 100, \"hasMore\": "
        + (next is null ? "false" : "true") + ", \"nextCursor\": " + (next is null ? "null" : "\"" + next + "\"") + " } }";

    [Fact]
    public async Task El_perfil_del_panel_pide_por_POST_con_la_clave_y_sigue_el_cursor()
    {
        var server = new FakeServer(
            PanelPage("c2", PanelTask("DEVLAB-1", "Por hacer"), PanelTask("DEVLAB-2", "En curso", assignee: "Persona Inventada")),
            PanelPage(null, PanelTask("DEVLAB-3", "Hecho", done: true)));

        var tasks = await Client(PanelProfile(), server, PanelValues)
            .TasksAsync(new BoardProject("p1", "DEV", "Desarrollo"), new BoardRef("b1", "WEB"));

        Assert.Equal(["DEVLAB-1", "DEVLAB-2", "DEVLAB-3"], tasks.Select(t => t.Key));
        Assert.Equal([TaskState.Todo, TaskState.Doing, TaskState.Done], tasks.Select(t => t.State));
        Assert.Equal(["backend", "millora"], tasks[0].Labels);
        Assert.Equal("Persona Inventada", tasks[1].Assignee);
        Assert.Null(tasks[0].Assignee);
        Assert.Equal(1.5, tasks[0].EstimatedHours);
        Assert.Equal(new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc), tasks[0].UpdatedUtc);

        Assert.All(server.Requests, r =>
        {
            Assert.Equal(HttpMethod.Post, r.Method);
            Assert.Equal("https://panel.example.test/api/v1/ai", r.Url);
            Assert.Equal("Bearer clave-de-prueba", r.Auth);
        });
        var first = JsonNode.Parse(server.Requests[0].Body!)!;
        Assert.Equal("list_issues", first["action"]!.GetValue<string>());
        Assert.Equal("b1", first["data"]!["boardId"]!.GetValue<string>());
        Assert.Null(first["data"]!["cursor"]);
        Assert.Equal("c2", JsonNode.Parse(server.Requests[1].Body!)!["data"]!["cursor"]!.GetValue<string>());
    }

    [Fact]
    public async Task Un_enlace_relativo_se_completa_con_la_direccion_del_panel()
    {
        var relative = PanelTask("DEVLAB-6", "Por hacer").Replace("https://panel.example.test/issues/DEVLAB-6", "/p/devlab/i/6");
        var server = new FakeServer(PanelPage(null, relative, PanelTask("DEVLAB-7", "Por hacer")));
        var tasks = await Client(PanelProfile(), server, PanelValues).TasksAsync(new BoardProject("p1", "DEVLAB", "DevLab"));
        Assert.Equal("https://panel.example.test/p/devlab/i/6", tasks[0].Url);
        Assert.Equal("https://panel.example.test/issues/DEVLAB-7", tasks[1].Url);
    }

    [Fact]
    public async Task La_marca_de_cerrada_no_convierte_en_hecha_una_descartada()
    {
        var server = new FakeServer(PanelPage(null, PanelTask("DEVLAB-4", "Descartado", done: true), PanelTask("DEVLAB-5", "Columna nueva", done: true)));
        var tasks = await Client(PanelProfile(), server, PanelValues).TasksAsync(new BoardProject("p1", "DEV", "Desarrollo"));
        Assert.Equal([TaskState.Discarded, TaskState.Done], tasks.Select(t => t.State));
    }

    [Fact]
    public async Task Por_descartar_es_su_propia_columna_aunque_el_panel_la_marque_cerrada()
    {
        var server = new FakeServer(PanelPage(null, PanelTask("DEVLAB-8", "Por descartar", done: true)));
        var tasks = await Client(PanelProfile(), server, PanelValues).TasksAsync(new BoardProject("p1", "DEVLAB", "DevLab"));
        Assert.Equal([TaskState.ToDiscard], tasks.Select(t => t.State));
    }

    [Fact]
    public async Task Quien_soy_sale_de_su_llamada_y_sin_ella_no_hay()
    {
        var server = new FakeServer("""{ "ok": true, "result": { "user": { "id": "u1", "name": "Persona Inventada", "role": "MEMBER" } } }""");
        var me = await Client(PanelProfile(), server, PanelValues).MeAsync();
        Assert.Equal(new BoardPerson("u1", "Persona Inventada"), me);
        Assert.Equal("get_capabilities", JsonNode.Parse(server.Requests[0].Body!)!["action"]!.GetValue<string>());

        var without = BoardProfile.Parse(TestProfiles.Panel.Replace("\"me\": { \"method\"", "\"nome\": { \"method\""));
        Assert.Null(await Client(without, new FakeServer(), PanelValues).MeAsync());
    }

    [Fact]
    public async Task Sin_subtablero_no_se_manda_el_campo()
    {
        var server = new FakeServer("""{ "ok": true, "result": [], "pagination": { "hasMore": false, "nextCursor": null } }""");
        await Client(PanelProfile(), server, PanelValues).TasksAsync(new BoardProject("p1", "DEV", "Desarrollo"));
        Assert.Null(JsonNode.Parse(server.Requests[0].Body!)!["data"]!["boardId"]);
    }

    [Fact]
    public async Task Un_perfil_tipo_Jira_pide_por_GET_con_usuario_y_token_y_pagina_por_desplazamiento()
    {
        var profile = BoardProfile.Parse("""
            {
              "id": "jira", "name": "Jira",
              "auth": { "type": "basic", "user": "{email}", "password": "{token}" },
              "requests": {
                "tasks": {
                  "url": "{baseUrl}/rest/api/3/search?jql=project={project.key}&startAt={offset}&maxResults={size}",
                  "items": "$.issues[*]",
                  "paging": { "type": "offset", "size": 2, "total": "$.total" }
                }
              },
              "fields": {
                "task": { "key": "$.key", "title": "$.fields.summary", "status": "$.fields.status.name",
                          "labels": "$.fields.labels[*]", "url": "{baseUrl}/browse/{key}" }
              },
              "statuses": { "To Do": "todo", "In Progress": "doing", "Done": "done" }
            }
            """);
        string Issue(string key, string status) =>
            $$"""{ "key": "{{key}}", "fields": { "summary": "S {{key}}", "status": { "name": "{{status}}" }, "labels": ["api"] } }""";
        var server = new FakeServer(
            $$"""{ "total": 3, "issues": [ {{Issue("ABC-1", "To Do")}}, {{Issue("ABC-2", "In Progress")}} ] }""",
            $$"""{ "total": 3, "issues": [ {{Issue("ABC-3", "Done")}} ] }""");

        var tasks = await Client(profile, server, new() { ["baseUrl"] = "https://empresa.example.test/", ["email"] = "a@b.test", ["token"] = "t" })
            .TasksAsync(new BoardProject("10", "ABC", "Proyecto"));

        Assert.Equal(["ABC-1", "ABC-2", "ABC-3"], tasks.Select(t => t.Key));
        Assert.Equal(TaskState.Doing, tasks[1].State);
        Assert.Equal("https://empresa.example.test/browse/ABC-1", tasks[0].Url);
        Assert.Equal(["api"], tasks[0].Labels);
        Assert.Equal("https://empresa.example.test/rest/api/3/search?jql=project=ABC&startAt=0&maxResults=2", server.Requests[0].Url);
        Assert.Contains("startAt=2", server.Requests[1].Url);
        Assert.Equal(HttpMethod.Get, server.Requests[0].Method);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("a@b.test:t")), server.Requests[0].Auth);
    }

    [Fact]
    public async Task Una_llamada_en_dos_pasos_pide_los_elementos_por_lotes()
    {
        var profile = BoardProfile.Parse("""
            {
              "id": "azure", "name": "Azure DevOps",
              "auth": { "type": "basic", "user": "", "password": "{pat}" },
              "requests": {
                "tasks": {
                  "method": "POST", "url": "{baseUrl}/_apis/wit/wiql",
                  "body": { "query": "SELECT [System.Id] FROM WorkItems" },
                  "items": "$.workItems[*]",
                  "then": { "ids": "$.id", "url": "{baseUrl}/_apis/wit/workitems?ids={ids}", "items": "$.value[*]", "batch": 2 }
                }
              },
              "fields": { "task": { "key": "$.id", "title": "$.fields['System.Title']", "status": "$.fields['System.State']" } },
              "statuses": { "Active": "doing" }
            }
            """);
        string Item(int id) => $$"""{ "id": {{id}}, "fields": { "System.Title": "T{{id}}", "System.State": "Active" } }""";
        var server = new FakeServer(
            """{ "workItems": [ { "id": 7 }, { "id": 8 }, { "id": 9 } ] }""",
            $$"""{ "value": [ {{Item(7)}}, {{Item(8)}} ] }""",
            $$"""{ "value": [ {{Item(9)}} ] }""");

        var tasks = await Client(profile, server, new() { ["baseUrl"] = "https://dev.example.test/org", ["pat"] = "x" })
            .TasksAsync(new BoardProject("p", "P", "P"));

        Assert.Equal(["7", "8", "9"], tasks.Select(t => t.Key));
        Assert.Equal("T8", tasks[1].Title);
        Assert.Equal(TaskState.Doing, tasks[2].State);
        Assert.Equal("https://dev.example.test/org/_apis/wit/workitems?ids=7%2C8", server.Requests[1].Url);
        Assert.Equal("https://dev.example.test/org/_apis/wit/workitems?ids=9", server.Requests[2].Url);
    }

    [Fact]
    public async Task Solo_se_admite_https()
    {
        var server = new FakeServer("{}");
        var values = new Dictionary<string, string>(PanelValues) { ["baseUrl"] = "http://panel.example.test" };
        var ex = await Assert.ThrowsAsync<BoardException>(() => Client(PanelProfile(), server, values).ProjectsAsync());
        Assert.Contains("https", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Sin_la_clave_dice_que_falta_y_no_llama()
    {
        var server = new FakeServer("{}");
        var ex = await Assert.ThrowsAsync<BoardException>(() =>
            Client(PanelProfile(), server, new() { ["baseUrl"] = "https://panel.example.test" }).ProjectsAsync());
        Assert.Contains("apiKey", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Una_clave_rechazada_se_explica_sin_ensenar_la_clave()
    {
        var server = new FakeServer("{}") { Status = HttpStatusCode.Unauthorized };
        var ex = await Assert.ThrowsAsync<BoardException>(() => Client(PanelProfile(), server, PanelValues).ProjectsAsync());
        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain("clave-de-prueba", ex.Message);
    }

    [Fact]
    public void Un_perfil_mal_escrito_dice_que_le_falta()
    {
        Assert.Contains("requests", Assert.Throws<BoardException>(() => BoardProfile.Parse("""{ "id": "x", "name": "X" }""")).Message);
        Assert.Contains("$", Assert.Throws<BoardException>(() => BoardProfile.Parse(
            """{ "id": "x", "name": "X", "requests": { "tasks": { "url": "https://a.test", "items": "result" } } }""")).Message);
        Assert.Contains("desconocida", Assert.Throws<BoardException>(() => BoardProfile.Parse(
            """{ "id": "x", "name": "X", "requests": { "tasks": { "url": "https://a.test", "items": "$", "paging": { "type": "pages" } } } }""")).Message);
    }

    [Fact]
    public void El_perfil_del_panel_declara_lo_que_sabe_rellenar_y_traduce_sus_estados()
    {
        var profile = PanelProfile();
        Assert.True(profile.Has("task", "labels"));
        Assert.False(profile.Has("task", "storyPoints"));
        Assert.Equal(TaskState.Blocked, profile.StateOf("Bloqueado"));
        Assert.Equal(TaskState.Backlog, profile.StateOf("Backlog"));
        Assert.Equal(TaskState.Review, profile.StateOf(" en revisión "));
        Assert.Equal(TaskState.Unknown, profile.StateOf("Otra"));
        Assert.Equal("apiKey", profile.Settings.Single(s => s.Secret).Key);
    }

    [Fact]
    public void Las_rutas_bajan_por_objetos_listas_y_nombres_con_espacios()
    {
        var root = System.Text.Json.JsonDocument.Parse("""{ "a": { "b c": [ { "d": 1 }, { "d": 2 } ] }, "n": null }""").RootElement;
        Assert.Equal(["1", "2"], JsonPath.Strings(root, "$.a['b c'][*].d"));
        Assert.Equal("2", JsonPath.String(root, "$.a['b c'][1].d"));
        Assert.Null(JsonPath.String(root, "$.n"));
        Assert.Null(JsonPath.String(root, "$.x.y"));
        Assert.Throws<FormatException>(() => JsonPath.Select(root, "a.b").ToList());
    }

    [Fact]
    public void Las_plantillas_codifican_la_direccion_y_quitan_lo_opcional_vacio()
    {
        string? Value(string n) => n switch { "baseUrl" => "https://x.test/", "q" => "a b&c", _ => null };
        Assert.Equal("https://x.test/s?q=a%20b%26c&c=", BoardTemplate.FillUrl("{baseUrl}/s?q={q}&c={cursor?}", Value));
        var body = BoardTemplate.FillBody(JsonNode.Parse("""{ "data": { "q": "{q}", "cursor": "{cursor?}", "n": 5 } }"""), Value)!;
        Assert.Equal("a b&c", body["data"]!["q"]!.GetValue<string>());
        Assert.Null(body["data"]!["cursor"]);
        Assert.Equal(5, body["data"]!["n"]!.GetValue<int>());
        Assert.Throws<BoardException>(() => BoardTemplate.Fill("{falta}", Value, forUrl: false));
    }
}
