using Turning.Domain.Entities;

namespace Turning.Application.Interfaces;

/// <summary>
/// Contrato de persistencia para sesiones experimentales.
/// </summary>
public interface IExperimentSessionRepository
{
    /// <summary>
    /// Persiste una nueva sesión experimental.
    /// </summary>
    Task AddAsync(ExperimentSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene una sesión por identificador.
    /// </summary>
    Task<ExperimentSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene la sesión más reciente para un usuario autenticado.
    /// </summary>
    Task<ExperimentSession?> GetLatestByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);

    Task<List<ExperimentSession>> ListByOwnerAsync(Guid ownerUserId, int page, int pageSize, CancellationToken ct = default);
    Task<int> CountByOwnerAsync(Guid ownerUserId, CancellationToken ct = default);
    Task<ExperimentSession?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Lista las sesiones Human que todavia no tienen interlocutor asignado.
    /// </summary>
    Task<List<ExperimentSession>> ListAwaitingInterlocutorAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Cuenta las sesiones Human que todavia esperan interlocutor.
    /// </summary>
    Task<int> CountAwaitingInterlocutorAsync(CancellationToken ct = default);
    Task ReloadAsync(ExperimentSession session, CancellationToken ct = default);
    Task AddAuditAsync(SessionAuditEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
