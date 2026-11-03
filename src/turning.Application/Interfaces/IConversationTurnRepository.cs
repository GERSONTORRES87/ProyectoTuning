using Turning.Domain.Entities;

namespace Turning.Application.Interfaces;

/// <summary>
/// Contrato de persistencia para turnos conversacionales.
/// </summary>
public interface IConversationTurnRepository
{
    /// <summary>
    /// Agrega un turno de conversación a la persistencia.
    /// </summary>
    Task AddAsync(ConversationTurn turn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista los turnos de una sesión ordenados por secuencia.
    /// </summary>
    Task<IReadOnlyList<ConversationTurn>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista los turnos de una sesión posteriores a una secuencia dada.
    /// </summary>
    /// <remarks>
    /// El cursor es <c>SequenceNumber</c> y no un identificador: ya es monótono, tiene un
    /// índice único por sesión y no deja huecos, así que un cliente que va pidiendo lo
    /// posterior a lo último que vio no puede saltarse ni repetir un turno.
    /// </remarks>
    Task<IReadOnlyList<ConversationTurn>> ListBySessionAfterAsync(Guid sessionId, int afterSequence, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el siguiente número de secuencia libre de la sesión.
    /// </summary>
    /// <remarks>
    /// Se calcula con MAX(SequenceNumber)+1 sobre los turnos persistidos, no con el
    /// contador de la sesión: ese contador deriva y, con dos personas escribiendo en la
    /// misma sesión, dos peticiones simultáneas calcularían la misma secuencia y violarían
    /// el índice único (ExperimentSessionId, SequenceNumber).
    /// </remarks>
    Task<int> GetNextSequenceNumberAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda cambios pendientes de la conversación.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<bool> TrySaveReplyAsync(CancellationToken cancellationToken = default);
}
