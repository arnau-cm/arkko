namespace Arkko.Tests;

/// <summary>Perfiles para las pruebas, con direcciones y nombres inventados: la app no trae ninguno.</summary>
internal static class TestProfiles
{
    /// <summary>Un panel con la forma de uno real que va por una sola dirección: POST con la acción en el cuerpo y paginación por cursor.</summary>
    public const string Panel = """
        {
          // Panel de prueba: la forma de un panel real, con datos inventados.
          "id": "panel-prueba",
          "name": "Panel de prueba",
          "settings": [
            { "key": "baseUrl", "label": "Dirección", "default": "https://panel.example.test" },
            { "key": "apiKey", "label": "Clave personal", "secret": true, "env": "PANEL_API_KEY" }
          ],
          "auth": { "type": "bearer", "token": "{apiKey}" },
          "requests": {
            "projects": { "method": "POST", "url": "{baseUrl}/api/v1/ai", "body": { "action": "list_projects" }, "items": "$.result[*]" },
            "boards": { "method": "POST", "url": "{baseUrl}/api/v1/ai",
                        "body": { "action": "list_boards", "data": { "projectId": "{project.id}" } }, "items": "$.result[*]" },
            "tasks": {
              "method": "POST", "url": "{baseUrl}/api/v1/ai",
              "body": { "action": "list_issues",
                        "data": { "projectId": "{project.id}", "boardId": "{board.id?}", "limit": 100, "cursor": "{cursor?}" } },
              "items": "$.result[*]",
              "paging": { "type": "cursor", "next": "$.pagination.nextCursor", "more": "$.pagination.hasMore" }
            },
            "task": { "method": "POST", "url": "{baseUrl}/api/v1/ai", "body": { "action": "get_issue", "data": { "key": "{key}" } }, "items": "$.result" },
            "me": { "method": "POST", "url": "{baseUrl}/api/v1/ai", "body": { "action": "get_capabilities" }, "items": "$.result.user" }
          },
          "fields": {
            "project": { "id": "$.id", "key": "$.key", "name": "$.name" },
            "board": { "id": "$.id", "name": "$.name" },
            "me": { "id": "$.id", "name": "$.name" },
            "task": {
              "key": "$.key", "id": "$.id", "title": "$.title", "description": "$.description", "status": "$.column.name",
              "done": "$.column.isDone", "type": "$.type", "priority": "$.priority", "labels": "$.labels[*].name",
              "assignee": "$.assignee.name", "assigneeId": "$.assigneeId", "estimatedHours": "$.estimatedHours", "url": "$.url",
              "created": "$.createdAt", "updated": "$.updatedAt", "due": "$.dueDate"
            }
          },
          "statuses": {
            "Backlog": "backlog", "Por hacer": "todo", "En curso": "doing", "Bloqueado": "blocked", "En revisión": "review",
            "Hecho": "done", "Por descartar": "todiscard", "Descartado": "discarded"
          }
        }
        """;
}
