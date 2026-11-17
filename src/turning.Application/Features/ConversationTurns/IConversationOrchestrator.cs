using Turning.Domain.Entities;

namespace Turning.Application.Features.ConversationTurns;

/// <summary>
/// Resultado de pedirle una respuesta al interlocutor.
/// </summary>
/// <param name="Text">
/// Texto generado. Vacío cuando la generación falló: el turno del participante se conserva
/// igual y la operación se marca degradada.
/// </param>
/// <param name="Provider">Proveedor que atendió (o intentó atender) la petición.</param>
/// <param name="Model">Modelo concreto, si el proveedor lo informa.</param>
/// <param name="LatencyMs">Latencia medida.</param>
/// <param name="Degraded">La respuesta no vino del proveedor previsto.</param>
/// <param name="FailureReason">Motivo del fallo, cuando lo hubo.</param>
/// <param name="Attempts">Intentos consumidos.</param>
/// <param name="ProvidersTried">Proveedores recorridos, en orden.</param>
/// <param name="Guard">Qué hizo el filtro previo al modelo (<see cref="Interfaces.GuardDecision.Trace"/>).</param>
public sealed record InterlocutorReplyOutcome(
    string? Text,
    string Provider,
    string? Model,
    long LatencyMs,
    bool Degraded,
    string? FailureReason,
    int Attempts,
    IReadOnlyList<string>? ProvidersTried,
    string? Guard = null)
{
    /// <summary>
    /// Indica si hay texto utilizable para persistir como turno del interlocutor.
    /// </summary>
    public bool HasReply => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Decide qué ocurre después de que el participante envía un mensaje, según la condición
/// experimental de la sesión.
/// </summary>
/// <remarks>
/// Es el router de RF-EXP-01 y existe para que esa decisión viva en un solo sitio. Antes
/// era un <c>if</c> suelto dentro del servicio de conversación, lo que hacía que "condición
/// Human" significara, en la práctica, únicamente "no generes nada".
///
/// En condición <see cref="ExperimentalCondition.AI"/> pide la respuesta al puerto de
/// generación. En <see cref="ExperimentalCondition.Human"/> no genera: responde una
/// persona, y el mensaje queda esperando su turno.
/// </remarks>
public interface IConversationOrchestrator
{
    /// <summary>
    /// Resuelve la respuesta del interlocutor para un turno recién registrado.
    /// </summary>
    /// <returns>
    /// <c>null</c> cuando la condición no genera respuesta automática; en caso contrario, el
    /// resultado de la generación, que puede venir degradado pero nunca lanza.
    /// </returns>
    Task<InterlocutorReplyOutcome?> ResolveReplyAsync(
        ExperimentSession session,
        ConversationTurn participantTurn,
        IReadOnlyList<ConversationTurn> previousTurns,
        Interfaces.EmotionContext? emotionContext = null,
        CancellationToken cancellationToken = default);
}
