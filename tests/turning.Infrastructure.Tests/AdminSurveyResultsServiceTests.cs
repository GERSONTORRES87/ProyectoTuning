using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Turning.Domain.Common;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;
using Turning.Infrastructure.Services;
using Xunit;

namespace Turning.Infrastructure.Tests;

public sealed class AdminSurveyResultsServiceTests : IDisposable
{
    private readonly TurningDbContext _db;
    private readonly AdminSurveyResultsService _service;

    public AdminSurveyResultsServiceTests()
    {
        var database = $"TurningAdminSurveyTests_{Guid.NewGuid():N}";
        _db = new TurningDbContext(new DbContextOptionsBuilder<TurningDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True").Options);
        _db.Database.EnsureCreated();
        _service = new AdminSurveyResultsService(_db);
    }

    [Fact]
    public async Task SubmittedResponsesKeepTheirActualParticipantsAndOriginalDefinitions()
    {
        var fixture = await SeedAsync();

        var result = await _service.GetResultsAsync();

        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().NotContain(item => item.ResponseId == fixture.Draft.Id);
        var alice = result.Items.Single(item => item.ResponseId == fixture.AliceResponse.Id);
        alice.Participant.Id.Should().Be(fixture.Alice.Id);
        alice.Participant.FullName.Should().Be("Agente Uno");
        alice.Participant.Email.Should().Be("agent-one@test.local");
        alice.SessionCode.Should().Be(fixture.AliceSession.SessionCode);
        alice.Condition.Should().Be("AI");
        alice.CompletionStatus.Should().Be("Completed");
        alice.SubmittedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        alice.Definition.Id.Should().Be(fixture.OriginalDefinition.Id);
        alice.Definition.Code.Should().Be("default-v1");
        alice.Definition.Version.Should().Be("1.0");
        alice.Definition.Questions.Select(question => question.Code).Should().Equal("perceived_interlocutor", "empathy", "understanding", "comments");
        alice.Definition.Questions.Single(question => question.Code == "empathy").Text.Should().Be("Pregunta original de empatía");
        var empathyId = alice.Definition.Questions.Single(question => question.Code == "empathy").Id;
        alice.Answers.Single(answer => answer.QuestionId == empathyId).Value.Should().Be("4");
        var bob = result.Items.Single(item => item.ResponseId == fixture.BobResponse.Id);
        bob.Participant.Id.Should().Be(fixture.Bob.Id);
        bob.Participant.FullName.Should().Be("Agente Dos");
        bob.Condition.Should().Be("Human");
        bob.CompletionStatus.Should().Be("TimedOut");
    }

    [Fact]
    public async Task PaginationOrdersBySubmissionDateAndKeepsTheFullTotal()
    {
        var fixture = await SeedAsync();

        var first = await _service.GetResultsAsync(page: 1, pageSize: 1);
        var second = await _service.GetResultsAsync(page: 2, pageSize: 1);

        first.Total.Should().Be(2);
        first.Page.Should().Be(1);
        first.PageSize.Should().Be(1);
        first.Items.Should().ContainSingle().Which.ResponseId.Should().Be(fixture.BobResponse.Id);
        second.Total.Should().Be(2);
        second.Items.Should().ContainSingle().Which.ResponseId.Should().Be(fixture.AliceResponse.Id);
    }

    [Fact]
    public async Task ParticipantFilterIsAppliedBeforeCountingAndPaging()
    {
        var fixture = await SeedAsync();

        var alice = await _service.GetResultsAsync(page: 1, pageSize: 1, participantId: fixture.Alice.Id);
        var missing = await _service.GetResultsAsync(participantId: Guid.NewGuid());

        alice.Total.Should().Be(1);
        alice.Items.Should().ContainSingle().Which.ResponseId.Should().Be(fixture.AliceResponse.Id);
        missing.Total.Should().Be(0);
        missing.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedResponsesOrSessionsAreExcludedBeforeCountingAndPaging(bool deleteSession)
    {
        var fixture = await SeedAsync();
        var deleted = deleteSession
            ? (BaseEntity)await _db.ExperimentSessions.SingleAsync(session => session.Id == fixture.BobResponse.SessionId)
            : fixture.BobResponse;
        Set(deleted, "IsDeleted", true);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var first = await _service.GetResultsAsync(page: 1, pageSize: 1);
        var beyondLast = await _service.GetResultsAsync(page: 2, pageSize: 1);
        var deletedParticipant = await _service.GetResultsAsync(participantId: fixture.Bob.Id);

        first.Total.Should().Be(1);
        first.Items.Should().ContainSingle().Which.ResponseId.Should().Be(fixture.AliceResponse.Id);
        beyondLast.Total.Should().Be(1);
        beyondLast.Items.Should().BeEmpty();
        deletedParticipant.Total.Should().Be(0);
        deletedParticipant.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task DeletedAnswersAreExcludedWithoutRemovingTheSubmittedResponse()
    {
        var fixture = await SeedAsync();
        var comments = fixture.OriginalDefinition.Questions.Single(question => question.Code == "comments");
        var deletedAnswer = fixture.AliceResponse.Answers.Single(answer => answer.SurveyQuestionId == comments.Id);
        Set(deletedAnswer, "IsDeleted", true);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await _service.GetResultsAsync(participantId: fixture.Alice.Id);

        result.Total.Should().Be(1);
        var response = result.Items.Should().ContainSingle().Which;
        response.ResponseId.Should().Be(fixture.AliceResponse.Id);
        response.Answers.Should().HaveCount(3);
        response.Answers.Should().NotContain(answer => answer.QuestionId == comments.Id);
        var empathy = fixture.OriginalDefinition.Questions.Single(question => question.Code == "empathy");
        response.Answers.Single(answer => answer.QuestionId == empathy.Id).Value.Should().Be("4");
    }

    [Fact]
    public async Task QueryingResultsDoesNotCreateModifyOrTrackSurveyEntities()
    {
        await SeedAsync();
        _db.ChangeTracker.Clear();

        await _service.GetResultsAsync();

        _db.ChangeTracker.Entries().Should().BeEmpty();
        (await _db.SurveyDefinitions.CountAsync()).Should().Be(2);
        (await _db.SurveyResponses.CountAsync()).Should().Be(3);
        (await _db.Set<SurveyQuestion>().CountAsync()).Should().Be(5);
        (await _db.Set<SurveyAnswer>().CountAsync()).Should().Be(8);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPaginationIsRejectedBeforeQuerying(int page, int size)
    {
        Func<Task> query = () => _service.GetResultsAsync(page, size);

        await query.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task PageBeyondTheLastKeepsTheFilteredTotal()
    {
        await SeedAsync();

        var result = await _service.GetResultsAsync(page: 3, pageSize: 1);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(2);
        result.Page.Should().Be(3);
    }

    [Fact]
    public async Task EmptyDatabaseReturnsNoResultsWithoutCreatingADefinition()
    {
        var result = await _service.GetResultsAsync();

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
        (await _db.SurveyDefinitions.CountAsync()).Should().Be(0);
        (await _db.SurveyResponses.CountAsync()).Should().Be(0);
    }

    private async Task<Fixture> SeedAsync()
    {
        var alice = UserAccount.Create("agent-one@test.local", "Agente Uno", "test-hash", UserRoles.Participant);
        var bob = UserAccount.Create("agent-two@test.local", "Agente Dos", "test-hash", UserRoles.Participant);
        var aliceSession = ExperimentSession.Create(alice.Id, ExperimentalCondition.AI);
        aliceSession.Activate(TimeSpan.FromMinutes(5));
        aliceSession.Complete();
        var bobSession = ExperimentSession.Create(bob.Id, ExperimentalCondition.Human);
        bobSession.Activate(TimeSpan.FromMinutes(5));
        bobSession.Expire();
        var draftSession = ExperimentSession.Create(alice.Id, ExperimentalCondition.Human);
        draftSession.Cancel("Prueba");
        var original = SurveyDefinition.Create("default-v1", "Encuesta original");
        Set(original, "IsActive", false);
        var understanding = Question(original, "understanding", "Pregunta original de comprensión", "scale", true, 3);
        var perceived = Question(original, "perceived_interlocutor", "Interlocutor percibido", "choice", true, 1);
        var comments = Question(original, "comments", "Comentarios", "text", false, 4);
        var empathy = Question(original, "empathy", "Pregunta original de empatía", "scale", true, 2);
        var current = SurveyDefinition.Create("default-v2", "Definición vigente diferente");
        Set(current, "Version", "2.0");
        var currentQuestion = Question(current, "empathy", "Texto nuevo que no debe reemplazar el original", "scale", true, 1);
        var aliceResponse = Response(aliceSession, original.Id, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));
        Answer(aliceResponse, empathy, "4");
        Answer(aliceResponse, understanding, "3");
        Answer(aliceResponse, perceived, "AI");
        Answer(aliceResponse, comments, "Comentario sintético de prueba");
        var bobResponse = Response(bobSession, original.Id, new DateTime(2026, 10, 2, 11, 0, 0, DateTimeKind.Utc));
        Answer(bobResponse, empathy, "2");
        Answer(bobResponse, understanding, "5");
        Answer(bobResponse, perceived, "Human");
        var draft = Response(draftSession, current.Id, null);
        Answer(draft, currentQuestion, "2");
        _db.UserAccounts.AddRange(alice, bob);
        _db.ExperimentSessions.AddRange(aliceSession, bobSession, draftSession);
        _db.SurveyDefinitions.AddRange(original, current);
        _db.SurveyResponses.AddRange(aliceResponse, bobResponse, draft);
        await _db.SaveChangesAsync();
        return new Fixture(alice, bob, aliceSession, original, aliceResponse, bobResponse, draft);
    }

    private static SurveyQuestion Question(SurveyDefinition definition, string code, string text, string type, bool required, int order)
    {
        var question = (SurveyQuestion)Activator.CreateInstance(typeof(SurveyQuestion), true)!;
        Set(question, "SurveyDefinitionId", definition.Id);
        Set(question, "Code", code); Set(question, "Text", text); Set(question, "Type", type);
        Set(question, "Required", required); Set(question, "Order", order);
        definition.Questions.Add(question);
        return question;
    }

    private static SurveyResponse Response(ExperimentSession session, Guid definitionId, DateTime? submitted)
    {
        var response = (SurveyResponse)Activator.CreateInstance(typeof(SurveyResponse), true)!;
        Set(response, "SessionId", session.Id); Set(response, "SurveyDefinitionId", definitionId);
        Set(response, "OwnerUserId", session.OwnerUserId);
        Set(response, "StartedAtUtc", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        Set(response, "SubmittedAtUtc", submitted);
        return response;
    }

    private static void Answer(SurveyResponse response, SurveyQuestion question, string value)
    {
        var answer = (SurveyAnswer)Activator.CreateInstance(typeof(SurveyAnswer), true)!;
        Set(answer, "SurveyResponseId", response.Id); Set(answer, "SurveyQuestionId", question.Id); Set(answer, "Value", value);
        response.Answers.Add(answer);
    }

    private static void Set(object entity, string property, object? value) => entity.GetType().GetProperty(property)!.SetValue(entity, value);
    private sealed record Fixture(UserAccount Alice, UserAccount Bob, ExperimentSession AliceSession,
        SurveyDefinition OriginalDefinition, SurveyResponse AliceResponse, SurveyResponse BobResponse, SurveyResponse Draft);

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }
}
