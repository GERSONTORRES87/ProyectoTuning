using Microsoft.EntityFrameworkCore;
using Turning.Application.Features.Events;
using Turning.Application.Features.Surveys;
using Turning.Infrastructure.Persistence;

namespace Turning.Infrastructure.Services;

/// <summary>Lee respuestas enviadas; no inicia encuestas ni modifica sus definiciones.</summary>
public sealed class AdminSurveyResultsService : IAdminSurveyResultsService
{
    private readonly TurningDbContext _db;
    public AdminSurveyResultsService(TurningDbContext db) => _db = db;

    public async Task<AdminSurveyResultsPageDto> GetResultsAsync(int page = 1, int pageSize = 50,
        Guid? participantId = null, CancellationToken ct = default)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var query = _db.SurveyResponses.AsNoTracking().Where(response => !response.IsDeleted
            && response.SubmittedAtUtc != null
            && _db.ExperimentSessions.Any(session => session.Id == response.SessionId && !session.IsDeleted));
        if (participantId.HasValue)
            query = query.Where(response => response.OwnerUserId == participantId.Value);
        var total = await query.CountAsync(ct);
        var offset = (long)(page - 1) * pageSize;
        if (offset >= total) return new AdminSurveyResultsPageDto([], total, page, pageSize);

        var responses = await query.OrderByDescending(response => response.SubmittedAtUtc)
            .ThenBy(response => response.Id)
            .Skip((int)offset).Take(pageSize)
            .Include(response => response.Answers)
            .ToListAsync(ct);
        var sessionIds = responses.Select(response => response.SessionId).Distinct().ToArray();
        var participantIds = responses.Select(response => response.OwnerUserId).Distinct().ToArray();
        var definitionIds = responses.Select(response => response.SurveyDefinitionId).Distinct().ToArray();
        var sessions = await _db.ExperimentSessions.AsNoTracking().Where(session => sessionIds.Contains(session.Id))
            .Select(session => new { session.Id, session.SessionCode, session.Condition, session.Status })
            .ToDictionaryAsync(session => session.Id, ct);
        var participants = await _db.UserAccounts.AsNoTracking().Where(user => participantIds.Contains(user.Id))
            .Select(user => new AdminSurveyParticipantDto(user.Id, user.FullName, user.Email))
            .ToDictionaryAsync(user => user.Id, ct);
        var definitions = await _db.SurveyDefinitions.AsNoTracking().Where(definition => definitionIds.Contains(definition.Id))
            .Include(definition => definition.Questions)
            .ToDictionaryAsync(definition => definition.Id, ct);

        var items = responses.Select(response =>
        {
            var session = sessions[response.SessionId];
            var definition = definitions[response.SurveyDefinitionId];
            return new AdminSurveyResultDto(
                response.Id, response.SessionId, session.SessionCode, session.Condition.ToString(), session.Status.ToString(),
                DateTime.SpecifyKind(response.SubmittedAtUtc!.Value, DateTimeKind.Utc),
                participants[response.OwnerUserId],
                new SurveyDefinitionDto(definition.Id, definition.Code, definition.Version,
                    definition.Questions.OrderBy(question => question.Order)
                        .Select(question => new SurveyQuestionDto(question.Id, question.Code, question.Text, question.Type, question.Required, question.Order))
                        .ToArray()),
                response.Answers.Where(answer => !answer.IsDeleted)
                    .Select(answer => new SurveyAnswerDto(answer.SurveyQuestionId, answer.Value)).ToArray());
        }).ToArray();
        return new AdminSurveyResultsPageDto(items, total, page, pageSize);
    }
}
