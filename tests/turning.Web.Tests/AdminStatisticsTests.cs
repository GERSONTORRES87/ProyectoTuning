using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
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

        html.Should().Contain("conectar con la API");
        html.Should().Contain("role=\"alert\"");
    }

    [Fact]
    public async Task ResearcherSeesStatisticsWithoutLoadingOrEditingUserRoles()
    {
        var handler = new StatisticsHandler([]);

        var html = await RenderAsync(handler, "Researcher");

        html.Should().Contain("No hay sesiones activas");
        html.Should().NotContain("Roles de usuario");
        html.Should().NotContain("role-search");
        handler.UsersRequested.Should().Be(0);
    }

    [Fact]
    public async Task AdministratorCanManageRolesEvenWhenStatisticsAreUnavailable()
    {
        var handler = new StatisticsHandler([], failConnection: true);

        var html = await RenderAsync(handler, "Administrator");

        html.Should().Contain("conectar con la API");
        html.Should().Contain("Roles de usuario");
        html.Should().Contain("role-search");
        html.Should().Contain("agent@example.test");
        html.Should().Contain("Guardar");
        handler.UsersRequested.Should().Be(1);
    }

    private static async Task<string> RenderAsync(StatisticsHandler handler, string role = "Researcher")
    {
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://local.test/") };
        var authSession = new AuthSession();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton(new ExperimentSessionApiClient(client, authSession));
        services.AddSingleton(new AuthApiClient(client, authSession));
        services.AddSingleton<NavigationManager>(new TestNavigationManager());
        services.AddSingleton(new TokenAuthenticationStateProvider(new BrowserTokenStore(new UnusedJsRuntime()), authSession));
        services.AddSingleton<AuthenticationStateProvider>(new FixedAuthenticationStateProvider(role));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment admin = builder =>
            {
                builder.OpenComponent<Admin>(0);
                builder.CloseComponent();
            };
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CascadingAuthenticationState.ChildContent)] = admin
            });
            var result = await renderer.RenderComponentAsync<CascadingAuthenticationState>(parameters);
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
        public int UsersRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/auth/users")
            {
                UsersRequested++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new PagedUsersDto
                    {
                        Items = [new AuthenticatedUserDto
                        {
                            Id = Guid.NewGuid(), FullName = "Agent", Email = "agent@example.test", Role = "Participant"
                        }],
                        Total = 1, Page = 1, PageSize = 20
                    })
                });
            }
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

    private sealed class FixedAuthenticationStateProvider(string role) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "Test user"), new Claim(ClaimTypes.Role, role)], "test"))));
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
