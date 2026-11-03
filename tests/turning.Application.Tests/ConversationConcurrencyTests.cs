using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Turning.Application.Features.ConversationTurns;
using Turning.Application.Features.Emotions;
using Turning.Application.Features.ExperimentSessions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Xunit;

namespace Turning.Application.Tests;

public class ConversationConcurrencyTests
{
    private readonly IConversationTurnRepository _turns = Substitute.For<IConversationTurnRepository>();
    private readonly IExperimentSessionRepository _sessions = Substitute.For<IExperimentSessionRepository>();
    private readonly IConversationOrchestrator _orchestrator = Substitute.For<IConversationOrchestrator>();
    private readonly IExperimentEventPublisher _events = Substitute.For<IExperimentEventPublisher>();
    private readonly ITextEmotionAnalysisService _emotions = Substitute.For<ITextEmotionAnalysisService>();
    private readonly IEmotionReadingRepository _readings = Substitute.For<IEmotionReadingRepository>();
    private readonly List<ConversationTurn> _persisted = [];
    private readonly ExperimentSession _session = ExperimentSession.Create(Guid.NewGuid(), ExperimentalCondition.AI);

    public ConversationConcurrencyTests()
    {
        _sessions.GetByIdAsync(_session.Id, Arg.Any<CancellationToken>()).Returns(_session);
        _turns.ListBySessionAsync(_session.Id, Arg.Any<CancellationToken>()).Returns(_ => _persisted.ToArray());
        _turns.GetNextSequenceNumberAsync(_session.Id, Arg.Any<CancellationToken>()).Returns(_ => _persisted.Count + 1);
        _turns.AddAsync(Arg.Any<ConversationTurn>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _persisted.Add(call.Arg<ConversationTurn>());
            return Task.CompletedTask;
        });
        _turns.TrySaveReplyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _emotions.Analyze(Arg.Any<string>()).Returns(new TextEmotionDto("neutral", 0, .3, 0,
            TextEmotionDto.SourceTextAnalysis, false, "test", 1, new Dictionary<string, double>()));
    }

    private ConversationTurnService Service() => new(_turns, _sessions, _orchestrator, _events,
        _emotions, _readings, Options.Create(new SessionOptions()));

    private static InterlocutorReplyOutcome Reply() => new("Respuesta", "openai", "test", 1, false, null, 1, ["openai"]);

    [Fact]
    public async Task ConcurrentMessages_ShouldWaitForTheInFlightReplyAndKeepConsecutiveSequences()
    {
        var generating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _orchestrator.ResolveReplyAsync(_session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
            Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (call.Arg<ConversationTurn>().Message == "Primero")
            {
                generating.SetResult();
                await release.Task;
            }
            return (InterlocutorReplyOutcome?)Reply();
        });

        var first = Service().AddAsync(_session.OwnerUserId, _session.Id, new() { Message = "Primero" });
        await generating.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Service().AddAsync(_session.OwnerUserId, _session.Id, new() { Message = "Segundo" });

        second.IsCompleted.Should().BeFalse();
        _persisted.Should().ContainSingle();
        release.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        _persisted.Select(t => t.SequenceNumber).Should().Equal(1, 2, 3, 4);
        _persisted.Where(t => t.Sender == ConversationActor.Interlocutor).Select(t => t.OriginatingTurnId)
            .Should().Equal(results.Select(r => (Guid?)r.Id));
        results.Should().OnlyContain(r => r.InterlocutorTurn == null);
        _session.ConversationTurnCount.Should().Be(4);
    }

    [Fact]
    public async Task ExpiredBeforeSending_ShouldCloseWithoutPersistingANewTurn()
    {
        _session.Activate(TimeSpan.FromMinutes(1), DateTime.UtcNow.AddMinutes(-2));

        var action = () => Service().AddAsync(_session.OwnerUserId, _session.Id, new() { Message = "Fuera de tiempo" });

        await action.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>()
            .Where(ex => ex.Code == "SESSION_TERMINAL");
        _persisted.Should().BeEmpty();
        _session.Status.Should().Be(ExperimentSessionStatus.TimedOut);
        _events.Received(1).Publish(_session.Id, "SessionEnded", Arg.Any<object>());
        _events.Received(1).Publish(_session.Id, "SurveyAvailable", Arg.Any<object>());
        await _turns.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeadlineDuringGeneration_ShouldCloseWithoutPersistingALateReply()
    {
        _orchestrator.ResolveReplyAsync(_session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
            Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            typeof(ExperimentSession).GetProperty(nameof(ExperimentSession.ExpiresAtUtc))!
                .SetValue(_session, DateTime.UtcNow.AddSeconds(-1));
            return Reply();
        });

        var result = await Service().AddAsync(_session.OwnerUserId, _session.Id, new() { Message = "Antes del límite" },
            isPrivilegedRequester: true);

        _persisted.Should().ContainSingle();
        result.InterlocutorTurn.Should().BeNull();
        result.DegradedReason.Should().Be("SessionClosedBeforeReply");
        _session.Status.Should().Be(ExperimentSessionStatus.TimedOut);
        _events.Received(1).Publish(_session.Id, "SessionEnded", Arg.Any<object>());
        _events.Received(1).Publish(_session.Id, "SurveyAvailable", Arg.Any<object>());
        await _turns.Received(1).TrySaveReplyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClosedWhileGenerating_ShouldKeepTheParticipantMessageWithoutAddingALateReply()
    {
        _orchestrator.ResolveReplyAsync(_session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
            Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _session.Complete();
            return Reply();
        });

        var result = await Service().AddAsync(_session.OwnerUserId, _session.Id, new() { Message = "Antes del cierre" },
            isPrivilegedRequester: true);

        _persisted.Should().ContainSingle();
        result.InterlocutorTurn.Should().BeNull();
        result.DegradedReason.Should().Be("SessionClosedBeforeReply");
        _session.Status.Should().Be(ExperimentSessionStatus.Completed);
        await _sessions.Received(2).ReloadAsync(_session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ParticipantResponse_ShouldHideTheConditionAndProvider()
    {
        _orchestrator.ResolveReplyAsync(_session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
            Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>()).Returns(Reply());

        var result = await Service().AddAsync(_session.OwnerUserId, _session.Id, new() { Message = "Hola" });

        result.Source.Should().Be(ConversationTurnResult.SourcePending);
        result.Ai.Should().BeNull();
        result.InterlocutorTurn.Should().BeNull();
        _persisted.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReplyCommitConflict_ShouldReturnTheDurableMessageAndRecordTheFailure()
    {
        _orchestrator.ResolveReplyAsync(_session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
            Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>()).Returns(Reply());
        _turns.TrySaveReplyAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await Service().AddAsync(_session.OwnerUserId, _session.Id, new() { Message = "Mensaje durable" },
            isPrivilegedRequester: true);

        result.Message.Should().Be("Mensaje durable");
        result.InterlocutorTurn.Should().BeNull();
        result.DegradedReason.Should().Be("SessionChangedBeforeReply");
        _events.Received(1).Publish(_session.Id, ExperimentEventTypes.DegradedOperation, Arg.Any<object>());
        await _turns.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Researcher_ShouldReadButCannotWriteAsTheParticipant()
    {
        var researcherId = Guid.NewGuid();
        await Service().ListAsync(researcherId, _session.Id, isPrivilegedRequester: true);
        var write = () => Service().AddAsync(researcherId, _session.Id, new() { Message = "No es mi sesión" },
            isPrivilegedRequester: true);
        await write.Should().ThrowAsync<Turning.Application.Exceptions.NotFoundException>();
        _persisted.Should().BeEmpty();
    }
}
