using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using turning.Web.Components;
using turning.Web.Surveys;
using Xunit;

namespace turning.Web.Tests;

public sealed class SurveyResultsPanelTests
{
    [Fact]
    public async Task LikertAnswerShowsItsQuestionAndTheActualFourOutOfFiveRating()
    {
        var html = await RenderAsync(new Dictionary<string, string> { ["EMPATHY"] = "4" });
        var document = Parse(html);

        var meter = Meter(document);
        meter.Attribute("aria-valuemin")!.Value.Should().Be("1");
        meter.Attribute("aria-valuemax")!.Value.Should().Be("5");
        meter.Attribute("aria-valuenow")!.Value.Should().Be("4");
        meter.Attribute("aria-label")!.Value.Should().Be(QuestionText);
        meter.Descendants("span").Count(element => element.Attribute("class")?.Value.Contains("is-filled") == true).Should().Be(4);
        document.Value.Should().Contain("4/5").And.Contain(QuestionText).And.Contain("Vista previa");
    }

    [Fact]
    public async Task ScaleQuestionShowsTheSameOneToFiveRatingAsTheStoredSurvey()
    {
        var definition = SingleQuestion("empathy", "scale", "Empatía percibida");
        var document = Parse(await RenderAsync(new Dictionary<string, string> { ["empathy"] = "4" }, definition));

        var meter = Meter(document);
        meter.Attribute("aria-valuemin")!.Value.Should().Be("1");
        meter.Attribute("aria-valuemax")!.Value.Should().Be("5");
        meter.Attribute("aria-valuenow")!.Value.Should().Be("4");
        meter.Descendants("span").Count(element => element.Attribute("class")?.Value.Contains("is-filled") == true).Should().Be(4);
        document.Value.Should().Contain("4/5");
    }

    [Theory]
    [InlineData("Human", "Humano")]
    [InlineData("AI", "IA")]
    [InlineData("Unknown", "No estoy seguro")]
    [InlineData("Unsure", "No estoy seguro")]
    [InlineData("<b>Otra opción</b>", "<b>Otra opción</b>")]
    public async Task ChoiceAnswersHaveFaithfulReadableLabelsWithoutRenderingHtml(string answer, string expectedLabel)
    {
        var definition = SingleQuestion("perceived_interlocutor", "choice", "Interlocutor percibido");
        var html = await RenderAsync(new Dictionary<string, string> { ["perceived_interlocutor"] = answer }, definition);

        Parse(html).Descendants("p").Select(element => element.Value).Should().Contain(expectedLabel);
        html.Should().NotContain("<b>");
    }

    [Fact]
    public async Task MissingAnswersRemainPendingWithoutInventingRatings()
    {
        var document = Parse(await RenderAsync(new Dictionary<string, string>()));

        document.Descendants().Where(element => element.Attribute("role")?.Value == "meter").Should().BeEmpty();
        document.Value.Should().Contain("Pendiente").And.Contain("Sin respuesta").And.NotContain("4/5");
        document.Value.Should().NotContain("Encuesta enviada");
    }

    [Fact]
    public async Task TextCommentsAreRenderedAsEncodedText()
    {
        const string comment = "<script>alert('x')</script>\nGracias & ánimo";

        var html = await RenderAsync(new Dictionary<string, string> { ["COMMENT"] = comment });

        html.Should().NotContain("<script>");
        Parse(html).Value.Should().Contain(comment);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    public async Task OutOfRangeAnswersNeverProduceARatingBar(string answer)
    {
        var document = Parse(await RenderAsync(new Dictionary<string, string> { ["EMPATHY"] = answer }));

        document.Descendants().Where(element => element.Attribute("role")?.Value == "meter").Should().BeEmpty();
        document.Value.Should().Contain("Respuesta no válida");
    }

    [Fact]
    public async Task UpdatingAnswersAndSubmissionStateUpdatesTheSameRenderedPanel()
    {
        var state = new PanelState { Answers = new Dictionary<string, string> { ["EMPATHY"] = "4" } };
        var servicesCollection = new ServiceCollection();
        servicesCollection.AddLogging();
        servicesCollection.AddSingleton(state);
        await using var services = servicesCollection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<PanelHost>();
            var preview = Parse(root.ToHtmlString());
            Meter(preview).Attribute("aria-valuenow")!.Value.Should().Be("4");
            preview.Value.Should().Contain("Vista previa");

            await state.UpdateAsync(new Dictionary<string, string> { ["EMPATHY"] = "2" }, submitted: true);

            var submitted = Parse(root.ToHtmlString());
            Meter(submitted).Attribute("aria-valuenow")!.Value.Should().Be("2");
            submitted.Value.Should().Contain("2/5").And.Contain("Encuesta enviada").And.NotContain("Vista previa");
        });
    }

    private const string QuestionText = "¿Sentiste que la conversación te permitió expresar con suficiente claridad tus emociones y que tus respuestas fueron escuchadas con atención?";

    private static SurveyDefinitionDto Definition() => new()
    {
        Id = Guid.NewGuid(), Code = "TEST", Version = "1",
        Questions =
        [
            new() { Id = Guid.NewGuid(), Code = "EMPATHY", Text = QuestionText, Type = "likert-1-5", Required = true, Order = 1 },
            new() { Id = Guid.NewGuid(), Code = "COMMENT", Text = "¿Quieres añadir un comentario?", Type = "text", Order = 2 }
        ]
    };

    private static SurveyDefinitionDto SingleQuestion(string code, string type, string text) => new()
    {
        Id = Guid.NewGuid(), Code = "TEST", Version = "1",
        Questions = [new() { Id = Guid.NewGuid(), Code = code, Type = type, Text = text, Required = true, Order = 1 }]
    };

    private static XElement Meter(XElement document) => document.Descendants()
        .Single(element => element.Attribute("role")?.Value == "meter");

    private static XElement Parse(string html) => XElement.Parse(Regex.Replace(html, @"\s(b-[a-z0-9]+)(?=[\s>])", " $1=\"\""));

    private static async Task<string> RenderAsync(IReadOnlyDictionary<string, string> answers, SurveyDefinitionDto? definition = null)
    {
        var servicesCollection = new ServiceCollection();
        servicesCollection.AddLogging();
        await using var services = servicesCollection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<SurveyResultsPanel>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(SurveyResultsPanel.Definition)] = definition ?? Definition(),
                    [nameof(SurveyResultsPanel.Answers)] = answers
                }));
            return root.ToHtmlString();
        });
    }

    private sealed class PanelState
    {
        public SurveyDefinitionDto Definition { get; } = SurveyResultsPanelTests.Definition();
        public IReadOnlyDictionary<string, string> Answers { get; set; } = new Dictionary<string, string>();
        public bool Submitted { get; private set; }
        public Func<Task>? Changed { get; set; }

        public async Task UpdateAsync(IReadOnlyDictionary<string, string> answers, bool submitted)
        {
            Answers = answers;
            Submitted = submitted;
            if (Changed is not null) await Changed();
        }
    }

    private sealed class PanelHost : ComponentBase
    {
        [Inject]
        public PanelState State { get; set; } = default!;

        protected override void OnInitialized() => State.Changed = () => InvokeAsync(StateHasChanged);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SurveyResultsPanel>(0);
            builder.AddAttribute(1, nameof(SurveyResultsPanel.Definition), State.Definition);
            builder.AddAttribute(2, nameof(SurveyResultsPanel.Answers), State.Answers);
            builder.AddAttribute(3, nameof(SurveyResultsPanel.Submitted), State.Submitted);
            builder.CloseComponent();
        }
    }
}
