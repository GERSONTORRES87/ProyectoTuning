using System.Text.Json;
using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Turning.Application.Features.Surveys;
using Turning.Application.Features.Emotions;
using Turning.Application.Interfaces;
using Turning.Domain.Common;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;
using Turning.Infrastructure.Repositories;
using Turning.Infrastructure.Services;
using Xunit;

namespace Turning.Infrastructure.Tests;

[Trait("Category", "SqlServer")]
public class ExperimentalDataServicesTests : IAsyncLifetime
{
    private TurningDbContext _db = null!;
    private DbContextOptions<TurningDbContext> _options = null!;
    private Guid _ownerId;

    public async Task InitializeAsync()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TURNING_TEST_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True")
        {
            InitialCatalog = $"TurningDataTests_{Guid.NewGuid():N}"
        };
        _options = new DbContextOptionsBuilder<TurningDbContext>()
            .UseSqlServer(connection.ConnectionString, sql => sql.EnableRetryOnFailure()).Options;
        _db = new TurningDbContext(_options);
        await _db.Database.MigrateAsync();
        var owner = UserAccount.Create("data-owner@example.com", "Data Owner", "hash-value", UserRoles.Participant);
        _db.UserAccounts.Add(owner);
        await _db.SaveChangesAsync();
        _ownerId = owner.Id;
    }

    public async Task DisposeAsync()
    {
        if (_db is null) return;
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task DefaultSurvey_IsSeededWithQuestionsAndIsIdempotent()
    {
        var legacyEmptyDefinition = SurveyDefinition.Create("default-v1", "Encuesta anterior vacía");
        _db.SurveyDefinitions.Add(legacyEmptyDefinition);
        await _db.SaveChangesAsync();
        var first = await SurveyDefinitionSeeder.EnsureDefaultAsync(_db);
        var second = await SurveyDefinitionSeeder.EnsureDefaultAsync(_db);

        second.Id.Should().Be(first.Id);
        (await _db.SurveyDefinitions.CountAsync()).Should().Be(1);
        first.Questions.Should().HaveCount(4);
        first.Questions.Should().Contain(q => q.Code == "perceived_interlocutor" && q.Required && q.Type == "choice");
    }

    [Fact]
    public async Task SubmitSurvey_PersistsAnswersInOnePostAndProjectsBlindResults()
    {
        var session = await CreateSessionAsync(terminal: true);
        var definition = await SurveyDefinitionSeeder.EnsureDefaultAsync(_db);
        var service = new SurveyAppService(_db);
        var id = await service.SubmitAsync(session.Id, Request(definition.Id), default);
        _db.ChangeTracker.Clear();

        var response = await _db.SurveyResponses.Include(r => r.Answers).SingleAsync(r => r.Id == id);
        response.OwnerUserId.Should().Be(_ownerId);
        response.SubmittedAtUtc.Should().NotBeNull();
        response.Answers.Should().HaveCount(3);
        var results = new ResultsService(_db);
        var participant = await results.GetResultAsync(session.Id, default, _ownerId, false);
        participant.Session.Condition.Should().Be("Hidden");
        participant.Survey.Single().Answers.Should().HaveCount(3);
        var serialized = JsonSerializer.Serialize(participant);
        serialized.Should().NotContain("OwnerUserId").And.NotContain("RowVersion").And.NotContain("IsDeleted");
        var investigator = await results.GetResultAsync(session.Id, default, _ownerId, true);
        investigator.Session.Condition.Should().Be("AI");

        Func<Task> duplicate = () => service.SubmitAsync(session.Id, Request(definition.Id), default);
        await duplicate.Should().ThrowAsync<InvalidOperationException>().WithMessage("ALREADY_SUBMITTED");
    }

    [Fact]
    public async Task SubmitSurvey_RejectsActiveSessionAndUnknownQuestionsWithoutPersisting()
    {
        var session = await CreateSessionAsync();
        var definition = await SurveyDefinitionSeeder.EnsureDefaultAsync(_db);
        var service = new SurveyAppService(_db);
        Func<Task> early = () => service.SubmitAsync(session.Id, Request(definition.Id), default);
        await early.Should().ThrowAsync<InvalidOperationException>().WithMessage("SURVEY_NOT_AVAILABLE");
        (await _db.SurveyResponses.CountAsync()).Should().Be(0);

        session.Complete();
        await _db.SaveChangesAsync();
        Func<Task> unknown = () => service.SubmitAsync(session.Id,
            new SurveySubmitRequest(definition.Id, new Dictionary<string, string> { ["not_a_question"] = "text" }), default);
        await unknown.Should().ThrowAsync<ArgumentException>().WithMessage("SURVEY_UNKNOWN_QUESTION");
        _db.ChangeTracker.Clear();
        (await _db.SurveyResponses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SubmitSurvey_ConcurrentRequestsProduceOneResponse()
    {
        var session = await CreateSessionAsync(terminal: true);
        var definition = await SurveyDefinitionSeeder.EnsureDefaultAsync(_db);
        async Task<string> SubmitAsync()
        {
            await using var context = new TurningDbContext(_options);
            try { await new SurveyAppService(context).SubmitAsync(session.Id, Request(definition.Id), default); return "success"; }
            catch (InvalidOperationException ex) when (ex.Message == "ALREADY_SUBMITTED") { return ex.Message; }
        }
        var outcomes = await Task.WhenAll(SubmitAsync(), SubmitAsync());
        outcomes.Should().BeEquivalentTo(["success", "ALREADY_SUBMITTED"]);
        (await _db.SurveyResponses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SubmitSurvey_CompletesExistingLegacyDraft()
    {
        var session = await CreateSessionAsync(terminal: true);
        var definition = await SurveyDefinitionSeeder.EnsureDefaultAsync(_db);
        var draft = SurveyResponse.Create(session.Id, _ownerId, definition.Id);
        _db.SurveyResponses.Add(draft);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var id = await new SurveyAppService(_db).SubmitAsync(session.Id, Request(definition.Id), default);
        id.Should().Be(draft.Id);
        _db.ChangeTracker.Clear();
        var saved = await _db.SurveyResponses.Include(r => r.Answers).SingleAsync();
        saved.Answers.Should().HaveCount(3);
        saved.SubmittedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Results_ListUsesStablePaginationAndFilters()
    {
        var firstSession = await CreateSessionAsync();
        var secondSession = await CreateSessionAsync();
        var human = ExperimentSession.Create(_ownerId, ExperimentalCondition.Human);
        _db.ExperimentSessions.Add(human);
        await _db.SaveChangesAsync();
        var fixedTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await _db.ExperimentSessions.ExecuteUpdateAsync(setters => setters.SetProperty(s => s.CreatedAt, fixedTime));
        var service = new ResultsService(_db);

        var firstPage = await service.ListAsync(fixedTime.AddMinutes(-1), fixedTime.AddMinutes(1), "AI", 1, 1, default);
        var secondPage = await service.ListAsync(fixedTime.AddMinutes(-1), fixedTime.AddMinutes(1), "AI", 2, 1, default);
        var repeated = await service.ListAsync(null, null, "AI", 1, 1, default);

        firstPage.Total.Should().Be(2);
        firstPage.Items.Concat(secondPage.Items).Select(s => s.Id).Should().BeEquivalentTo([firstSession.Id, secondSession.Id]);
        repeated.Items.Single().Id.Should().Be(firstPage.Items.Single().Id);
        firstPage.Items.Should().OnlyContain(s => s.Condition == "AI");
        (await service.ListAsync(fixedTime.AddDays(1), null, null, 1, 10, default)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Events_PaginateTiesWithoutLossAndRejectExpiredOrForeignCursors()
    {
        var session = await CreateSessionAsync();
        var another = await CreateSessionAsync();
        var now = DateTime.UtcNow;
        var entries = Enumerable.Range(0, 105).Select(_ => ExperimentEvent.Create(session.Id, "ConversationTurnAdded", "{}", now)).ToArray();
        var expired = ExperimentEvent.Create(session.Id, "ConversationTurnAdded", "{}", now.AddDays(-31));
        var foreign = ExperimentEvent.Create(another.Id, "ConversationTurnAdded", "{}", now);
        _db.ExperimentEvents.AddRange(entries.Append(expired).Append(foreign));
        await _db.SaveChangesAsync();
        var service = new EventService(_db);
        var first = await service.ListAsync(session.Id, null, default);
        var next = await service.ListAsync(session.Id, first[^1].Id, default);
        first.Should().HaveCount(100);
        next.Should().HaveCount(5);
        first.Concat(next).Select(e => e.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(entries.Select(e => e.Id));
        (await service.ListAsync(session.Id, next[^1].Id, default)).Should().BeEmpty();
        Func<Task> old = () => service.ListAsync(session.Id, expired.Id, default);
        await old.Should().ThrowAsync<InvalidOperationException>().WithMessage("EVENT_EXPIRED");
        Func<Task> other = () => service.ListAsync(session.Id, foreign.Id, default);
        await other.Should().ThrowAsync<InvalidOperationException>().WithMessage("EVENT_EXPIRED");
    }

    [Fact]
    public async Task Events_HideProviderAndAiConditionFromParticipant()
    {
        var session = await CreateSessionAsync();
        await AddConversationEventsAsync(session.Id);
        var service = new EventService(_db);
        var participant = await service.ListAsync(session.Id, null, default, false);
        var serialized = JsonSerializer.Serialize(participant);
        serialized.Should().NotContain("AiReplyGenerated").And.NotContain("TextGeneration")
            .And.NotContain("AiProviderFailover").And.NotContain("private-");
        participant.Should().OnlyContain(e => e.SessionId == session.Id);
        var investigator = await service.ListAsync(session.Id, null, default, true);
        investigator.Should().Contain(e => e.Type == ExperimentEventTypes.AiReplyGenerated);
        investigator.Should().Contain(e => e.Type == ExperimentEventTypes.AiProviderFailover);
        investigator.Count(e => e.Type == ExperimentEventTypes.DegradedOperation).Should().Be(2);
    }

    [Fact]
    public async Task Events_GiveParticipantTheSameSchemaForAiReplyAndHumanTurn()
    {
        var session = await CreateSessionAsync();
        await AddConversationEventsAsync(session.Id);
        var participant = await new EventService(_db).ListAsync(session.Id, null, default, false);

        var turnKeys = participant.Where(e => e.Type == ExperimentEventTypes.ConversationTurnAdded)
            .Select(e => JsonDocument.Parse(e.PayloadJson).RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray())
            .ToArray();
        // Un turno humano y una respuesta de IA se proyectan con exactamente las mismas claves.
        turnKeys.Should().HaveCount(2);
        turnKeys[0].Should().Equal("sender", "sequenceNumber", "turnId");
        turnKeys[1].Should().Equal(turnKeys[0]);
        // La degradacion del analisis emocional existe en ambas condiciones y se conserva;
        // la de generacion de texto solo existe con IA y se oculta.
        var degraded = participant.Where(e => e.Type == ExperimentEventTypes.DegradedOperation).ToArray();
        degraded.Should().ContainSingle();
        degraded[0].PayloadJson.Should().Contain("EmotionAnalysis");
    }

    [Fact]
    public async Task Results_HideConversationDegradationFromParticipantButKeepItForInvestigators()
    {
        var session = await CreateSessionAsync(terminal: true);
        await AddConversationEventsAsync(session.Id);
        var results = new ResultsService(_db);

        var participant = await results.GetResultAsync(session.Id, default, _ownerId, false);
        var investigator = await results.GetResultAsync(session.Id, default, _ownerId, true);

        participant.DegradedEvents.Should().ContainSingle();
        participant.DegradedEvents[0].PayloadJson.Should().Contain("EmotionAnalysis").And.NotContain("TextGeneration");
        investigator.DegradedEvents.Should().HaveCount(2);
    }

    private async Task AddConversationEventsAsync(Guid sessionId)
    {
        _db.ExperimentEvents.Add(ExperimentEvent.Create(sessionId, ExperimentEventTypes.ConversationTurnAdded,
            JsonSerializer.Serialize(new { turnId = Guid.NewGuid(), sequenceNumber = 1, sender = "Interlocutor" })));
        _db.ExperimentEvents.Add(ExperimentEvent.Create(sessionId, ExperimentEventTypes.AiReplyGenerated,
            JsonSerializer.Serialize(new
            {
                turnId = Guid.NewGuid(), sequenceNumber = 2, sender = "Interlocutor", originatingTurnId = Guid.NewGuid(),
                provider = "private-provider", model = "private-model", latencyMs = 12, degraded = true
            })));
        _db.ExperimentEvents.Add(ExperimentEvent.Create(sessionId, ExperimentEventTypes.DegradedOperation,
            "{\"operation\":\"TextGeneration\",\"provider\":\"private-provider\",\"failureReason\":\"private-error\"}"));
        _db.ExperimentEvents.Add(ExperimentEvent.Create(sessionId, ExperimentEventTypes.AiProviderFailover,
            "{\"from\":\"private-provider\",\"to\":\"rule-based\"}"));
        _db.ExperimentEvents.Add(ExperimentEvent.Create(sessionId, ExperimentEventTypes.DegradedOperation,
            "{\"code\":\"EMOTION_PROVIDER_UNAVAILABLE\",\"operation\":\"EmotionAnalysis\",\"provider\":\"private-emotion-provider\"}"));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task EmotionFallback_PersistsReadingAvatarSnapshotAndSafeEventsTogether()
    {
        var session = await CreateSessionAsync();
        var port = Substitute.For<IEmotionAnalysisPort>();
        port.AnalyzeAsync(Arg.Any<EmotionAnalysisRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EmotionAnalysisResult>(new HttpRequestException("private-provider-response")));
        var service = new EmotionService(_db, port, new ExperimentEventPublisher(_db));
        await service.AddAsync(session.Id, new EmotionCreateRequest(null, 0.5, "video"), default);
        _db.ChangeTracker.Clear();

        var saved = await _db.ExperimentSessions.SingleAsync(s => s.Id == session.Id);
        saved.Status.Should().Be(ExperimentSessionStatus.Active);
        saved.EmotionSampleCount.Should().Be(1);
        saved.LastDetectedEmotion.Should().Be("neutral");
        saved.AvatarState.Should().Be("Neutral");
        var reading = await _db.EmotionReadings.SingleAsync();
        reading.IsDegraded.Should().BeTrue();
        var avatar = await _db.AvatarExpressions.SingleAsync();
        avatar.EmotionReadingId.Should().Be(reading.Id);
        // Una lectura degradada no es una medicion: el avatar de respaldo no puede mostrarse como un 30 %.
        avatar.IsFallback.Should().BeTrue();
        var events = await _db.ExperimentEvents.ToArrayAsync();
        events.Select(e => e.Type).Should().BeEquivalentTo(["DegradedOperation", "EmotionReadingAdded", "AvatarExpressionChanged"]);
        events.Should().OnlyContain(e => !e.PayloadJson.Contains("private-provider-response"));
    }

    [Theory]
    [InlineData(true, "RecoveredTimedOut")]
    [InlineData(false, "DurationExpired")]
    public async Task Scheduler_ExpirationAndRecoveryPersistCloseAndSurveyEvents(bool expiredAtStartup, string expectedReason)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new TurningDbContext(_options));
        services.AddScoped<IExperimentSessionRepository, ExperimentSessionRepository>();
        services.AddScoped<IExperimentEventPublisher, ExperimentEventPublisher>();
        await using var provider = services.BuildServiceProvider();
        using var scheduler = new SessionSchedulerService(provider, NullLogger<SessionSchedulerService>.Instance,
            Options.Create(new SessionOptions()));
        var session = ExperimentSession.Create(_ownerId, ExperimentalCondition.AI);
        session.Activate(TimeSpan.FromMinutes(5), DateTime.UtcNow.AddMinutes(-10));
        if (expiredAtStartup)
        {
            _db.ExperimentSessions.Add(session);
            await _db.SaveChangesAsync();
        }

        await scheduler.StartAsync(default);
        try
        {
            if (!expiredAtStartup)
            {
                await Task.Delay(1500);
                _db.ExperimentSessions.Add(session);
                await _db.SaveChangesAsync();
            }
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (await _db.ExperimentSessions.AsNoTracking().AnyAsync(s => s.Id == session.Id && s.Status == ExperimentSessionStatus.TimedOut))
                    break;
                await Task.Delay(100);
            }
            var saved = await _db.ExperimentSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
            saved.Status.Should().Be(ExperimentSessionStatus.TimedOut);
            var audit = await _db.SessionAuditEntries.SingleAsync(a => a.SessionId == session.Id);
            audit.ActorType.Should().Be("Scheduler");
            audit.ActorId.Should().BeNull();
            audit.Reason.Should().Be(expectedReason);
            var events = await _db.ExperimentEvents.Where(e => e.SessionId == session.Id).Select(e => e.Type).ToArrayAsync();
            events.Should().BeEquivalentTo(["SessionStateChanged", "SessionEnded", "SurveyAvailable"]);
        }
        finally { await scheduler.StopAsync(default); }
    }

    [Fact]
    public async Task Emotion_WaitsForSessionLockAndReloadsAfterConcurrentClosure()
    {
        var session = await CreateSessionAsync();
        var barrier = new CommandBarrier();
        var options = new DbContextOptionsBuilder<TurningDbContext>(_options).AddInterceptors(barrier).Options;
        await using var context = new TurningDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.ExperimentSessions.SingleAsync(s => s.Id == session.Id);
        var service = new EmotionService(context, Substitute.For<IEmotionAnalysisPort>(), new ExperimentEventPublisher(context));
        using var held = await Turning.Application.Features.ExperimentSessions.SessionOperationLock.AcquireAsync(session.Id);
        var commandsBefore = barrier.CommandCount;
        barrier.RejectCommands = true;

        var pending = service.AddAsync(session.Id, new EmotionCreateRequest("joy", 0.8, "direct"), default);
        pending.IsCompleted.Should().BeFalse();
        barrier.CommandCount.Should().Be(commandsBefore);
        session.Complete();
        await _db.SaveChangesAsync();
        barrier.RejectCommands = false;
        held.Dispose();

        Func<Task> result = () => pending;
        await result.Should().ThrowAsync<InvalidOperationException>().WithMessage("SESSION_TERMINAL");
        (await _db.EmotionReadings.CountAsync()).Should().Be(0);
        (await _db.ExperimentEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Scheduler_WaitsForSessionLockAndReloadsCandidateBeforeExpiring()
    {
        var session = ExperimentSession.Create(_ownerId, ExperimentalCondition.AI);
        session.Activate(TimeSpan.FromMinutes(5), DateTime.UtcNow.AddMinutes(-10));
        _db.ExperimentSessions.Add(session);
        await _db.SaveChangesAsync();
        var barrier = new CommandBarrier();
        var options = new DbContextOptionsBuilder<TurningDbContext>(_options).AddInterceptors(barrier).Options;
        var services = new ServiceCollection();
        services.AddScoped(_ => new TurningDbContext(options));
        services.AddScoped<IExperimentSessionRepository, ExperimentSessionRepository>();
        services.AddScoped<IExperimentEventPublisher, ExperimentEventPublisher>();
        await using var provider = services.BuildServiceProvider();
        var logger = new RecoveryCompletedLogger();
        using var scheduler = new SessionSchedulerService(provider, logger, Options.Create(new SessionOptions()));
        using var held = await Turning.Application.Features.ExperimentSessions.SessionOperationLock.AcquireAsync(session.Id);
        await scheduler.StartAsync(default);
        try
        {
            await barrier.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
            barrier.RejectCommands = true;
            session.Cancel("Cierre concurrente mientras esperaba el scheduler");
            await _db.SaveChangesAsync();
            barrier.CommandCount.Should().Be(1);
            (await _db.ExperimentEvents.CountAsync()).Should().Be(0);
            barrier.RejectCommands = false;
            held.Dispose();

            await logger.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var saved = await _db.ExperimentSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
            saved.Status.Should().Be(ExperimentSessionStatus.Cancelled);
            (await _db.SessionAuditEntries.CountAsync()).Should().Be(0);
            (await _db.ExperimentEvents.CountAsync()).Should().Be(0);
            barrier.CommandCount.Should().BeGreaterThan(1);
        }
        finally
        {
            barrier.RejectCommands = false;
            held.Dispose();
            await scheduler.StopAsync(default);
        }
    }

    private sealed class CommandBarrier : DbCommandInterceptor
    {
        private int _commandCount;
        public int CommandCount => Volatile.Read(ref _commandCount);
        public volatile bool RejectCommands;
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commandCount);
            if (RejectCommands) throw new InvalidOperationException("El servicio accedió a SQL mientras la sesión seguía bloqueada.");
            return ValueTask.FromResult(result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            FirstRead.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecoveryCompletedLogger : ILogger<SessionSchedulerService>
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).StartsWith("Session recovery:", StringComparison.Ordinal)) Completed.TrySetResult();
        }
    }

    private async Task<ExperimentSession> CreateSessionAsync(bool terminal = false)
    {
        var session = ExperimentSession.Create(_ownerId, ExperimentalCondition.AI);
        session.Activate(TimeSpan.FromMinutes(5));
        if (terminal) session.Complete();
        _db.ExperimentSessions.Add(session);
        await _db.SaveChangesAsync();
        return session;
    }

    private static SurveySubmitRequest Request(Guid definitionId) => new(definitionId, new Dictionary<string, string>
    {
        ["perceived_interlocutor"] = "Unsure", ["empathy"] = "4", ["understanding"] = "5"
    });
}
