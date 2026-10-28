using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using turning.Web.Auth;
using turning.Web.Components.Pages;
using turning.Web.ExperimentSessions;
using Xunit;

namespace turning.Web.Tests;

public sealed class ResultsPageTests
{
    [Fact]
    public async Task GlobalResultsShowEverySessionAndAChartOfActualReadings()
    {
        var handler = ResultsHandler.WithTwoSessions();

        var result = await RenderAsync(handler);

        result.Html.Should().Contain("EXP-FIRST").And.Contain("EXP-SECOND");
        result.Html.Should().Contain("role=\"img\"").And.NotContain("<canvas");
        WebUtility.HtmlDecode(result.Html).Should().Contain("3 lecturas");
        result.Html.Should().NotContain("78%").And.NotContain("84%");
        handler.RequestedPaths.Should().NotContain("/api/experiment-sessions/latest");
    }

    [Fact]
    public async Task NoSessionsKeepsAnEmptyChartWithoutInventingAnEmotion()
    {
        var result = await RenderAsync(new ResultsHandler([]));

        result.Html.Should().Contain("role=\"img\"");
        WebUtility.HtmlDecode(result.Html).Should().Contain("No hay sesiones").And.Contain("0 lecturas");
        result.Html.Should().NotContain("Calma").And.NotContain("78%").And.NotContain("84%");
    }

    [Fact]
    public async Task ConnectionFailureBecomesAnAccessibleErrorInsteadOfBreakingThePage()
    {
        RenderedResult? result = null;
        Func<Task> render = async () => result = await RenderAsync(new ResultsHandler([], failConnection: true));

        await render.Should().NotThrowAsync();

        result!.Html.Should().Contain("role=\"alert\"");
        WebUtility.HtmlDecode(result.Html).Should().Contain("No fue posible conectar");
    }

    [Fact]
    public async Task MissingSessionDetailShowsAnErrorInsteadOfAPartialReport()
    {
        var handler = ResultsHandler.WithTwoSessions();
        handler.MissingDetailId = handler.Sessions[1].Id;

        var result = await RenderAsync(handler);

        result.Html.Should().Contain("role=\"alert\"");
        result.Html.Should().NotContain("<table");
    }

    [Fact]
    public async Task ExportContainsTheSameSessionAndEmotionTotalsAsTheReport()
    {
        var handler = ResultsHandler.WithTwoSessions();

        var result = await RenderAsync(handler, export: true);

        result.DownloadJson.Should().NotBeNullOrWhiteSpace();
        using var document = JsonDocument.Parse(result.DownloadJson!);
        var report = document.RootElement;
        report.TryGetProperty("summary", out var summary).Should().BeTrue("an exported analysis must include the actual aggregate summary");
        summary.GetProperty("totalSessions").GetInt32().Should().Be(2);
        summary.GetProperty("totalMessages").GetInt32().Should().Be(8);
        summary.GetProperty("totalReadings").GetInt32().Should().Be(3);
        summary.GetProperty("degradedReadings").GetInt32().Should().Be(1);
        report.GetProperty("emotionCounts").GetProperty("joy").GetInt32().Should().Be(2);
        report.GetProperty("emotionCounts").GetProperty("sadness").GetInt32().Should().Be(1);
        report.GetProperty("sessions").GetArrayLength().Should().Be(2);
        report.GetProperty("sessions").ToString().Should().Contain("EXP-FIRST").And.Contain("EXP-SECOND");
        result.DownloadJson.Should().NotContain("\"conversation\"").And.NotContain("accessToken").And.NotContain("password");
    }

    private static async Task<RenderedResult> RenderAsync(ResultsHandler handler, bool export = false)
    {
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://local.test/") };
        var authSession = new AuthSession();
        var pageReference = new PageReference();
        var js = new DownloadJsRuntime();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ExperimentSessionApiClient(client, authSession));
        services.AddSingleton<NavigationManager>(new TestNavigationManager());
        services.AddSingleton(new TokenAuthenticationStateProvider(new BrowserTokenStore(js), authSession));
        services.AddSingleton<IJSRuntime>(js);
        services.AddSingleton(pageReference);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<ResultsHost>();
            if (export)
            {
                var exportHandler = typeof(Results).GetMethod("ExportReportAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)exportHandler.Invoke(pageReference.Page, null)!;
            }
            return new RenderedResult(rendered.ToHtmlString(), js.DownloadJson);
        });
    }

    private sealed record RenderedResult(string Html, string? DownloadJson);

    private sealed class PageReference
    {
        public Results? Page { get; set; }
    }

    private sealed class ResultsHost : ComponentBase
    {
        [Inject]
        public PageReference Reference { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Results>(0);
            builder.AddComponentReferenceCapture(1, component => Reference.Page = (Results)component);
            builder.CloseComponent();
        }
    }

    private sealed class ResultsHandler(List<ExperimentSessionSnapshotDto> sessions, bool failConnection = false) : HttpMessageHandler
    {
        public List<ExperimentSessionSnapshotDto> Sessions { get; } = sessions;
        public Dictionary<Guid, List<EmotionReadingDto>> Readings { get; } = [];
        public List<string> RequestedPaths { get; } = [];
        public Guid? MissingDetailId { get; set; }

        public static ResultsHandler WithTwoSessions()
        {
            var first = Session("EXP-FIRST", "Active", 3, 2);
            var second = Session("EXP-SECOND", "TimedOut", 5, 1);
            var handler = new ResultsHandler([first, second]);
            handler.Readings[first.Id] = [Reading("joy"), Reading("joy")];
            handler.Readings[second.Id] = [Reading("sadness", isDegraded: true)];
            return handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            RequestedPaths.Add(path);
            if (failConnection) throw new HttpRequestException("offline");
            if (path == "/api/experiment-sessions/latest")
                return Task.FromResult(Sessions.Count == 0
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : JsonResponse(Sessions[0]));
            if (path == "/api/results")
                return Task.FromResult(JsonResponse(new SessionListDto { Items = Sessions, Total = Sessions.Count }));

            var id = Guid.Parse(path.Split('/')[3]);
            if (id == MissingDetailId) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(JsonResponse(new SessionResultDto
            {
                Session = Sessions.Single(session => session.Id == id),
                EmotionReadings = Readings.GetValueOrDefault(id) ?? []
            }));
        }

        private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

        private static ExperimentSessionSnapshotDto Session(string code, string status, int messages, int emotions) => new()
        {
            Id = Guid.NewGuid(), SessionCode = code, Condition = "AI", Status = status,
            AvatarState = "neutral", ConversationStage = "available", EmotionStage = "available", AvatarStage = "available",
            ConversationTurnCount = messages, EmotionSampleCount = emotions,
            CreatedAtUtc = new DateTime(2026, 10, 27, 16, 0, 0, DateTimeKind.Utc)
        };

        private static EmotionReadingDto Reading(string emotion, bool isDegraded = false) => new()
        {
            Emotion = emotion, Score = 0.8, IsDegraded = isDegraded,
            CapturedAtUtc = new DateTime(2026, 10, 27, 16, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/results");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private sealed class DownloadJsRuntime : IJSRuntime
    {
        public string? DownloadJson { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier != "sueDownloadJson") throw new InvalidOperationException("Unexpected JavaScript call.");
            DownloadJson = (string)args![1]!;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
