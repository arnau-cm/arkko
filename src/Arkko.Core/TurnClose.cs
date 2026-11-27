namespace Arkko.Core;

/// <summary>Cierra un turno de una sesión: la da por terminada, calcula el consumo del turno con el transcript y lo
/// apunta en el registro de prompts.</summary>
/// <remarks>
/// Lo hace el hook Stop y, cuando ese hook no llega a ejecutarse, la app, al ver en el transcript que el turno terminó:
/// por eso está aquí y no en el hook.
/// </remarks>
public static class TurnClose
{
    /// <summary>Lo que se guarda del texto de un prompt: basta para reconocerlo, el completo sigue en el transcript.</summary>
    public const int PromptMaxLength = 1000;

    /// <summary>Cierra el turno y devuelve el estado que queda y su consumo; null si la sesión ya no estaba guardada y
    /// <paramref name="create"/> es falso.</summary>
    /// <param name="whenUtc">Cuándo terminó el turno, para el registro del prompt.</param>
    /// <param name="retries">Cuántas veces volver a leer un turno que aún no tiene respuestas, con un cuarto de segundo entre
    /// una y otra.</param>
    /// <param name="common">Lo que se rellena además en el estado, como la carpeta y el proceso que da el hook.</param>
    /// <remarks>
    /// Claude Code puede llamar a Stop antes de escribir la última respuesta en el transcript: en un turno con una sola
    /// respuesta, como el de un comando «!», no quedaría nada que contar sin volver a leer un poco después.
    /// </remarks>
    public static (SessionState State, TurnUsage? Usage)? Close(string sessionId, string? transcript, DateTime whenUtc,
        int retries = 0, bool create = true, Action<SessionState>? common = null)
    {
        TurnUsage? usage = null;
        (PromptKind Kind, string? Text) opening = (PromptKind.Prompt, null);
        if (!string.IsNullOrEmpty(transcript))
        {
            try
            {
                usage = TranscriptReader.LastTurn(transcript, out opening);
                for (var retry = 0; retry < retries && usage is { ByModel.Count: 0 }; retry++)
                {
                    Thread.Sleep(250);
                    usage = TranscriptReader.LastTurn(transcript, out opening);
                }
            }
            catch (Exception ex)
            {
                JsonStore.LogError("transcript", ex);
            }
        }

        void Change(SessionState s)
        {
            common?.Invoke(s);
            s.Status = SessionStatus.Done;
            s.Detail = null;
            if (usage is not null) s.LastTurn = usage;
        }
        var state = create ? SessionStore.Update(sessionId, Change) : SessionStore.UpdateIfExists(sessionId, Change);
        if (state is null) return null;

        if (usage is { ByModel.Count: > 0 })
        {
            JsonStore.AppendLine(AppPaths.PromptsFile(whenUtc.ToLocalTime()), new PromptRecord
            {
                WhenUtc = whenUtc,
                SessionId = sessionId,
                Project = state.Project,
                Repo = state.Repo,
                Usage = usage,
                SessionName = state.SessionName,
                Prompt = TurnText(opening, state.LastPrompt),
                Kind = opening.Kind,
                TranscriptPath = transcript,
            });
        }
        return (state, usage);
    }

    /// <summary>El texto que se apunta del turno: el prompt guardado al enviarlo o, si no lo hubo, lo que lo empezó.</summary>
    /// <remarks>
    /// Un comando «!» no pasa por el envío de prompts: sin esto, el turno se apuntaría con el texto del prompt anterior.
    /// El comando se guarda con sus secretos tapados, porque no pasa por el detector que frena los prompts.
    /// </remarks>
    private static string? TurnText((PromptKind Kind, string? Text) opening, string? lastPrompt) => opening.Kind switch
    {
        PromptKind.Shell => opening.Text is { Length: > 0 } command ? Truncate(SecretDetector.Redact(command)!.Trim()) : null,
        PromptKind.Notification => opening.Text is { Length: > 0 } notice ? Truncate(notice.Trim()) : lastPrompt,
        _ => lastPrompt,
    };

    /// <summary>Recorta un texto a lo que se guarda de un prompt.</summary>
    private static string Truncate(string text) => text.Length <= PromptMaxLength ? text : text[..(PromptMaxLength - 1)] + "…";
}
