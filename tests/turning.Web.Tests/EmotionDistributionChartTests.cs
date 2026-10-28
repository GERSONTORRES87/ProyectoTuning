using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using turning.Web.Components;
using Xunit;

namespace turning.Web.Tests;

public sealed class EmotionDistributionChartTests
{
    [Fact]
    public async Task CountsProduceBarsWithRealProportionsAndAccessibleLabels()
    {
        var document = await RenderAsync(new Dictionary<string, int> { ["joy"] = 2, ["sadness"] = 1 });

        var chart = document.Descendants("svg").Single();
        chart.Attribute("role")!.Value.Should().Be("img");
        var joy = Bar(document, "Alegría:");
        var sadness = Bar(document, "Tristeza:");
        joy.Attribute("aria-label")!.Value.Should().Contain("2 lecturas").And.Contain("66,7%");
        sadness.Attribute("aria-label")!.Value.Should().Contain("1 lectura").And.Contain("33,3%");
        Width(joy).Should().BeApproximately(2 * Width(sadness), 0.01);
        document.Value.Should().Contain("3 lecturas");
        document.Descendants("desc").Single().Value.Should().Contain("Alegría: 2 lecturas, 66,7%");
    }

    [Fact]
    public async Task EmptyCountsKeepChartAxesAndClearlyStateThereAreNoReadings()
    {
        var document = await RenderAsync(new Dictionary<string, int>());

        document.Descendants("svg").Should().ContainSingle();
        document.Descendants("line").Should().NotBeEmpty();
        document.Descendants("rect").Where(element => element.Attribute("aria-label") is not null).Should().BeEmpty();
        document.Value.Should().Contain("0 lecturas").And.Contain("Sin lecturas emocionales");
    }

    [Fact]
    public async Task EmotionNamesAreGroupedIgnoringCaseWithoutLosingCounts()
    {
        var document = await RenderAsync(new Dictionary<string, int> { ["Joy"] = 1, ["joy"] = 2, ["neutral"] = 1 });

        var joy = Bar(document, "Alegría:");
        joy.Attribute("aria-label")!.Value.Should().Contain("3 lecturas").And.Contain("75%");
        document.Descendants("rect").Count(element => element.Attribute("aria-label")?.Value.StartsWith("Alegría:") == true).Should().Be(1);
        document.Value.Should().Contain("4 lecturas");
    }

    [Fact]
    public async Task UnknownEmotionsKeepTheirNameAndRealShare()
    {
        var document = await RenderAsync(new Dictionary<string, int> { ["Mi emoción"] = 4 });

        var emotion = Bar(document, "Mi emoción:");
        emotion.Attribute("aria-label")!.Value.Should().Contain("4 lecturas").And.Contain("100%");
        document.Value.Should().Contain("Mi emoción");
    }

    [Fact]
    public async Task SvgCoordinatesUseInvariantDecimalsWhenTheUiUsesSpanishCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-CO");
            var document = await RenderAsync(new Dictionary<string, int> { ["joy"] = 2, ["sadness"] = 1 });

            document.Descendants("line").Select(line => line.Attribute("x1")!.Value).Should().Contain("282.5");
            foreach (var line in document.Descendants("line"))
            {
                double.TryParse(line.Attribute("x1")!.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _).Should().BeTrue();
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static XElement Bar(XElement document, string label) => document.Descendants("rect")
        .Single(element => element.Attribute("aria-label")?.Value.StartsWith(label, StringComparison.Ordinal) == true);

    private static double Width(XElement bar) => double.Parse(bar.Attribute("width")!.Value, CultureInfo.InvariantCulture);

    private static async Task<XElement> RenderAsync(IReadOnlyDictionary<string, int> counts)
    {
        var servicesCollection = new ServiceCollection();
        servicesCollection.AddLogging();
        await using var services = servicesCollection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<EmotionDistributionChart>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(EmotionDistributionChart.Counts)] = counts }));
            return result.ToHtmlString();
        });

        // Scoped CSS emits minimized HTML attributes, which XML does not accept.
        var xml = Regex.Replace(html, @"\s(b-[a-z0-9]+)(?=[\s>])", " $1=\"\"");
        return XElement.Parse(xml);
    }
}
