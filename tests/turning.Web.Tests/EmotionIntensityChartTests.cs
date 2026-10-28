using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using turning.Web.Components;
using Xunit;

namespace turning.Web.Tests;

public sealed class EmotionIntensityChartTests
{
    [Fact]
    public async Task EachIntensityUsesTheFullScaleInsteadOfAShareOfTheTotal()
    {
        var document = await RenderAsync(new Dictionary<string, int> { ["Alegría"] = 82, ["Calma"] = 74 },
            new Dictionary<string, string> { ["Alegría"] = "#E8C9A0", ["Calma"] = "#B5C4B1" });

        var joy = Bar(document, "Alegría:");
        var calm = Bar(document, "Calma:");
        ShareOfScale(joy).Should().BeApproximately(0.82, 0.0001);
        ShareOfScale(calm).Should().BeApproximately(0.74, 0.0001);
        joy.Attribute("aria-label")!.Value.Should().Be("Alegría: 82%");
        calm.Attribute("aria-label")!.Value.Should().Be("Calma: 74%");
        joy.Attribute("fill")!.Value.Should().Be("#E8C9A0");
        document.Descendants("svg").Single().Attribute("role")!.Value.Should().Be("img");
        document.Descendants("desc").Single().Value.Should().Contain("Alegría: 82%").And.Contain("Calma: 74%");
    }

    [Fact]
    public async Task ZeroIntensityRemainsVisibleWithAZeroWidthBar()
    {
        var document = await RenderAsync(new Dictionary<string, int> { ["Calma"] = 0 });

        var calm = Bar(document, "Calma:");
        Width(calm).Should().Be(0);
        calm.Attribute("aria-label")!.Value.Should().Be("Calma: 0%");
        document.Value.Should().NotContain("No hay intensidades");
    }

    [Fact]
    public async Task ValuesOutsideTheIntensityRangeAreClamped()
    {
        var document = await RenderAsync(new Dictionary<string, int> { ["Baja"] = -10, ["Alta"] = 150 });

        ShareOfScale(Bar(document, "Baja:")).Should().Be(0);
        ShareOfScale(Bar(document, "Alta:")).Should().Be(1);
        document.Descendants("desc").Single().Value.Should().Contain("Baja: 0%").And.Contain("Alta: 100%");
    }

    [Fact]
    public async Task EmptyDataShowsAxesAndAnExplicitEmptyState()
    {
        var document = await RenderAsync(new Dictionary<string, int>());

        document.Descendants("svg").Should().ContainSingle();
        document.Descendants("line").Should().NotBeEmpty();
        document.Descendants("rect").Should().BeEmpty();
        document.Value.Should().Contain("No hay intensidades emocionales para mostrar");
    }

    [Fact]
    public async Task SvgDecimalsRemainValidUnderSpanishCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-CO");
            var document = await RenderAsync(new Dictionary<string, int> { ["Alegría"] = 82 });

            Width(Bar(document, "Alegría:")).Should().BeApproximately(303.4, 0.001);
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

    [Fact]
    public async Task ChangingParametersUpdatesTheSameRenderedChart()
    {
        var state = new ChartState { Levels = new Dictionary<string, int> { ["Alegría"] = 82 } };
        var servicesCollection = new ServiceCollection();
        servicesCollection.AddLogging();
        servicesCollection.AddSingleton(state);
        await using var services = servicesCollection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<ChartHost>();
            ShareOfScale(Bar(Parse(result.ToHtmlString()), "Alegría:")).Should().BeApproximately(0.82, 0.0001);

            await state.UpdateAsync(new Dictionary<string, int> { ["Alegría"] = 20 });

            var updated = Parse(result.ToHtmlString());
            ShareOfScale(Bar(updated, "Alegría:")).Should().BeApproximately(0.2, 0.0001);
            updated.Descendants("desc").Single().Value.Should().Contain("Alegría: 20%").And.NotContain("82%");
        });
    }

    private static XElement Bar(XElement document, string label) => document.Descendants("rect")
        .Single(element => element.Attribute("aria-label")?.Value.StartsWith(label, StringComparison.Ordinal) == true);

    private static double Width(XElement rectangle) => double.Parse(rectangle.Attribute("width")!.Value, CultureInfo.InvariantCulture);

    private static double ShareOfScale(XElement bar) => Width(bar) / Width(bar.Parent!.Elements("rect")
        .Single(rectangle => rectangle.Attribute("aria-label") is null));

    private static XElement Parse(string html) => XElement.Parse(Regex.Replace(html, @"\s(b-[a-z0-9]+)(?=[\s>])", " $1=\"\""));

    private static async Task<XElement> RenderAsync(IReadOnlyDictionary<string, int> levels, IReadOnlyDictionary<string, string>? colors = null)
    {
        var servicesCollection = new ServiceCollection();
        servicesCollection.AddLogging();
        await using var services = servicesCollection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = await renderer.RenderComponentAsync<EmotionIntensityChart>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(EmotionIntensityChart.Levels)] = levels,
                    [nameof(EmotionIntensityChart.Colors)] = colors ?? new Dictionary<string, string>()
                }));
            return result.ToHtmlString();
        });
        return Parse(html);
    }

    private sealed class ChartState
    {
        public IReadOnlyDictionary<string, int> Levels { get; set; } = new Dictionary<string, int>();
        public Func<Task>? Changed { get; set; }

        public async Task UpdateAsync(IReadOnlyDictionary<string, int> levels)
        {
            Levels = levels;
            if (Changed is not null) await Changed();
        }
    }

    private sealed class ChartHost : ComponentBase
    {
        [Inject]
        public ChartState State { get; set; } = default!;

        protected override void OnInitialized() => State.Changed = () => InvokeAsync(StateHasChanged);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EmotionIntensityChart>(0);
            builder.AddAttribute(1, nameof(EmotionIntensityChart.Levels), State.Levels);
            builder.CloseComponent();
        }
    }
}
