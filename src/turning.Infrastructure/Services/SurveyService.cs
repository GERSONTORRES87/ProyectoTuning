using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Turning.Application.Features.Surveys;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;

namespace Turning.Infrastructure.Services;

public sealed class SurveyAppService : ISurveyService
{
    private readonly TurningDbContext _db;
    public SurveyAppService(TurningDbContext db) => _db = db;

    public async Task<SurveyDefinitionDto> GetForSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _db.ExperimentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId && !s.IsDeleted, ct)
            ?? throw new InvalidOperationException("SESSION_NOT_FOUND");
        if (!session.IsTerminal) throw new InvalidOperationException("SURVEY_NOT_AVAILABLE");
        var definition = await SurveyDefinitionSeeder.EnsureDefaultAsync(_db, ct);
        return new SurveyDefinitionDto(definition.Id, definition.Code, definition.Version,
            definition.Questions.OrderBy(q => q.Order)
                .Select(q => new SurveyQuestionDto(q.Id, q.Code, q.Text, q.Type, q.Required, q.Order)).ToArray());
    }

    public async Task<Guid> SubmitAsync(Guid sessionId, SurveySubmitRequest req, CancellationToken ct)
    {
        var attempt = 0;
        try
        {
            return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (attempt++ > 0) _db.ChangeTracker.Clear();
                await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var session = await _db.ExperimentSessions.FirstOrDefaultAsync(s => s.Id == sessionId && !s.IsDeleted, ct)
                    ?? throw new InvalidOperationException("SESSION_NOT_FOUND");
                if (!session.IsTerminal) throw new InvalidOperationException("SURVEY_NOT_AVAILABLE");
                var definition = await _db.SurveyDefinitions.Include(d => d.Questions)
                    .FirstOrDefaultAsync(d => d.Id == req.SurveyDefinitionId, ct)
                    ?? throw new InvalidOperationException("SURVEY_NOT_FOUND");
                var response = await _db.SurveyResponses.Include(r => r.Answers)
                    .FirstOrDefaultAsync(r => r.SessionId == sessionId && r.SurveyDefinitionId == definition.Id, ct);
                if (response is null)
                {
                    response = SurveyResponse.Create(sessionId, session.OwnerUserId, definition.Id);
                    _db.SurveyResponses.Add(response);
                }
                var previousAnswerCount = response.Answers.Count;
                response.Submit(definition, req.Answers);
                _db.Set<SurveyAnswer>().AddRange(response.Answers.Skip(previousAnswerCount));
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return response.Id;
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new InvalidOperationException("ALREADY_SUBMITTED");
        }
    }
}
