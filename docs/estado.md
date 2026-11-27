# Estado del proyecto

> **WIP: trabajo en curso.** Arkko se usa a diario, pero no está terminado. Este documento resume lo
> que funciona hoy y lo que queda por hacer.

## Qué funciona

| Área | Estado |
|---|---|
| Ventana flotante | Límites de uso, consumo por modelo, sesiones abiertas con su estado, avisos y contador en la barra de tareas |
| Hooks y statusline (`ak.exe`) | Registran el estado de cada sesión y el consumo de cada prompt; avisan si un hook deja de funcionar |
| Sesiones | Cada sesión, en directo o repetida paso a paso: secuencia, diagrama de agentes y herramientas, chat y «Obra» (la sesión como una obra en 3D) |
| Corcho | Ventanas sueltas con piezas de Sesiones para seguir varias sesiones a la vez |
| Tareas | Tablero de un panel de tareas configurable por perfiles, con las sesiones de cada tarea unidas por la clave de la rama |
| Diario y estadísticas | Lo hecho cada día, consumo por periodo, proyecto y tarea, y exportación CSV |
| Seguridad | Detección de secretos en los transcripts sin guardar su valor, con su ubicación exacta y el valor real solo mientras se mantiene pulsado |
| Vulnerabilidades | Lectura del informe de seguridad de un script externo (`check-security.ps1`) |
| Plataforma | Windows 10/11 con Avalonia y WebView2; el núcleo ya es portable |

## Qué falta

| Área | Pendiente |
|---|---|
| Plataforma | macOS y Linux: notificaciones, icono y barra de título propios de cada sistema, e instalador (`install.sh`) |
| Plataforma | Pasar los hooks de .NET 8 a .NET 10 |
| Plataforma | Firma de código, para que Smart App Control no bloquee los ejecutables |
| Plataforma | `ak.exe` nativo (AOT), para que los hooks no retrasen a Claude Code |
| Rendimiento | Descargar las vistas web que no se ven, parar los bucles de las páginas ocultas, revisar el sondeo de cada segundo y compartir los recorridos de los transcripts |
| Terminal | Terminal integrada (ConPTY y xterm.js) con pestañas, perfiles y escritura desde el chat de Sesiones |
| Fuentes | Otras herramientas de agentes además de Claude Code, empezando por Codex CLI |
| Sesiones | Siguientes fases de la Obra: el texto del plan, el parte del día y otras situaciones |
| Seguridad | Mejorar el diseño de la vista «Ver dónde» de un secreto |

## Documentación

- [Arquitectura](arquitectura.md): piezas, flujo de datos y decisiones de fondo.
- [Diseño](diseno.md): ventana, modos, aspecto visual y vistas.
- [Detección de claves](deteccion-de-claves.md): qué detecta, qué no y por qué.
