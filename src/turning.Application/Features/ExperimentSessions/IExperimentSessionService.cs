namespace Turning.Application.Features.ExperimentSessions;

/// <summary>
/// Casos de uso de bootstrap y consulta de sesiones experimentales.
/// </summary>
public interface IExperimentSessionService
{
    /// <summary>
    /// Crea una sesión experimental inicial para el usuario autenticado.
    /// </summary>
    Task<ExperimentSessionSnapshot> CreateBootstrapSessionAsync(Guid ownerUserId, CreateExperimentSessionRequest? request = null, CancellationToken cancellationToken = default, bool isPrivilegedRequester = false);

    Task<ExperimentSessionSnapshot?> GetLatestSessionAsync(Guid ownerUserId, CancellationToken cancellationToken = default, bool isPrivilegedRequester = false);
    /// <summary>
    /// Obtiene una sesion por id. Solo el propietario o un solicitante privilegiado
    /// (Researcher/Administrator) puede verla; para el resto la sesion no existe.
    /// </summary>
    Task<ExperimentSessionSnapshot> GetByIdAsync(Guid id, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default);
    Task<PagedSessionsResult> ListByParticipantAsync(Guid participantId, Guid requestingUserId, bool isPrivilegedRequester, int page, int pageSize, CancellationToken ct = default);
    /// <summary>
    /// Activa una sesion propia (o cualquiera si el solicitante es privilegiado).
    /// </summary>
    Task<ExperimentSessionSnapshot> ActivateAsync(Guid id, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default);
    /// <summary>
    /// Completa una sesion propia (o cualquiera si el solicitante es privilegiado).
    /// </summary>
    Task<ExperimentSessionSnapshot> CompleteAsync(Guid id, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default);
    Task<ExperimentSessionSnapshot> CancelAsync(Guid id, string reason, Guid actorId, CancellationToken ct = default);

    /// <summary>
    /// Indica si el solicitante puede operar sobre la sesion dada. Devuelve false
    /// tanto cuando la sesion no existe como cuando existe pero es ajena: quien
    /// pregunta no debe poder distinguir ambos casos.
    ///
    /// Existe para que las rutas anidadas bajo /api/sessions/{sessionId}/... puedan
    /// aplicar el mismo aislamiento sin reimplementar la regla en cada controller.
    /// </summary>
    Task<bool> IsSessionAccessibleAsync(Guid sessionId, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default);

    /// <summary>
    /// Resuelve que puede hacer un usuario sobre una sesion.
    /// </summary>
    /// <remarks>
    /// Devuelve <see cref="SessionAccessLevel.None"/> tanto si la sesion no existe como si
    /// el usuario no tiene nada que ver con ella: el llamante no debe poder distinguirlo.
    /// </remarks>
    Task<SessionAccessLevel> GetAccessLevelAsync(Guid sessionId, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default);

    /// <summary>
    /// Une al usuario a una sesion Human como interlocutor, a partir del codigo de sesion.
    /// </summary>
    /// <remarks>
    /// Recibe el codigo y no el identificador a proposito: quien se une todavia no tiene
    /// acceso a la sesion, asi que una ruta con {sessionId} la cortaria el filtro de
    /// aislamiento antes de llegar aqui.
    /// </remarks>
    Task<ExperimentSessionSnapshot> JoinAsInterlocutorAsync(string sessionCode, Guid interlocutorUserId, CancellationToken ct = default);

    /// <summary>
    /// Asigna explicitamente un interlocutor a una sesion. Solo para roles privilegiados.
    /// </summary>
    Task<ExperimentSessionSnapshot> AssignInterlocutorAsync(Guid sessionId, Guid interlocutorUserId, CancellationToken ct = default);

    /// <summary>
    /// Libera al interlocutor de una sesion. Solo para roles privilegiados.
    /// </summary>
    Task<ExperimentSessionSnapshot> ReleaseInterlocutorAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Lista las sesiones Human que todavia esperan interlocutor.
    /// </summary>
    Task<PagedSessionsResult> ListAwaitingInterlocutorAsync(int page, int pageSize, CancellationToken ct = default);
}
