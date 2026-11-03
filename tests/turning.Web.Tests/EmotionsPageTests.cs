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

public sealed class EmotionsPageTests
{
    [Fact]
    public async Task EmotionalBalanceRendersLatestPersistedIntensityForEachEmotion()
    {
        var handler = new EmotionsHandler();
        handler.Readings.AddRange([
            Reading("joy", 0.8, minute: 2), Reading("joy", 0.1, minute: 1), Reading("sadness", 0.2, minute: 3)
        ]);

        var html = await RenderAsync(handler);

        html.Should().Contain("Balance emocional").And.Contain("role=\"img\"").And.NotContain("<canvas");
        var chart = WebUtility.HtmlDecode(html[html.IndexOf("<svg", StringComparison.Ordinal)..]);
        chart.Should().Contain("Alegría: 80%").And.Contain("Tristeza: 20%");
        chart.Should().NotContain("Alegría: 10%").And.NotContain("82%").And.NotContain("74%");
    }

    [Fact]
    public async Task EmptySessionShowsEmptyIntensityChartWithoutDemoReadings()
    {
        var html = await RenderAsync(new EmotionsHandler());

        WebUtility.HtmlDecode(html).Should().Contain("No hay intensidades emocionales para mostrar");
        html.Should().NotContain("82%").And.NotContain("74%").And.NotContain("39%");
    }

    [Fact]
    public async Task SavePersistsSelfReportAndRefreshesTheChartFromTheBackend()
    {
        var handler = new EmotionsHandler();

        var html = await RenderAsync(handler, save: true);

        handler.PostedSessionId.Should().Be(handler.Session.Id);
        handler.PostedEmotion.Should().Be("neutral");
        handler.PostedScore.Should().Be(0.5);
        handler.PostedSource.Should().Be("self-report");
        WebUtility.HtmlDecode(html).Should().Contain("La lectura quedó guardada").And.Contain("Neutral: 50%");
    }

    [Theory]
    [InlineData("Owner", "Active", true)]
    [InlineData("Owner", "Completed", false)]
    [InlineData(null, "Active", false)]
    [InlineData("Interlocutor", "Active", false)]
    public async Task OnlyTheActiveOwnerCanSubmitAReading(string? participationRole, string status, bool canSave)
    {
        var handler = new EmotionsHandler(participationRole, status);

        var html = await RenderAsync(handler, save: !canSave);

        html.Contains("Guardar lectura emocional", StringComparison.Ordinal).Should().Be(canSave);
        handler.PostedSessionId.Should().BeNull();
    }

    private static async Task<string> RenderAsync(EmotionsHandler handler, bool save = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var js = new UnusedJsRuntime();
        services.AddSingleton<IJSRuntime>(js);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://local.test/") };
        var authSession = new AuthSession();
        services.AddSingleton(new ExperimentSessionApiClient(client, authSession));
        services.AddSingleton(new AuthApiClient(client, authSession));
        services.AddSingleton(new TokenAuthenticationStateProvider(new BrowserTokenStore(js), authSession));
        services.AddSingleton<NavigationManager>(new TestNavigationManager());
        var reference = new PageReference();
        services.AddSingleton(reference);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<EmotionsHost>();
            var load = typeof(Emotions).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)load.Invoke(reference.Page, [handler.Session])!;
            if (save)
            {
                var saveHandler = typeof(Emotions).GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)saveHandler.Invoke(reference.Page, null)!;
            }
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reference.Page, null);
            return rendered.ToHtmlString();
        });
    }

    private sealed class PageReference
    {
        public Emotions? Page { get; set; }
    }

    private sealed class EmotionsHost : ComponentBase
    {
        [Inject] public PageReference Reference { get; set; } = default!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Emotions>(0);
            builder.AddComponentReferenceCapture(1, component => Reference.Page = (Emotions)component);
            builder.CloseComponent();
        }
    }

    private sealed class EmotionsHandler(string? participationRole = "Owner", string status = "Active") : HttpMessageHandler
    {
        public ExperimentSessionSnapshotDto Session { get; } = new()
        {
            Id = Guid.NewGuid(), SessionCode = "EXP-EMOTIONS", ParticipationRole = participationRole, Status = status
        };
        public List<EmotionDto> Readings { get; } = [];
        public Guid? PostedSessionId { get; private set; }
        public string? PostedEmotion { get; private set; }
        public string? PostedSource { get; private set; }
        public double? PostedScore { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/me")
                return JsonResponse(new AuthenticatedUserDto { Id = Guid.NewGuid(), FullName = "Test", Email = "test@example.test", Role = "Participant" });
            if (path.StartsWith("/api/sessions/participant/", StringComparison.Ordinal))
                return JsonResponse(new ParticipantSessionsDto());
            if (path == "/api/experiment-sessions/latest") return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path == $"/api/sessions/{Session.Id}") return JsonResponse(Session);
            if (path == $"/api/sessions/{Session.Id}/emotions")
            {
                if (request.Method == HttpMethod.Get) return JsonResponse(Readings);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                PostedSessionId = Session.Id;
                PostedEmotion = body.RootElement.GetProperty("emotion").GetString();
                PostedScore = body.RootElement.GetProperty("score").GetDouble();
                PostedSource = body.RootElement.GetProperty("source").GetString();
                Readings.Add(Reading(PostedEmotion!, PostedScore.Value, minute: 10));
                return JsonResponse(new { saved = true });
            }
            throw new InvalidOperationException($"Unexpected path {path}.");
        }

        private static HttpResponseMessage JsonResponse<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private static EmotionDto Reading(string emotion, double score, int minute) => new(Guid.NewGuid(), emotion, score, "self-report", "manual", new DateTime(2026, 11, 3, 16, minute, 0, DateTimeKind.Utc));

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/emotions");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Unexpected JS call.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException("Unexpected JS call.");
    }
}
