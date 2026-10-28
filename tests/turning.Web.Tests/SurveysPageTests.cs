using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using turning.Web.Auth;
using turning.Web.Surveys;
using Xunit;

namespace turning.Web.Tests;

public sealed class SurveysPageTests
{
    [Fact]
    public async Task AdministratorSeesTheAgentsSavedResultsWithoutAnAnswerForm()
    {
        var handler = new SurveyHandler();
        var html = WebUtility.HtmlDecode(await RenderAsync(handler));
        Regex.IsMatch(html, @"4\s*/\s*5").Should().BeTrue();
        html.Should().Contain("EXP-SURVEY").And.Contain("Agente de prueba").And.Contain("agente@example.local").And.Contain("v1.0");
        html.Should().NotContain("Enviar encuesta").And.NotContain("type=\"radio\"");
        handler.Posts.Should().Be(0);
    }

    [Fact]
    public async Task EmptyResultsExplainThatAgentsHaveNotSubmittedSurveys()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync(new SurveyHandler(empty: true)));
        html.Should().Contain("encuestas enviadas").And.NotContain("Próximamente");
        html.Should().NotContain("4/5").And.NotContain("type=\"radio\"");
    }

    [Fact]
    public async Task ParticipantDoesNotFetchThePrivilegedResults()
    {
        var handler = new SurveyHandler(role: "Participant");
        var html = await RenderAsync(handler);
        handler.ResultRequests.Should().Be(0);
        html.Should().NotContain("agente@example.local");
    }

    [Fact]
    public async Task NetworkFailureIsVisibleInsteadOfLeavingThePlaceholder()
    {
        var html = WebUtility.HtmlDecode(await RenderAsync(new SurveyHandler(offline: true)));
        html.Should().Contain("role=\"alert\"").And.Contain("No fue posible conectar");
    }

    [Theory]
    [InlineData("4.5", "scale")]
    [InlineData("5", "text")]
    public async Task NonScaleOrNonIntegerAnswersDoNotBecomeAnEmpathyAverage(string rating, string questionType)
    {
        var html = WebUtility.HtmlDecode(await RenderAsync(new SurveyHandler(rating: rating, questionType: questionType)));
        html.Should().NotContain("aria-label=\"Empatía\"");
    }

    private static async Task<string> RenderAsync(SurveyHandler handler)
    {
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://local.test/") };
        var authSession = new AuthSession();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new AuthApiClient(http, authSession));
        services.AddSingleton(new SurveyApiClient(http, authSession));
        services.AddSingleton<NavigationManager>(new TestNavigationManager());
        services.AddSingleton(new TokenAuthenticationStateProvider(new BrowserTokenStore(new UnusedJsRuntime()), authSession));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<turning.Web.Components.Pages.Surveys>()).ToHtmlString());
    }

    private sealed class SurveyHandler(string role = "Administrator", bool empty = false, bool offline = false, string rating = "4", string questionType = "scale") : HttpMessageHandler
    {
        public int Posts { get; private set; }
        public int ResultRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (offline) throw new HttpRequestException("offline");
            if (request.Method == HttpMethod.Post) { Posts++; throw new InvalidOperationException("Read-only view must not submit answers."); }
            object body;
            if (request.RequestUri!.AbsolutePath == "/api/auth/me")
                body = new AuthenticatedUserDto { Id = Guid.NewGuid(), Email = "admin@example.local", FullName = "Admin", Role = role };
            else if (request.RequestUri.AbsolutePath == "/api/surveys/results")
            {
                ResultRequests++;
                var questionId = Guid.NewGuid();
                var row = new
                {
                    responseId = Guid.NewGuid(), sessionId = Guid.NewGuid(), sessionCode = "EXP-SURVEY",
                    condition = "AI", completionStatus = "Completed", submittedAtUtc = DateTime.UtcNow,
                    participant = new { id = Guid.NewGuid(), fullName = "Agente de prueba", email = "agente@example.local" },
                    definition = new { id = Guid.NewGuid(), code = "default-v1", version = "1.0", questions = new[]
                    {
                        new { id = questionId, code = "empathy", text = "Sentí empatía durante la conversación (1: nada, 5: mucha).", type = questionType, required = true, order = 1 }
                    }},
                    answers = new[] { new { questionId, value = rating } }
                };
                body = new { items = empty ? Array.Empty<object>() : new object[] { row }, total = empty ? 0 : 1 };
            }
            else throw new InvalidOperationException("Unexpected request path.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) });
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/surveys");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }
    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Unexpected JS call.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException("Unexpected JS call.");
    }
}
