using Microsoft.EntityFrameworkCore;
using Turning.Application.Features.Events;
using Turning.Application.Features.ExperimentSessions;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;

namespace Turning.Infrastructure.Services;

public sealed class EventService : IEventService
{
    private readonly TurningDbContext _db;
    public EventService(TurningDbContext db) => _db = db;

    public async Task<IReadOnlyList<EventDto>> ListAsync(Guid sessionId, Guid? after, CancellationToken ct, bool includeInternalDetails = true)
    {
        if (!await _db.ExperimentSessions.AnyAsync(s => s.Id == sessionId && !s.IsDeleted, ct))
            throw new InvalidOperationException("SESSION_NOT_FOUND");
        var now = DateTime.UtcNow;
        var query = _db.ExperimentEvents.AsNoTracking().Where(e => e.SessionId == sessionId && e.ExpiresAtUtc > now);
        if (after.HasValue && after.Value != Guid.Empty)
        {
            var cursor = await query.FirstOrDefaultAsync(e => e.Id == after.Value, ct)
                ?? throw new InvalidOperationException("EVENT_EXPIRED");
            query = query.Where(e => e.OccurredAtUtc > cursor.OccurredAtUtc
                || (e.OccurredAtUtc == cursor.OccurredAtUtc && e.Id.CompareTo(cursor.Id) > 0));
        }
        var entries = await query.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).Take(100).ToListAsync(ct);
        return entries.Where(e => includeInternalDetails || EventProjection.IsVisibleToParticipant(e))
            .Select(e => EventProjection.ToDto(e, includeInternalDetails)).ToArray();
    }
}

public sealed class ResultsService : IResultsService
{
    private readonly TurningDbContext _db;
    public ResultsService(TurningDbContext db) => _db = db;

    public async Task<SessionResultDto> GetResultAsync(Guid sessionId, CancellationToken ct, Guid? requestingUserId = null, bool isPrivilegedRequester = true)
    {
        var session = await _db.ExperimentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && !s.IsDeleted, ct)
            ?? throw new InvalidOperationException("SESSION_NOT_FOUND");
        var turns = await _db.ConversationTurns.AsNoTracking().Where(t => t.ExperimentSessionId == sessionId && !t.IsDeleted)
            .OrderBy(t => t.SequenceNumber).ToListAsync(ct);
        var emotions = await _db.EmotionReadings.AsNoTracking().Where(e => e.SessionId == sessionId && !e.IsDeleted)
            .OrderBy(e => e.CapturedAtUtc).ThenBy(e => e.Id).ToListAsync(ct);
        var avatars = await _db.AvatarExpressions.AsNoTracking().Where(a => a.SessionId == sessionId && !a.IsDeleted)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
        var responses = await _db.SurveyResponses.AsNoTracking().Where(r => r.SessionId == sessionId && !r.IsDeleted)
            .Include(r => r.Answers).OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).ToListAsync(ct);
        var events = await _db.ExperimentEvents.AsNoTracking().Where(e => e.SessionId == sessionId && e.Type == "DegradedOperation")
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).ToListAsync(ct);

        return new SessionResultDto
        {
            Session = ExperimentSessionSnapshotMapper.ToSnapshot(session, requestingUserId, isPrivilegedRequester),
            Conversation = turns.Select(t => new ConversationTurnResultDto(t.Id, t.SequenceNumber, t.Sender.ToString(), t.Message, isPrivilegedRequester ? t.OriginatingTurnId : null, t.CreatedAt)).ToArray(),
            EmotionReadings = emotions.Select(e => new EmotionReadingDto(e.Id, e.ConversationTurnId, e.Source, e.Emotion, e.Score,
                isPrivilegedRequester ? e.Provider : "analysis", e.IsDegraded, e.CapturedAtUtc)).ToArray(),
            AvatarExpressions = avatars.Select(a => new AvatarExpressionDto(a.Id, a.EmotionReadingId, a.ExpressionName, a.Intensity, a.ParametersJson, a.IsFallback, a.CreatedAt)).ToArray(),
            Survey = responses.Select(r => new SurveyResponseDto(r.Id, r.SurveyDefinitionId, r.StartedAtUtc, r.SubmittedAtUtc,
                r.Answers.Where(a => !a.IsDeleted).OrderBy(a => a.SurveyQuestionId).Select(a => new SurveyAnswerDto(a.SurveyQuestionId, a.Value)).ToArray())).ToArray(),
            DegradedEvents = events.Where(e => isPrivilegedRequester || EventProjection.IsVisibleToParticipant(e))
                .Select(e => EventProjection.ToDto(e, isPrivilegedRequester)).ToArray()
        };
    }

    public async Task<PagedSessionsResult> ListAsync(DateTime? from, DateTime? to, string? condition, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("INVALID_PAGINATION");
        if (from.HasValue && to.HasValue && from > to)
            throw new ArgumentException("INVALID_DATE_RANGE");
        var query = _db.ExperimentSessions.AsNoTracking().Where(s => !s.IsDeleted);
        if (from.HasValue) query = query.Where(s => s.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(s => s.CreatedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(condition))
        {
            if (!Enum.TryParse<ExperimentalCondition>(condition, true, out var parsed)
                || !Enum.IsDefined(parsed) || int.TryParse(condition, out _))
                throw new ArgumentException("INVALID_CONDITION");
            query = query.Where(s => s.Condition == parsed);
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedSessionsResult
        {
            Items = items.Select(s => ExperimentSessionSnapshotMapper.ToSnapshot(s)).ToList(),
            Total = total, Page = page, PageSize = pageSize
        };
    }
}
