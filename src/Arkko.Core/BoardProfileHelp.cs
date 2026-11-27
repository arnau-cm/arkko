namespace Arkko.Core;

/// <summary>Lo que enseña la ayuda de «Cómo se hace un perfil»: una plantilla comentada y el texto para pedírselo a Claude.</summary>
/// <remarks>La plantilla es un perfil válido, y una prueba lo comprueba: una ayuda que no funcionara sería peor que ninguna.</remarks>
public static class BoardProfileHelp
{
    /// <summary>Perfil de ejemplo para una API REST típica, con un comentario en cada parte.</summary>
    public const string Template = """
        {
          // Identificador (minúsculas, cifras, - y _) y nombre que verás en la app.
          "id": "mi-panel",
          "name": "Mi panel",

          // Lo que se pide al crear una conexión. Lo marcado "secret" se guarda cifrado.
          // "default" rellena el campo y "env" sugiere una variable de entorno para la clave.
          "settings": [
            { "key": "baseUrl", "label": "Dirección", "default": "https://mi-panel.example.com" },
            { "key": "token", "label": "Token", "secret": true, "env": "MI_PANEL_TOKEN" }
          ],

          // Cómo se identifica uno: "bearer" (token), "basic" (user y password) o "header" (header y value).
          "auth": { "type": "bearer", "token": "{token}" },

          // Las llamadas, solo de lectura. {baseUrl} y los demás ajustes se rellenan con la conexión;
          // {project.id}, {project.key} y {board.id}, con lo que se elige. Con «?», como {board.id?},
          // se quita si no hay valor. "items" dice dónde está la lista en la respuesta.
          "requests": {
            "projects": { "url": "{baseUrl}/api/projects", "items": "$.projects[*]" },
            "boards": { "url": "{baseUrl}/api/projects/{project.id}/boards", "items": "$[*]" },
            "tasks": {
              "url": "{baseUrl}/api/projects/{project.id}/tasks?board={board.id?}&start={offset}&size={size}",
              "items": "$.tasks[*]",
              // Página siguiente: "offset" (de size en size), "cursor" (next y more) o "link" (next es la dirección).
              "paging": { "type": "offset", "size": 50, "total": "$.total" }
            },
            // Para una llamada por POST: "method": "POST" y su "body", también con plantillas.
            "task": { "url": "{baseUrl}/api/tasks/{key}", "items": "$" },
            // Quién eres con esa clave, para el filtro «Mías». Opcional.
            "me": { "url": "{baseUrl}/api/me", "items": "$" }
          },

          // Dónde está cada dato. Rutas: $.a.b, $.lista[*].nombre o $['con espacios'].
          "fields": {
            "project": { "id": "$.id", "key": "$.key", "name": "$.name" },
            // De un subtablero, "color" es opcional: si el panel no lo da, la app elige uno.
            "board": { "id": "$.id", "name": "$.name", "color": "$.color" },
            "me": { "id": "$.id", "name": "$.name" },
            "task": {
              "key": "$.key", "title": "$.title", "status": "$.status.name", "type": "$.type",
              "priority": "$.priority", "labels": "$.labels[*].name", "assignee": "$.assignee.name", "assigneeId": "$.assignee.id",
              "estimatedHours": "$.estimate", "url": "$.url", "created": "$.createdAt", "updated": "$.updatedAt",
              // Subtablero de la tarea: con él, el proyecto entero se puede ver por subtableros.
              "boardId": "$.board.id"
            }
          },

          // A qué columna de la app va cada estado del panel:
          // backlog, todo, doing, blocked, review, done, todiscard (propuesta para descartar) o discarded.
          "statuses": { "Pendiente": "todo", "En curso": "doing", "En revisión": "review", "Hecho": "done" }
        }
        """;

    /// <summary>Texto para pegarle a Claude Code, con la documentación de la API, y que escriba el perfil.</summary>
    public static string ClaudePrompt => $"""
        Escribe un perfil de tablero para Arkko que lea las tareas de mi panel. Más abajo tienes la
        documentación de su API.

        Sigue exactamente el formato de la plantilla (JSON con comentarios) y rellena:
        1. Cómo se identifica uno con la API.
        2. Las llamadas para listar proyectos, sus subtableros (si existen), las tareas de un proyecto o de un
           subtablero y una tarea por su clave, cada una con su paginación; y, si la API lo permite, la que dice
           quién es el dueño de la clave («me»).
        3. Dónde está cada dato de una tarea en la respuesta.
        4. A qué columna va cada estado del panel: backlog, todo, doing, blocked, review, done, todiscard (propuesta
           para descartar) o discarded.

        Solo lectura: no uses ninguna llamada que cree, mueva o cambie nada. Devuélveme solo el JSON.

        Plantilla:
        {Template}

        Documentación de la API:
        (pega aquí la documentación o el OpenAPI de tu panel)
        """;
}
