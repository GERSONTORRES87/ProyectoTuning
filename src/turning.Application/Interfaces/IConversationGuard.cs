namespace Turning.Application.Interfaces;

/// <summary>
/// Cómo decidió el filtro previo al modelo de lenguaje.
/// </summary>
public enum GuardMode
{
    /// <summary>Se buscó por embeddings y no hubo bloqueo.</summary>
    Context,

    /// <summary>Sin embeddings: solo se comprobaron las palabras clave de las reglas de bloqueo.</summary>
    Keywords,

    /// <summary>Una regla de bloqueo respondió en lugar del modelo.</summary>
    Blocked
}

/// <summary>
/// Decisión del filtro sobre un mensaje del participante.
/// </summary>
/// <param name="Mode">Cómo se decidió.</param>
/// <param name="RuleId">Regla de bloqueo aplicada, si hubo bloqueo.</param>
/// <param name="Response">Respuesta aprobada de esa regla.</param>
/// <param name="Guidance">Pautas para el modelo, si no hubo bloqueo.</param>
/// <param name="GuidanceIds">Identificadores de esas pautas, para la trazabilidad.</param>
public sealed record GuardDecision(
    GuardMode Mode,
    string? RuleId,
    string? Response,
    IReadOnlyList<string> Guidance,
    IReadOnlyList<string> GuidanceIds)
{
    /// <summary>Indica que el modelo no debe llamarse.</summary>
    public bool Blocked => Mode == GuardMode.Blocked;

    /// <summary>Resumen para el evento de la respuesta: <c>blocked:id</c>, <c>context:ids</c> o <c>keywords</c>.</summary>
    public string Trace => Mode switch
    {
        GuardMode.Blocked => $"blocked:{RuleId}",
        GuardMode.Context => $"context:{string.Join(',', GuidanceIds)}",
        _ => "keywords"
    };

    /// <summary>Decisión neutra: sin bloqueo ni pautas.</summary>
    public static GuardDecision PassThrough { get; } = new(GuardMode.Keywords, null, null, [], []);
}

/// <summary>
/// Filtro RAG que revisa el mensaje del participante antes del modelo de lenguaje.
/// </summary>
public interface IConversationGuard
{
    /// <summary>
    /// Evalúa un mensaje. Solo lanza si el llamante cancela: ante cualquier fallo propio
    /// degrada a palabras clave, porque el filtro no puede costarle el turno al participante.
    /// </summary>
    Task<GuardDecision> EvaluateAsync(string message, string? emotionId, CancellationToken cancellationToken = default);
}
