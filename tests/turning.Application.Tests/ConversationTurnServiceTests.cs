using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text.Json;
using Turning.Application.Features.ConversationTurns;
using Turning.Application.Features.Emotions;
using Turning.Application.Features.ExperimentSessions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Xunit;

namespace Turning.Application.Tests;

/// <summary>
/// Pruebas del servicio de conversación.
/// </summary>
public class ConversationTurnServiceTests
{
    private readonly IConversationTurnRepository _conversationTurnRepository = Substitute.For<IConversationTurnRepository>();
    private readonly IExperimentSessionRepository _experimentSessionRepository = Substitute.For<IExperimentSessionRepository>();
    private readonly IConversationOrchestrator _orchestrator = Substitute.For<IConversationOrchestrator>();
    private readonly IExperimentEventPublisher _eventPublisher = Substitute.For<IExperimentEventPublisher>();
    private readonly ITextEmotionAnalysisService _emotionAnalysis = Substitute.For<ITextEmotionAnalysisService>();
    private readonly IEmotionReadingRepository _emotionReadingRepository = Substitute.For<IEmotionReadingRepository>();

    private ConversationTurnService CreateService(SessionOptions? options = null) =>
        new(_conversationTurnRepository,
            _experimentSessionRepository,
            _orchestrator,
            _eventPublisher,
            _emotionAnalysis,
            _emotionReadingRepository,
            Options.Create(options ?? new SessionOptions()));

    public ConversationTurnServiceTests()
    {
        _conversationTurnRepository.TrySaveReplyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _emotionAnalysis.Analyze(Arg.Any<string>()).Returns(new TextEmotionDto(
            EmotionId: "neutral",
            Confidence: 0,
            Intensity: 0.3,
            Polarity: 0,
            Source: TextEmotionDto.SourceFallback,
            FallbackUsed: true,
            ModelVersion: "lexicon-test",
            LatencyMs: 1,
            Scores: new Dictionary<string, double>()));
    }

    private ExperimentSession ArrangeSession(ExperimentalCondition condition, Guid ownerUserId, int nextSequence = 1)
    {
        var session = ExperimentSession.Create(ownerUserId, condition);
        _experimentSessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _conversationTurnRepository.ListBySessionAsync(session.Id, Arg.Any<CancellationToken>()).Returns([]);
        _conversationTurnRepository.GetNextSequenceNumberAsync(session.Id, Arg.Any<CancellationToken>()).Returns(nextSequence, nextSequence + 1);
        return session;
    }

    private static InterlocutorReplyOutcome SuccessfulReply(string text = "Respuesta generada por IA.") =>
        new(text, "rule-based", "rule-based", 5, false, null, 1, ["rule-based"]);

