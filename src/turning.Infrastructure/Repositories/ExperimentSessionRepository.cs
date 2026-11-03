using Microsoft.EntityFrameworkCore;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;

namespace Turning.Infrastructure.Repositories;

/// <summary>
/// Repositorio EF Core para sesiones experimentales.
/// </summary>
public sealed class ExperimentSessionRepository : IExperimentSessionRepository
{
    private readonly TurningDbContext _dbContext;

    /// <summary>
    /// Constructor del repositorio de sesiones.
    /// </summary>
    public ExperimentSessionRepository(TurningDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task AddAsync(ExperimentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _dbContext.ExperimentSessions.AddAsync(session, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ExperimentSession?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ExperimentSessions
            .FirstOrDefaultAsync(session => session.Id == sessionId && !session.IsDeleted, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ExperimentSession?> GetLatestByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ExperimentSessions
            .Where(session => session.OwnerUserId == ownerUserId && !session.IsDeleted)
            .OrderByDescending(session => session.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<ExperimentSession>> ListByOwnerAsync(Guid ownerUserId, int page, int pageSize, CancellationToken ct = default)
        => _dbContext.ExperimentSessions.Where(s => s.OwnerUserId == ownerUserId && !s.IsDeleted).OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    public Task<int> CountByOwnerAsync(Guid ownerUserId, CancellationToken ct = default)
        => _dbContext.ExperimentSessions.CountAsync(s => s.OwnerUserId == ownerUserId && !s.IsDeleted, ct);
    public Task<ExperimentSession?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _dbContext.ExperimentSessions.FirstOrDefaultAsync(s => s.SessionCode == code && !s.IsDeleted, ct);
    /// <inheritdoc />
    public Task<List<ExperimentSession>> ListAwaitingInterlocutorAsync(int page, int pageSize, CancellationToken ct = default)
        => AwaitingInterlocutorQuery()
            .OrderBy(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<int> CountAwaitingInterlocutorAsync(CancellationToken ct = default)
        => AwaitingInterlocutorQuery().CountAsync(ct);

    /// <summary>
    /// Sesiones Human sin interlocutor y todavia vivas. Se ordenan por antiguedad: el que
    /// lleva mas tiempo esperando se atiende primero.
    /// </summary>
    private IQueryable<ExperimentSession> AwaitingInterlocutorQuery()
        => _dbContext.ExperimentSessions.Where(s =>
            !s.IsDeleted
            && s.Condition == ExperimentalCondition.Human
            && s.InterlocutorUserId == null
            && s.Status != ExperimentSessionStatus.Completed
            && s.Status != ExperimentSessionStatus.TimedOut
            && s.Status != ExperimentSessionStatus.Cancelled);

    public Task ReloadAsync(ExperimentSession session, CancellationToken ct = default)
        => _dbContext.Entry(session).ReloadAsync(ct);

    public async Task AddAuditAsync(SessionAuditEntry entry, CancellationToken ct = default)
        => await _dbContext.SessionAuditEntries.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);
}
