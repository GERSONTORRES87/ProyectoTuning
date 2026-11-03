using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Turning.Application.Exceptions;
using Turning.Application.Features.ConversationTurns;
using Turning.Application.Features.Emotions;
using Turning.Application.Features.ExperimentSessions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Xunit;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;

namespace Turning.Application.Tests;

/// <summary>
/// Pruebas de la conversación con un interlocutor humano real.
/// </summary>
public class ConversationInterlocutorTests
{
    private readonly IConversationTurnRepository _turns = Substitute.For<IConversationTurnRepository>();
    private readonly IExperimentSessionRepository _sessions = Substitute.For<IExperimentSessionRepository>();
    private readonly IConversationOrchestrator _orchestrator = Substitute.For<IConversationOrchestrator>();
    private readonly IExperimentEventPublisher _events = Substitute.For<IExperimentEventPublisher>();
    private readonly ITextEmotionAnalysisService _emotions = Substitute.For<ITextEmotionAnalysisService>();
    private readonly IEmotionReadingRepository _readings = Substitute.For<IEmotionReadingRepository>();

    public ConversationInterlocutorTests()
    {
        _emotions.Analyze(Arg.Any<string>()).Returns(new TextEmotionDto(
            "neutral", 0, 0.3, 0, TextEmotionDto.SourceFallback, true, "lexicon-test", 1,
            new Dictionary<string, double>()));

        _orchestrator.ResolveReplyAsync(
                Arg.Any<ExperimentSession>(), Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
                Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>())
            .Returns((InterlocutorReplyOutcome?)null);
    }

    private ConversationTurnService CreateService() =>
        new(_turns, _sessions, _orchestrator, _events, _emotions, _readings, Options.Create(new SessionOptions()));

    private ExperimentSession ArrangePairedSession(Guid ownerId, Guid interlocutorId, bool started = true)
    {
        var session = ExperimentSession.Create(ownerId, ExperimentalCondition.Human);
        session.AssignInterlocutor(interlocutorId);

        // Solo el dueño inicia la sesión: el interlocutor necesita una sesión ya Active.
        if (started) session.Activate(TimeSpan.FromMinutes(5));

        _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _turns.ListBySessionAsync(session.Id, Arg.Any<CancellationToken>()).Returns([]);
        _turns.GetNextSequenceNumberAsync(session.Id, Arg.Any<CancellationToken>()).Returns(1);
        return session;
    }

    [Fact]
    public async Task AddAsync_ShouldLabelTheOwnerAsParticipant()
    {
        var ownerId = Guid.NewGuid();
        var session = ArrangePairedSession(ownerId, Guid.NewGuid());

        var result = await CreateService().AddAsync(ownerId, session.Id, new AddConversationTurnRequest
        {
            Message = "Hola, soy el participante."
        });

        result.Sender.Should().Be("Participant");
        result.Source.Should().Be(ConversationTurnResult.SourcePending);
    }

    [Fact]
    public async Task AddAsync_ShouldLabelTheInterlocutorFromTheIdentity()
    {
        var interlocutorId = Guid.NewGuid();
        var session = ArrangePairedSession(Guid.NewGuid(), interlocutorId);

        var result = await CreateService().AddAsync(interlocutorId, session.Id, new AddConversationTurnRequest
        {
            Message = "Hola, soy la persona que responde."
        });

        // El emisor sale de quién llama, no de lo que el cliente declare.
        result.Sender.Should().Be("Interlocutor");
        result.Source.Should().Be(ConversationTurnResult.SourceHuman);
    }

    [Fact]
    public async Task AddAsync_ShouldRejectAnOwnerClaimingToBeTheInterlocutor()
    {
        var ownerId = Guid.NewGuid();
        var session = ArrangePairedSession(ownerId, Guid.NewGuid());

        var act = async () => await CreateService().AddAsync(ownerId, session.Id, new AddConversationTurnRequest
        {
            Sender = "Interlocutor",
            Message = "Me hago pasar por el otro."
        });

        // Es exactamente lo que el frontend permitía con un desplegable.
        await act.Should().ThrowAsync<TurningApplicationException>()
            .Where(ex => ex.Code == "CONVERSATION_SENDER_NOT_ALLOWED");
    }

    [Fact]
    public async Task AddAsync_ShouldAcceptASenderThatMatchesTheRole()
    {
        var interlocutorId = Guid.NewGuid();
        var session = ArrangePairedSession(Guid.NewGuid(), interlocutorId);

        var result = await CreateService().AddAsync(interlocutorId, session.Id, new AddConversationTurnRequest
        {
            Sender = "Interlocutor",
            Message = "Coincide con mi papel."
        });

        result.Sender.Should().Be("Interlocutor");
    }