    [Fact]
    public async Task Participant_ShouldReceiveAnAcknowledgmentAndHistoryWithoutAiIdentifiers()
    {
        var owner = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.AI, owner);
        var service = CreateService();
        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
            Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>()).Returns(SuccessfulReply());

        var acknowledgment = await service.AddAsync(owner, session.Id, new AddConversationTurnRequest { Message = "Hola" });
        acknowledgment.Source.Should().Be(ConversationTurnResult.SourcePending);
        acknowledgment.Ai.Should().BeNull();
        acknowledgment.InterlocutorTurn.Should().BeNull();

        var original = ConversationTurn.Create(session.Id, 1, ConversationActor.Participant, "Hola");
        var reply = ConversationTurn.Create(session.Id, 2, ConversationActor.Interlocutor, "Te escucho");
        reply.LinkOriginatingTurn(original.Id);
        _conversationTurnRepository.ListBySessionAsync(session.Id, Arg.Any<CancellationToken>()).Returns([original, reply]);
        var history = await service.ListAsync(owner, session.Id);
        history.Should().HaveCount(2);
        history.Should().OnlyContain(turn => turn.OriginatingTurnId == null);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistParticipantTurnAndAiInterlocutorReply_ForAiSessions()
    {
        // Arrange
        var service = CreateService();
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.AI, ownerUserId);

        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<Turning.Application.Interfaces.EmotionContext?>(), Arg.Any<CancellationToken>())
            .Returns(SuccessfulReply());

        // Act
        var result = await service.AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest
        {
            Sender = "Participant",
            Message = "Hola, inicio la conversación."
        });

        // Assert
        result.SequenceNumber.Should().Be(1);
        result.Sender.Should().Be("Participant");
        result.Message.Should().Be("Hola, inicio la conversación.");
        session.ConversationTurnCount.Should().Be(2);
        session.Status.Should().Be(ExperimentSessionStatus.Active);
        await _conversationTurnRepository.Received(1).AddAsync(Arg.Is<ConversationTurn>(turn =>
            turn.SequenceNumber == 1 && turn.Sender == ConversationActor.Participant), Arg.Any<CancellationToken>());
        await _conversationTurnRepository.Received(1).AddAsync(Arg.Is<ConversationTurn>(turn =>
            turn.SequenceNumber == 2 &&
            turn.Sender == ConversationActor.Interlocutor &&
            turn.Message == "Respuesta generada por IA."), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_ShouldReturnTheGeneratedReplyAndItsMetadata()
    {
        // Arrange
        var service = CreateService();
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.AI, ownerUserId);

        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<Turning.Application.Interfaces.EmotionContext?>(), Arg.Any<CancellationToken>())
            .Returns(new InterlocutorReplyOutcome("Hola, cuéntame más.", "openai", "gpt-x", 412, false, null, 1, ["openai"]));

        // Act
        var result = await service.AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest
        {
            Message = "Buenas."
        }, isPrivilegedRequester: true);

        // Assert: antes estos datos se descartaban y el cliente tenía que releer la lista.
        result.Source.Should().Be(ConversationTurnResult.SourceAi);
        result.InterlocutorTurn.Should().NotBeNull();
        result.InterlocutorTurn!.Message.Should().Be("Hola, cuéntame más.");
        result.InterlocutorTurn.OriginatingTurnId.Should().Be(result.Id);
        result.Ai.Should().NotBeNull();
        result.Ai!.Provider.Should().Be("openai");
        result.Ai.Model.Should().Be("gpt-x");
        result.Ai.LatencyMs.Should().Be(412);
        result.Degraded.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_ShouldPublishTheAiReplyEventWithTheKeysOfAHumanTurnEvent()
    {
        var published = new List<(string Type, string Json)>();
        _eventPublisher.When(publisher => publisher.Publish(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>()))
            .Do(call => published.Add((call.ArgAt<string>(1), JsonSerializer.Serialize(call.ArgAt<object>(2)))));
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.AI, ownerUserId);
        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(),
            Arg.Any<EmotionContext?>(), Arg.Any<CancellationToken>()).Returns(SuccessfulReply());

        await CreateService().AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest { Message = "Hola" });

        var turnEvent = JsonDocument.Parse(published.Single(e => e.Type == ExperimentEventTypes.ConversationTurnAdded).Json).RootElement;
        var replyEvent = JsonDocument.Parse(published.Single(e => e.Type == ExperimentEventTypes.AiReplyGenerated).Json).RootElement;
        // La proyeccion del participante renombra la respuesta de IA a ConversationTurnAdded y conserva
        // solo estas claves: si falta alguna, la respuesta de IA se distingue de un turno humano.
        foreach (var key in new[] { "turnId", "sequenceNumber", "sender" })
        {
            turnEvent.TryGetProperty(key, out _).Should().BeTrue(key);
            replyEvent.TryGetProperty(key, out _).Should().BeTrue(key);
        }
        replyEvent.GetProperty("sender").GetString().Should().Be("Interlocutor");
        replyEvent.GetProperty("sequenceNumber").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task AddAsync_ShouldReportDegradation_WhenGenerationProducesNoReply()
    {
        // Arrange
        var service = CreateService();
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.AI, ownerUserId);
        var eventId = Guid.NewGuid();

        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<Turning.Application.Interfaces.EmotionContext?>(), Arg.Any<CancellationToken>())
            .Returns(new InterlocutorReplyOutcome(null, "none", null, 10, true, "HttpRequestException", 3, ["openai"]));
        _eventPublisher.Publish(session.Id, ExperimentEventTypes.DegradedOperation, Arg.Any<object>()).Returns(eventId);

        // Act
        var result = await service.AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest
        {
            Message = "Mensaje que sobrevive."
        }, isPrivilegedRequester: true);

        // Assert: el turno del participante se conserva y la degradación no se oculta.
        result.Message.Should().Be("Mensaje que sobrevive.");
        result.InterlocutorTurn.Should().BeNull();
        result.Degraded.Should().BeTrue();
        result.DegradedReason.Should().Be("HttpRequestException");
        result.EventId.Should().Be(eventId);
        session.ConversationTurnCount.Should().Be(1);
        _eventPublisher.Received(1).Publish(session.Id, ExperimentEventTypes.DegradedOperation, Arg.Any<object>());
        var payload = _eventPublisher.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IExperimentEventPublisher.Publish)
            && Equals(call.GetArguments()[1], ExperimentEventTypes.DegradedOperation)).GetArguments()[2];
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        document.RootElement.GetProperty("code").GetString().Should().Be("TEXT_GENERATION_DEGRADED");
        document.RootElement.GetProperty("message").GetString().Should().Be("No se pudo completar la respuesta del interlocutor.");
        document.RootElement.GetProperty("retryable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistParticipantTurnBeforeAskingForTheReply()
    {
        // Arrange
        var service = CreateService();
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.AI, ownerUserId);
        var savedBeforeGenerating = 0;

        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<Turning.Application.Interfaces.EmotionContext?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                savedBeforeGenerating = _conversationTurnRepository.ReceivedCalls()
                    .Count(call => call.GetMethodInfo().Name == nameof(IConversationTurnRepository.SaveChangesAsync));
                return SuccessfulReply();
            });

        // Act
        await service.AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest { Message = "Hola." });

        // Assert: si el proveedor tumba el proceso, el mensaje del participante ya está guardado.
        savedBeforeGenerating.Should().Be(1);
        await _conversationTurnRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _conversationTurnRepository.Received(1).TrySaveReplyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_ShouldPublishTurnAddedEvent()
    {
        // Arrange
        var service = CreateService();
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.Human, ownerUserId);

        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<Turning.Application.Interfaces.EmotionContext?>(), Arg.Any<CancellationToken>())
            .Returns((InterlocutorReplyOutcome?)null);

        // Act
        await service.AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest { Message = "Hola." });

        // Assert
        _eventPublisher.Received(1).Publish(session.Id, ExperimentEventTypes.ConversationTurnAdded, Arg.Any<object>());
    }

    [Fact]
    public async Task AddAsync_ShouldUseConfiguredDuration_WhenAutoActivatingSession()
    {
        // Arrange
        var service = CreateService(new SessionOptions { DurationSeconds = 1800 });
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.Human, ownerUserId);

        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<Turning.Application.Interfaces.EmotionContext?>(), Arg.Any<CancellationToken>())
            .Returns((InterlocutorReplyOutcome?)null);

        // Act
        await service.AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest { Message = "Primer turno." });

        // Assert: la auto-activación respeta SessionOptions en vez de los 300s fijos.
        session.Status.Should().Be(ExperimentSessionStatus.Active);
        (session.ExpiresAtUtc!.Value - session.ActivatedAtUtc!.Value).TotalSeconds.Should().BeApproximately(1800, 1);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistOnlyOneTurn_ForHumanSessions()
    {
        // Arrange
        var service = CreateService();
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.Human, ownerUserId);

        _orchestrator.ResolveReplyAsync(session, Arg.Any<ConversationTurn>(), Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<Turning.Application.Interfaces.EmotionContext?>(), Arg.Any<CancellationToken>())
            .Returns((InterlocutorReplyOutcome?)null);

        // Act
        var result = await service.AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest
        {
            Sender = "Participant",
            Message = "Inicio una sesión humana."
        });

        // Assert
        result.SequenceNumber.Should().Be(1);
        result.Source.Should().Be(ConversationTurnResult.SourcePending);
        result.InterlocutorTurn.Should().BeNull();
        result.Ai.Should().BeNull();
        session.ConversationTurnCount.Should().Be(1);
        await _conversationTurnRepository.Received(1).AddAsync(Arg.Any<ConversationTurn>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_ShouldRejectTurnsOnTerminalSessions()
    {
        // Arrange
        var service = CreateService();
        var ownerUserId = Guid.NewGuid();
        var session = ArrangeSession(ExperimentalCondition.AI, ownerUserId);
        session.Activate(TimeSpan.FromSeconds(300));
        session.Complete();

        // Act
        var act = async () => await service.AddAsync(ownerUserId, session.Id, new AddConversationTurnRequest { Message = "Tarde." });

        // Assert
        await act.Should().ThrowAsync<Turning.Application.Exceptions.ApplicationException>()
            .Where(ex => ex.Code == "SESSION_TERMINAL");
    }
}
