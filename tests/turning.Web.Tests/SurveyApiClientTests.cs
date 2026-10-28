using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using turning.Web.Auth;
using turning.Web.Surveys;
using Xunit;

namespace turning.Web.Tests;

public sealed class SurveyApiClientTests
{
    [Fact]
    public async Task AdminResultsFollowEveryPageAndAuthenticateEveryReadOnlyRequest()
    {
        var firstPage = Enumerable.Range(1, 50).Select(Result).ToArray();
        var lastResult = Result(51);
        using var handler = new Handler(
            Json(new { items = firstPage, total = 51, page = 1, pageSize = 50 }),
            Json(new { items = new[] { lastResult }, total = 51, page = 2, pageSize = 50 }));
        using var http = Http(handler);

        var results = await Client(http).GetAdminResultsAsync();

        results.Should().HaveCount(51);
        results.Last().ResponseId.Should().Be(lastResult.ResponseId);
        handler.Requests.Select(request => request.Path).Should().Equal(
            "/api/surveys/results?page=1&pageSize=50",
            "/api/surveys/results?page=2&pageSize=50");
        handler.Requests.Should().OnlyContain(request =>
            request.Method == "GET" && request.Authorization == "Bearer survey-test-token");
    }

    [Fact]
    public async Task ResultsPreserveTheAgentIdentityQuestionnaireVersionAndActualSavedAnswers()
    {
        var responseId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        using var handler = new Handler(Json(new
        {
            total = 1, page = 1, pageSize = 50,
            items = new[]
            {
                new
                {
                    responseId, sessionId, sessionCode = "EXP-SAVED", condition = "Human", completionStatus = "TimedOut",
                    submittedAtUtc = "2026-10-28T18:03:00Z",
                    participant = new { id = participantId, fullName = "Agente de prueba", email = "agente@example.test" },
                    definition = new
                    {
                        id = definitionId, code = "post-session", version = "2.0",
                        questions = new[] { new { id = questionId, code = "comfort", text = "Experiencia registrada", type = "text", required = true, order = 2 } }
                    },
                    answers = new[] { new { questionId, value = "Me sentí escuchado." } }
                }
            }
        }));
        using var http = Http(handler);

        var results = await Client(http).GetAdminResultsAsync();

        var result = results.Should().ContainSingle().Which;
        result.ResponseId.Should().Be(responseId);
        result.SessionId.Should().Be(sessionId);
        result.SessionCode.Should().Be("EXP-SAVED");
        result.Condition.Should().Be("Human");
        result.CompletionStatus.Should().Be("TimedOut");
        result.SubmittedAtUtc.Should().Be(new DateTime(2026, 10, 28, 18, 3, 0, DateTimeKind.Utc));
        result.Participant.Should().BeEquivalentTo(new { Id = participantId, FullName = "Agente de prueba", Email = "agente@example.test" });
        result.Definition.Id.Should().Be(definitionId);
        result.Definition.Version.Should().Be("2.0");
        result.Definition.Questions.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = questionId, Code = "comfort", Text = "Experiencia registrada", Type = "text", Required = true, Order = 2
        });
        result.Answers.Should().ContainSingle().Which.Should().BeEquivalentTo(new { QuestionId = questionId, Value = "Me sentí escuchado." });
    }

    [Fact]
    public async Task AnEmptyResultSetStopsWithoutAnExtraPageOrWrite()
    {
        using var handler = new Handler(Json(new { items = Array.Empty<object>(), total = 0, page = 1, pageSize = 50 }));
        using var http = Http(handler);

        var results = await Client(http).GetAdminResultsAsync();

        results.Should().BeEmpty();
        handler.Requests.Should().ContainSingle().Which.Method.Should().Be("GET");
    }

    [Fact]
    public async Task AFullSinglePageDoesNotRequestAnUnnecessarySecondPage()
    {
        using var handler = new Handler(Json(new { items = Enumerable.Range(1, 50).Select(Result).ToArray(), total = 50, page = 1, pageSize = 50 }));
        using var http = Http(handler);

        var results = await Client(http).GetAdminResultsAsync();

        results.Should().HaveCount(50);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnEmptyLaterPageStopsWhenTheResultSetChanges()
    {
        using var handler = new Handler(
            Json(new { items = Enumerable.Range(1, 50).Select(Result).ToArray(), total = 100, page = 1, pageSize = 50 }),
            Json(new { items = Array.Empty<object>(), total = 100, page = 2, pageSize = 50 }));
        using var http = Http(handler);

        var results = await Client(http).GetAdminResultsAsync();

        results.Should().HaveCount(50);
        handler.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "La consulta no es válida.")]
    [InlineData(HttpStatusCode.Forbidden, "No tienes permiso para consultar estas encuestas.")]
    public async Task ApiErrorsPreserveTheHttpStatusAndReadableMessage(HttpStatusCode status, string message)
    {
        using var handler = new Handler(Json(new { message }, status));
        using var http = Http(handler);

        Func<Task> act = async () => await Client(http).GetAdminResultsAsync();

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(status);
        exception.Which.Message.Should().Be(message);
        handler.Requests.Should().ContainSingle().Which.Method.Should().Be("GET");
    }

    [Fact]
    public async Task AnEmptyForbiddenResponseStillShowsAReadablePermissionError()
    {
        using var handler = new Handler(new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var http = Http(handler);

        Func<Task> act = async () => await Client(http).GetAdminResultsAsync();

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        exception.Which.Message.Should().Contain("permiso");
    }

    [Fact]
    public async Task AnErrorOnALaterPageDoesNotReturnAnIncompleteReport()
    {
        using var handler = new Handler(
            Json(new { items = Enumerable.Range(1, 50).Select(Result).ToArray(), total = 51, page = 1, pageSize = 50 }),
            Json(new { error = "SURVEY_INVALID_PAGE" }, HttpStatusCode.BadRequest));
        using var http = Http(handler);

        Func<Task> act = async () => await Client(http).GetAdminResultsAsync();

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task NetworkFailureBecomesAReadableServiceUnavailableError()
    {
        using var handler = new Handler(new HttpRequestException("offline"));
        using var http = Http(handler);

        Func<Task> act = async () => await Client(http).GetAdminResultsAsync();

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        exception.Which.Message.Should().Contain("conectar");
    }

    [Fact]
    public async Task TimeoutBecomesAReadableGatewayTimeoutError()
    {
        using var handler = new Handler(new TaskCanceledException("timeout"));
        using var http = Http(handler);

        Func<Task> act = async () => await Client(http).GetAdminResultsAsync();

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var handler = new Handler(new TaskCanceledException());
        using var http = Http(handler);

        Func<Task> act = async () => await Client(http).GetAdminResultsAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvalidSuccessPayloadBecomesAnApiError()
    {
        using var handler = new Handler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") });
        using var http = Http(handler);

        Func<Task> act = async () => await Client(http).GetAdminResultsAsync();

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    private static SurveyApiClient Client(HttpClient http)
    {
        var authSession = new AuthSession();
        authSession.SetAccessToken("survey-test-token");
        return new SurveyApiClient(http, authSession);
    }

    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("http://api.local/") };
    private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(payload) };
    private static AdminSurveyResultDto Result(int index) => new()
    {
        ResponseId = Guid.Parse($"00000000-0000-0000-0000-{index:000000000000}"),
        SessionId = Guid.Parse($"10000000-0000-0000-0000-{index:000000000000}"),
        SessionCode = $"EXP-{index}", Condition = "AI", CompletionStatus = "Completed",
        SubmittedAtUtc = new DateTime(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc),
        Participant = new SurveyParticipantDto { Id = Guid.NewGuid(), FullName = "Agente", Email = "agente@example.test" },
        Definition = new SurveyDefinitionDto { Id = Guid.NewGuid(), Code = "post-session", Version = "1.0" }
    };

    private sealed record Request(string Method, string Path, string Authorization);
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        private readonly Exception? _failure;
        public List<Request> Requests { get; } = [];
        public Handler(params HttpResponseMessage[] responses) => _responses = new(responses);
        public Handler(Exception failure) { _responses = new(); _failure = failure; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString() ?? string.Empty));
            if (_failure is not null) throw _failure;
            if (_responses.Count == 0) throw new InvalidOperationException("Unexpected extra request.");
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
