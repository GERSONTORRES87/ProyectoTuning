namespace Turning.Application.Features.ConversationTurns;

/// <summary>
/// Casos de uso para listar y registrar mensajes en una sesión experimental.
/// </summary>
public interface IConversationTurnService
{
    /// <summary>
    /// Devuelve la conversación persistida de una sesión autenticada.
    /// </summary>
    /// <param name="ownerUserId">Usuario autenticado.</param>
    /// <param name="sessionId">Sesión consultada.</param>
    /// <param name="afterSequence">
    /// Si se indica, solo devuelve los turnos posteriores a esa secuencia. Sirve para que un
    /// cliente sondee la conversacion sin volver a descargarla entera.
    /// </param>
    /// <param name="limit">Numero maximo de turnos a devolver.</param>
    /// <param name="cancellationToken">Cancelación de la petición.</param>
    /// <param name="isPrivilegedRequester">Permite la lectura investigadora de sesiones ajenas.</param>
    Task<IReadOnlyList<ConversationTurnSnapshot>> ListAsync(Guid ownerUserId, Guid sessionId, int? afterSequence = null, int limit = 200, CancellationToken cancellationToken = default, bool isPrivilegedRequester = false);

    /// <summary>
    /// Agrega un mensaje a una sesión autenticada y resuelve la respuesta del interlocutor
    /// según la condición experimental.
    /// </summary>
    /// <returns>
    /// El turno registrado junto con la respuesta generada, sus metadatos de proveedor y el
    /// estado de degradación, si lo hubo.
    /// </returns>
    Task<ConversationTurnResult> AddAsync(Guid ownerUserId, Guid sessionId, AddConversationTurnRequest request, CancellationToken cancellationToken = default, bool isPrivilegedRequester = false);
}
