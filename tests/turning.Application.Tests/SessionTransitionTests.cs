using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Turning.Application.Features.ExperimentSessions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Xunit;

namespace Turning.Application.Tests;

public class SessionTransitionTests
{
    [Fact]
    public async Task Observer_CannotActivateOrCompleteAnotherParticipantsSession()
    {
        var sessions = Substitute.For<IExperimentSessionRepository>();
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.Human);
        var observer = Guid.NewGuid();
        sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var service = new ExperimentSessionService(sessions, Options.Create(new SessionOptions()));

        var activate = () => service.ActivateAsync(session.Id, observer, true);
        await activate.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>()
            .Where(ex => ex.Code == "SESSION_NOT_FOUND");
        session.Activate(TimeSpan.FromMinutes(5));
        var complete = () => service.CompleteAsync(session.Id, observer, true);
        await complete.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>()
            .Where(ex => ex.Code == "SESSION_NOT_FOUND");
        session.Status.Should().Be(ExperimentSessionStatus.Active);
        await sessions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActivateAndComplete_ShouldAuditTheActorAndPublishCloseAndSurveyEvents()
    {
        var sessions = Substitute.For<IExperimentSessionRepository>();
        var events = Substitute.For<IExperimentEventPublisher>();
        var owner = Guid.NewGuid();
        var session = ExperimentSession.Create(owner, ExperimentalCondition.AI);
        sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var service = new ExperimentSessionService(sessions, Options.Create(new SessionOptions()), events: events);

        await service.ActivateAsync(session.Id, owner, false);
        await service.CompleteAsync(session.Id, owner, false);

        await sessions.Received(1).AddAuditAsync(Arg.Is<SessionAuditEntry>(a => a.ActorId == owner &&
            a.PreviousStatus == ExperimentSessionStatus.Created && a.NewStatus == ExperimentSessionStatus.Active), Arg.Any<CancellationToken>());
        await sessions.Received(1).AddAuditAsync(Arg.Is<SessionAuditEntry>(a => a.ActorId == owner &&
            a.PreviousStatus == ExperimentSessionStatus.Active && a.NewStatus == ExperimentSessionStatus.Completed), Arg.Any<CancellationToken>());
        events.Received(2).Publish(session.Id, "SessionStateChanged", Arg.Any<object>());
        events.Received(1).Publish(session.Id, "SessionEnded", Arg.Any<object>());
        events.Received(1).Publish(session.Id, "SurveyAvailable", Arg.Any<object>());
        await sessions.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancellation_ShouldAuditReasonAndPublishSurveyAvailability()
    {
        var sessions = Substitute.For<IExperimentSessionRepository>();
        var events = Substitute.For<IExperimentEventPublisher>();
        var session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.Human);
        var administrator = Guid.NewGuid();
        sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        var service = new ExperimentSessionService(sessions, Options.Create(new SessionOptions()), events: events);

        await service.CancelAsync(session.Id, "Error de protocolo", administrator);

        await sessions.Received(1).AddAuditAsync(Arg.Is<SessionAuditEntry>(a => a.ActorId == administrator &&
            a.Reason == "Error de protocolo" && a.NewStatus == ExperimentSessionStatus.Cancelled), Arg.Any<CancellationToken>());
        events.Received(1).Publish(session.Id, "SessionStateChanged", Arg.Any<object>());
        events.Received(1).Publish(session.Id, "SessionEnded", Arg.Any<object>());
        events.Received(1).Publish(session.Id, "SurveyAvailable", Arg.Any<object>());
    }

    [Fact]
    public void ParticipantSnapshot_ShouldHideTheConditionAndPairingEvenAfterCompletion()
    {
        var owner = Guid.NewGuid();
        var session = ExperimentSession.Create(owner, ExperimentalCondition.Human);
        session.AssignInterlocutor(Guid.NewGuid());
        session.Activate(TimeSpan.FromMinutes(5));
        session.Complete();

        var participant = ExperimentSessionSnapshotMapper.ToSnapshot(session, owner, false);
        var researcher = ExperimentSessionSnapshotMapper.ToSnapshot(session, Guid.NewGuid(), true);

        participant.ParticipationRole.Should().Be("Owner");
        participant.Condition.Should().Be("Hidden");
        participant.InterlocutorUserId.Should().BeNull();
        participant.InterlocutorJoinedAtUtc.Should().BeNull();
        participant.HasInterlocutor.Should().BeFalse();
        participant.IsAwaitingInterlocutor.Should().BeFalse();
        researcher.ParticipationRole.Should().Be("Observer");
        researcher.Condition.Should().Be("Human");
        researcher.HasInterlocutor.Should().BeTrue();
    }
}
