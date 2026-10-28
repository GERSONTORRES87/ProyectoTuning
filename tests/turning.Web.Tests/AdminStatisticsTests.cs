using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using turning.Web.Auth;
using turning.Web.Components.Pages;
using turning.Web.ExperimentSessions;
using Xunit;

namespace turning.Web.Tests;

public sealed class AdminStatisticsTests
{
    [Fact]
    public async Task EmptyDashboardKeepsTheChartVisibleAndExplainsTheMissingActivity()
    {
        var html = await RenderAsync(new StatisticsHandler([]));

        html.Should().Contain("role=\"img\"");
        html.Should().Contain("No hay sesiones activas");
        html.Should().Contain("Historial");
    }

    [Fact]
    public async Task ActiveViewCountsActualReadingsAndExcludesExpiredSessions()
    {
        var active = Session("EXP-ACTIVE", "Active");
        var expired = Session("EXP-EXPIRED", "TimedOut");
        var handler = new StatisticsHandler([active, expired], new Dictionary<Guid, List<EmotionReadingDto>>
        {
            [active.Id] = [Reading("joy"), Reading("joy"), Reading("sadness")],
            [expired.Id] = [Reading("anger")]
        });

        var html = await RenderAsync(handler);

        html.Should().Contain("EXP-ACTIVE");
        html.Should().NotContain("EXP-EXPIRED");
        html.Should().Contain("role=\"img\"");
        handler.DetailsRequested.Should().Equal(active.Id);
    }

    [Fact]
    public async Task ConnectionFailureIsDisplayedInsteadOfBreakingTheDashboard()
    {
        var html = await RenderAsync(new StatisticsHandler([], failConnection: true));

        html.Should().Contain("No fue posible conectar");
        html.Should().Contain("role=\"alert\"");
    }

    private static async Task<string> RenderAsync(StatisticsHandler handler)
    {
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://local.test/") };
        var authSession = new AuthSession();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ExperimentSessionApiClient(client, authSession));
        services.AddSingleton<NavigationManager>(new TestNavigationManager());
        services.AddSingleton(new TokenAuthenticationStateProvider(new BrowserTokenStore(new UnusedJsRuntime()), authSession));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<Admin>();
            return result.ToHtmlString();
        });
    }

    private static ExperimentSessionSnapshotDto Session(string code, string status) => new()
    {
        Id = Guid.NewGuid(), SessionCode = code, Condition = "AI", Status = status,
        AvatarState = "neutral", ConversationStage = "available", EmotionStage = "available", AvatarStage = "available"
    };

    private static EmotionReadingDto Reading(string emotion) => new()
    {
        Emotion = emotion, Score = 0.8, CapturedAtUtc = new DateTime(2026, 10, 27, 16, 0, 0, DateTimeKind.Utc)
    };

    private sealed class StatisticsHandler(
        List<ExperimentSessionSnapshotDto> sessions,
        Dictionary<Guid, List<EmotionReadingDto>>? readings = null,
        bool failConnection = false) : HttpMessageHandler
    {
        public List<Guid> DetailsRequested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (failConnection) throw new HttpRequestException("offline");
            if (request.RequestUri!.AbsolutePath == "/api/results")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new SessionListDto { Items = sessions, Total = sessions.Count })
                });

            var id = Guid.Parse(request.RequestUri.AbsolutePath.Split('/')[3]);
            DetailsRequested.Add(id);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SessionResultDto
                {
                    Session = sessions.Single(session => session.Id == id),
                    EmotionReadings = readings?.GetValueOrDefault(id) ?? []
                })
            });
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/admin");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Unexpected JS call.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException("Unexpected JS call.");
    }
}
