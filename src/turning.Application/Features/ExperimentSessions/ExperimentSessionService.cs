using Microsoft.Extensions.Options;
using Turning.Application.Exceptions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Turning.Domain.Exceptions;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;

namespace Turning.Application.Features.ExperimentSessions;

/// <summary>
/// Implementa el bootstrap inicial de sesiones experimentales.
/// </summary>
public sealed class ExperimentSessionService : IExperimentSessionService
{
    private readonly IExperimentSessionRepository _repo;
    private readonly SessionOptions _opts;
    private readonly IAssignmentService _assignment;
    private readonly IExperimentEventPublisher? _events;
    public ExperimentSessionService(IExperimentSessionRepository repo, IOptions<SessionOptions> opts, IAssignmentService? assignment = null, IExperimentEventPublisher? events = null) { _repo = repo; _opts = opts.Value; _assignment = assignment!; _events = events; }

    public async Task<ExperimentSessionSnapshot> CreateBootstrapSessionAsync(Guid ownerUserId, CreateExperimentSessionRequest? request = null, CancellationToken cancellationToken = default, bool isPrivilegedRequester = false)
    {
        if (ownerUserId == Guid.Empty) throw new TurningApplicationException("No fue posible resolver el usuario autenticado para crear la sesion.", "SESSION_INVALID_OWNER");
        using var operation = await SessionOperationLock.AcquireAsync(Guid.Empty, cancellationToken);
        var preferred = string.IsNullOrWhiteSpace(request?.PreferredCondition) ? (ExperimentalCondition?)null : ParseCondition(request!.PreferredCondition);
        var sessionId = Guid.NewGuid();
        ExperimentalCondition condition;
        if (_assignment != null) { var a = await _assignment.AssignAsync(sessionId, preferred, cancellationToken); condition = a.Condition; }
        else condition = preferred ?? ExperimentalCondition.AI;
        var session = ExperimentSession.Create(ownerUserId, condition, explicitId: sessionId);
        await _repo.AddAsync(session, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        return Map(session, ownerUserId, isPrivilegedRequester);
    }
    public async Task<ExperimentSessionSnapshot?> GetLatestSessionAsync(Guid ownerUserId, CancellationToken cancellationToken = default, bool isPrivilegedRequester = false)
    {
        if (ownerUserId == Guid.Empty) throw new TurningApplicationException("No fue posible resolver el usuario autenticado.", "SESSION_INVALID_OWNER");
        var s = await _repo.GetLatestByOwnerAsync(ownerUserId, cancellationToken);
        return s is null ? null : Map(s, ownerUserId, isPrivilegedRequester);
    }
    public async Task<ExperimentSessionSnapshot> GetByIdAsync(Guid id, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default)
    {
        var s = await GetAccessibleSessionAsync(id, requestingUserId, isPrivilegedRequester, SessionAccessLevel.Interlocutor, ct);
        return Map(s, requestingUserId, isPrivilegedRequester);
    }
    public async Task<PagedSessionsResult> ListByParticipantAsync(Guid participantId, Guid requestingUserId, bool isPrivilegedRequester, int page, int pageSize, CancellationToken ct = default)
    {
        if (requestingUserId != participantId && !isPrivilegedRequester) throw new TurningApplicationException("No autorizado para consultar sesiones de otro participante.", "SESSION_FORBIDDEN");
        if (page < 1 || pageSize < 1 || pageSize > 50) throw new TurningApplicationException("Paginacion invalida.", "SESSION_INVALID_PAGE");
        var items = await _repo.ListByOwnerAsync(participantId, page, pageSize, ct);
        var total = await _repo.CountByOwnerAsync(participantId, ct);
        return new PagedSessionsResult { Items = items.Select(s => Map(s, requestingUserId, isPrivilegedRequester)).ToList(), Total = total, Page = page, PageSize = pageSize };
    }
    public async Task<ExperimentSessionSnapshot> ActivateAsync(Guid id, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default)
    {
        using var operation = await SessionOperationLock.AcquireAsync(id, ct);
        var s = await GetAccessibleSessionAsync(id, requestingUserId, false, SessionAccessLevel.Owner, ct);
        await _repo.ReloadAsync(s, ct);
        var previous = s.Status;
        try { s.Activate(TimeSpan.FromSeconds(_opts.DurationSeconds)); } catch (DomainException ex) { throw new TurningApplicationException(ex.Message, "SESSION_CONFLICT"); }
        await SessionTransitionRecorder.RecordAsync(s, previous, requestingUserId, "User", _repo, _events, ct: ct);
        try { await _repo.SaveChangesAsync(ct); } catch (Exception ex) when (ex.GetType().Name.Contains("Concurrency")) { throw new TurningApplicationException("Conflicto de concurrencia.", "SESSION_CONFLICT"); }
        return Map(s, requestingUserId, isPrivilegedRequester);
    }
    public async Task<ExperimentSessionSnapshot> CompleteAsync(Guid id, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default)
    {
        using var operation = await SessionOperationLock.AcquireAsync(id, ct);
        var s = await GetAccessibleSessionAsync(id, requestingUserId, false, SessionAccessLevel.Owner, ct);
        await _repo.ReloadAsync(s, ct);
        var previous = s.Status;
        try { s.Complete(); } catch (DomainException ex) { throw new TurningApplicationException(ex.Message, "SESSION_CONFLICT"); }
        await SessionTransitionRecorder.RecordAsync(s, previous, requestingUserId, "User", _repo, _events, ct: ct);
        try { await _repo.SaveChangesAsync(ct); } catch (Exception ex) when (ex.GetType().Name.Contains("Concurrency")) { throw new TurningApplicationException("Conflicto de concurrencia.", "SESSION_CONFLICT"); }
        return Map(s, requestingUserId, isPrivilegedRequester);
    }
    public async Task<ExperimentSessionSnapshot> CancelAsync(Guid id, string reason, Guid actorId, CancellationToken ct = default)
    {
        if (actorId == Guid.Empty) throw new TurningApplicationException("No fue posible resolver el usuario autenticado.", "SESSION_INVALID_OWNER");
        if (string.IsNullOrWhiteSpace(reason)) throw new TurningApplicationException("Motivo obligatorio.", "SESSION_INVALID_REASON");
        using var operation = await SessionOperationLock.AcquireAsync(id, ct);
        var s = await _repo.GetByIdAsync(id, ct) ?? throw new TurningApplicationException("Sesion no encontrada.", "SESSION_NOT_FOUND");
        await _repo.ReloadAsync(s, ct);
        var previous = s.Status;
        try { s.Cancel(reason); } catch (DomainException ex) { throw new TurningApplicationException(ex.Message, "SESSION_CONFLICT"); }
        await SessionTransitionRecorder.RecordAsync(s, previous, actorId, "Administrator", _repo, _events, reason, ct);
        try { await _repo.SaveChangesAsync(ct); } catch (Exception ex) when (ex.GetType().Name.Contains("Concurrency")) { throw new TurningApplicationException("Conflicto de concurrencia.", "SESSION_CONFLICT"); }
        return Map(s, actorId, true);
    }

    /// <summary>
    /// Resuelve una sesion aplicando aislamiento por propietario: un solicitante que no
    /// es dueno ni privilegiado recibe el mismo SESSION_NOT_FOUND que si no existiera,
    /// para no filtrar la existencia de sesiones ajenas por enumeracion de GUIDs.
    /// </summary>
    private async Task<ExperimentSession> GetAccessibleSessionAsync(
        Guid id,
        Guid requestingUserId,
        bool isPrivilegedRequester,
        SessionAccessLevel minimumLevel,
        CancellationToken ct)
    {
        var s = await _repo.GetByIdAsync(id, ct) ?? throw new TurningApplicationException("Sesion no encontrada.", "SESSION_NOT_FOUND");

        if (ResolveAccessLevel(s, requestingUserId, isPrivilegedRequester) < minimumLevel)
            throw new TurningApplicationException("Sesion no encontrada.", "SESSION_NOT_FOUND");

        return s;
    }

    /// <inheritdoc />
    public async Task<bool> IsSessionAccessibleAsync(Guid sessionId, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default)
    {
        var level = await GetAccessLevelAsync(sessionId, requestingUserId, isPrivilegedRequester, ct);
        return level >= SessionAccessLevel.Owner;
    }

    /// <inheritdoc />
    public async Task<SessionAccessLevel> GetAccessLevelAsync(Guid sessionId, Guid requestingUserId, bool isPrivilegedRequester, CancellationToken ct = default)
    {
        var s = await _repo.GetByIdAsync(sessionId, ct);
        return s is null ? SessionAccessLevel.None : ResolveAccessLevel(s, requestingUserId, isPrivilegedRequester);
    }

    /// <inheritdoc />
    public async Task<ExperimentSessionSnapshot> JoinAsInterlocutorAsync(string sessionCode, Guid interlocutorUserId, CancellationToken ct = default)
    {
        if (interlocutorUserId == Guid.Empty)
            throw new TurningApplicationException("No fue posible resolver el usuario autenticado.", "SESSION_INVALID_OWNER");

        if (string.IsNullOrWhiteSpace(sessionCode))
            throw new TurningApplicationException("El codigo de sesion es obligatorio.", "SESSION_INVALID_CODE");

        var s = await _repo.GetByCodeAsync(sessionCode.Trim(), ct)
            ?? throw new TurningApplicationException("Sesion no encontrada.", "SESSION_NOT_FOUND");

        if (s.OwnerUserId == interlocutorUserId)
            throw new TurningApplicationException("No puedes ser interlocutor de tu propia sesion.", "SESSION_SELF_PAIRING");

        try { s.AssignInterlocutor(interlocutorUserId); }
        catch (DomainException ex) { throw new TurningApplicationException(ex.Message, "SESSION_CONFLICT"); }

        await SaveWithConcurrencyGuardAsync(ct);
        return Map(s, interlocutorUserId, false);
    }

    /// <inheritdoc />
    public async Task<ExperimentSessionSnapshot> AssignInterlocutorAsync(Guid sessionId, Guid interlocutorUserId, CancellationToken ct = default)
    {
        if (interlocutorUserId == Guid.Empty)
            throw new TurningApplicationException("El interlocutor es obligatorio.", "SESSION_INVALID_INTERLOCUTOR");

        var s = await _repo.GetByIdAsync(sessionId, ct)
            ?? throw new TurningApplicationException("Sesion no encontrada.", "SESSION_NOT_FOUND");

        try { s.AssignInterlocutor(interlocutorUserId); }
        catch (DomainException ex) { throw new TurningApplicationException(ex.Message, "SESSION_CONFLICT"); }

        await SaveWithConcurrencyGuardAsync(ct);
        return Map(s);
    }

    /// <inheritdoc />
    public async Task<ExperimentSessionSnapshot> ReleaseInterlocutorAsync(Guid sessionId, CancellationToken ct = default)
    {
        var s = await _repo.GetByIdAsync(sessionId, ct)
            ?? throw new TurningApplicationException("Sesion no encontrada.", "SESSION_NOT_FOUND");

        s.ReleaseInterlocutor();
        await SaveWithConcurrencyGuardAsync(ct);
        return Map(s);
    }

    /// <inheritdoc />
    public async Task<PagedSessionsResult> ListAwaitingInterlocutorAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 50)
            throw new TurningApplicationException("Paginacion invalida.", "SESSION_INVALID_PAGE");

