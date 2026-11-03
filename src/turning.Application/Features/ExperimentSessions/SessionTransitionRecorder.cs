using Turning.Application.Interfaces;
using Turning.Domain.Entities;

namespace Turning.Application.Features.ExperimentSessions;

public static class SessionTransitionRecorder
{
    public static async Task RecordAsync(ExperimentSession session, ExperimentSessionStatus previous,
        Guid? actorId, string actorType, IExperimentSessionRepository repository,
        IExperimentEventPublisher? publisher, string? reason = null, CancellationToken ct = default)
    {
        if (previous == session.Status) return;
        await repository.AddAuditAsync(SessionAuditEntry.Create(session.Id, previous, session.Status,
            actorType, actorId, reason), ct);
        publisher?.Publish(session.Id, "SessionStateChanged", new
        {
            previousStatus = previous.ToString(), status = session.Status.ToString(), actorId, actorType, reason
        });
        if (!session.IsTerminal) return;
        publisher?.Publish(session.Id, "SessionEnded", new { status = session.Status.ToString(), actorId, reason });
        publisher?.Publish(session.Id, "SurveyAvailable", new { sessionId = session.Id });
    }
}