    [Fact]
    public async Task AddAsync_ShouldRejectAStranger()
    {
        var session = ArrangePairedSession(Guid.NewGuid(), Guid.NewGuid());

        var act = async () => await CreateService().AddAsync(Guid.NewGuid(), session.Id, new AddConversationTurnRequest
        {
            Message = "No tengo nada que ver con esta sesión."
        });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AddAsync_ShouldNeverCallTheAi_InHumanSessions()
    {
        var interlocutorId = Guid.NewGuid();
        var session = ArrangePairedSession(Guid.NewGuid(), interlocutorId);

        await CreateService().AddAsync(interlocutorId, session.Id, new AddConversationTurnRequest
        {
            Message = "Respondo yo, que soy una persona."
        });

        // El orquestador se consulta igual, pero devuelve null en condición Human: la
        // decisión vive en un solo sitio y no se duplica aquí.
        await _orchestrator.Received(1).ResolveReplyAsync(
            Arg.Any<ExperimentSession>(), Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
            Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_ShouldRejectTheInterlocutor_BeforeTheOwnerStartsTheSession()
    {
        var interlocutorId = Guid.NewGuid();
        var session = ArrangePairedSession(Guid.NewGuid(), interlocutorId, started: false);

        var act = async () => await CreateService().AddAsync(interlocutorId, session.Id, new AddConversationTurnRequest
        {
            Message = "Intento abrir la sesión yo."
        });

        await act.Should().ThrowAsync<TurningApplicationException>()
            .Where(ex => ex.Code == "SESSION_NOT_STARTED");
        session.Status.Should().Be(ExperimentSessionStatus.Created);
        session.ConversationTurnCount.Should().Be(0);
        await _turns.DidNotReceive().AddAsync(Arg.Any<ConversationTurn>(), Arg.Any<CancellationToken>());
        await _readings.DidNotReceive().AddAsync(
            Arg.Any<EmotionReading>(), Arg.Any<AvatarExpression>(), Arg.Any<CancellationToken>());
        _events.DidNotReceive().Publish(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public async Task AddAsync_ShouldLetTheOwnerStartTheSession_WithTheFirstTurn()
    {
        var ownerId = Guid.NewGuid();
        var session = ArrangePairedSession(ownerId, Guid.NewGuid(), started: false);

        await CreateService().AddAsync(ownerId, session.Id, new AddConversationTurnRequest { Message = "Empiezo yo." });

        session.Status.Should().Be(ExperimentSessionStatus.Active);
    }

    [Fact]
    public async Task AddAsync_ShouldKeepThePrivilegedFlagForProjectionOnly_WhenTheCallerIsTheAssignedInterlocutor()
    {
        // Un Researcher/Administrator asignado como interlocutor escribe como interlocutor,
        // no como observador: el flag privilegiado solo cambia qué ve, no qué papel tiene.
        var interlocutorId = Guid.NewGuid();
        var session = ArrangePairedSession(Guid.NewGuid(), interlocutorId);

        var result = await CreateService().AddAsync(interlocutorId, session.Id,
            new AddConversationTurnRequest { Message = "Respondo siendo investigador." },
            isPrivilegedRequester: true);

        result.Sender.Should().Be("Interlocutor");
    }

    [Fact]
    public async Task AddAsync_ShouldRejectAnUnassignedPrivilegedObserver_WithoutPersistingAnything()
    {
        var session = ArrangePairedSession(Guid.NewGuid(), Guid.NewGuid());

        var act = async () => await CreateService().AddAsync(Guid.NewGuid(), session.Id,
            new AddConversationTurnRequest { Message = "Observador privilegiado." },
            isPrivilegedRequester: true);

        await act.Should().ThrowAsync<NotFoundException>();
        await _turns.DidNotReceive().AddAsync(Arg.Any<ConversationTurn>(), Arg.Any<CancellationToken>());
        _events.DidNotReceive().Publish(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>());
    }

    [Fact]
    public async Task ListAsync_ShouldLetTheInterlocutorReadTheConversation()
    {
        var interlocutorId = Guid.NewGuid();
        var session = ArrangePairedSession(Guid.NewGuid(), interlocutorId);
        _turns.ListBySessionAsync(session.Id, Arg.Any<CancellationToken>()).Returns(new[]
        {
            ConversationTurn.Create(session.Id, 1, ConversationActor.Participant, "Hola")
        });

        var turns = await CreateService().ListAsync(interlocutorId, session.Id);

        turns.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListAsync_ShouldRejectAStranger()
    {
        var session = ArrangePairedSession(Guid.NewGuid(), Guid.NewGuid());

        var act = async () => await CreateService().ListAsync(Guid.NewGuid(), session.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
