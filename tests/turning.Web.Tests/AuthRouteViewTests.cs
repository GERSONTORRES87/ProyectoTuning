using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using turning.Web.Components;
using Xunit;

namespace turning.Web.Tests;

public sealed class AuthRouteViewTests
{
    [Fact]
    public async Task AuthenticatedParticipantSeesAccessDeniedWithoutBeingSentBackToLogin()
    {
        var html = await RenderRouteAsync(typeof(PrivilegedPage), "Participant");

        html.Should().Contain("role=\"alert\"");
        html.Should().Contain("href=\"/workspace\"");
        html.Should().NotContain("privileged-page");
        html.Should().NotContain("Redirigiendo al login");
    }

    [Fact]
    public async Task AnonymousVisitorIsSentToLoginWithoutRenderingTheProtectedPage()
    {
        var html = await RenderRouteAsync(typeof(PrivilegedPage), role: null);

        html.Should().Contain("Redirigiendo al login");
        html.Should().NotContain("privileged-page");
        html.Should().NotContain("role=\"alert\"");
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Researcher")]
    public async Task AllowedRolesCanSeeThePrivilegedPage(string role)
    {
        var html = await RenderRouteAsync(typeof(PrivilegedPage), role);

        html.Should().Contain("privileged-page");
        html.Should().Contain("test-layout");
        html.Should().NotContain("role=\"alert\"");
        html.Should().NotContain("Redirigiendo al login");
    }

    [Fact]
    public async Task ParticipantCanSeeAPageThatOnlyRequiresAuthentication()
    {
        var html = await RenderRouteAsync(typeof(ParticipantPage), "Participant");

        html.Should().Contain("participant-page");
        html.Should().NotContain("role=\"alert\"");
        html.Should().NotContain("Redirigiendo al login");
    }

    private static async Task<string> RenderRouteAsync(Type pageType, string? role)
    {
        var identity = role is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Test user"), new Claim(ClaimTypes.Role, role) }, "test");
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddAuthorizationCore();
        serviceCollection.AddSingleton<AuthenticationStateProvider>(new FixedAuthenticationStateProvider(new ClaimsPrincipal(identity)));
        serviceCollection.AddSingleton<NavigationManager>(new TestNavigationManager());
        await using var services = serviceCollection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment route = builder =>
            {
                builder.OpenComponent<AuthRouteView>(0);
                builder.AddAttribute(1, nameof(AuthRouteView.RouteData), new RouteData(pageType, new Dictionary<string, object?>()));
                builder.AddAttribute(2, nameof(AuthRouteView.DefaultLayout), typeof(TestLayout));
                builder.CloseComponent();
            };
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CascadingAuthenticationState.ChildContent)] = route
            });
            var result = await renderer.RenderComponentAsync<CascadingAuthenticationState>(parameters);
            return result.ToHtmlString();
        });
    }

    [Authorize(Roles = "Administrator,Researcher")]
    private sealed class PrivilegedPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p>privileged-page</p>");
    }

    [Authorize]
    private sealed class ParticipantPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p>participant-page</p>");
    }

    private sealed class TestLayout : LayoutComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "main");
            builder.AddAttribute(1, "class", "test-layout");
            builder.AddContent(2, Body);
            builder.CloseElement();
        }
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/admin");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Static rendering should not navigate.");
    }
}