        var items = await _repo.ListAwaitingInterlocutorAsync(page, pageSize, ct);
        var total = await _repo.CountAwaitingInterlocutorAsync(ct);

        return new PagedSessionsResult { Items = items.Select(Map).ToList(), Total = total, Page = page, PageSize = pageSize };
    }

    private async Task SaveWithConcurrencyGuardAsync(CancellationToken ct)
    {
        try { await _repo.SaveChangesAsync(ct); }
        catch (Exception ex) when (ex.GetType().Name.Contains("Concurrency"))
        {
            // Dos personas intentando tomar la misma sesion a la vez: la segunda pierde.
            throw new TurningApplicationException("Conflicto de concurrencia.", "SESSION_CONFLICT");
        }
    }

    /// <summary>
    /// Unica definicion de que puede hacer alguien sobre una sesion.
    /// </summary>
    /// <remarks>
    /// El orden importa: el dueno que ademas tuviera rol privilegiado sigue siendo dueno, y
    /// el interlocutor es el nivel mas bajo porque solo participa en la conversacion.
    /// </remarks>
    private static SessionAccessLevel ResolveAccessLevel(ExperimentSession session, Guid requestingUserId, bool isPrivilegedRequester)
    {
        if (session.OwnerUserId == requestingUserId)
            return SessionAccessLevel.Owner;

        if (isPrivilegedRequester)
            return SessionAccessLevel.Privileged;

        if (session.IsInterlocutor(requestingUserId))
            return SessionAccessLevel.Interlocutor;

        return SessionAccessLevel.None;
    }

    private static ExperimentalCondition ParseCondition(string? preferredCondition)
    {
        if (string.IsNullOrWhiteSpace(preferredCondition))
        {
            return ExperimentalCondition.AI;
        }

        return preferredCondition.Trim().ToUpperInvariant() switch
        {
            "AI" => ExperimentalCondition.AI,
            "HUMAN" => ExperimentalCondition.Human,
            _ => throw new TurningApplicationException("La condicion experimental debe ser AI o Human.", "SESSION_INVALID_CONDITION")
        };
    }

    private static ExperimentSessionSnapshot Map(ExperimentSession s) => ExperimentSessionSnapshotMapper.ToSnapshot(s);
    private static ExperimentSessionSnapshot Map(ExperimentSession s, Guid userId, bool privileged) =>
        ExperimentSessionSnapshotMapper.ToSnapshot(s, userId, privileged);
}
