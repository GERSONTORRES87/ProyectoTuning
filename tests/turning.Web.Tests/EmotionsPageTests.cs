using System.Net;
using FluentAssertions;
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
    public async Task EmotionalBalanceRendersIntensityBarsWithTheLevelsShownByThePage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime>(new UnusedJsRuntime());
        using var client = new HttpClient { BaseAddress = new Uri("http://unused.test/") };
        services.AddSingleton(new ExperimentSessionApiClient(client, new AuthSession()));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<Emotions>();
            return result.ToHtmlString();
        });

        html.Should().Contain("Balance emocional");
        html.Should().Contain("role=\"img\"");
        html.Should().NotContain("<canvas");
        var chart = WebUtility.HtmlDecode(html[html.IndexOf("<svg", StringComparison.Ordinal)..]);
        chart.Should().Contain("Alegría").And.Contain("82%");
        chart.Should().Contain("Calma").And.Contain("74%");
        chart.Should().Contain("Tristeza").And.Contain("39%");
        chart.Should().Contain("Ansiedad").And.Contain("28%");
    }

    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Unexpected JS call.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException("Unexpected JS call.");
    }
}
