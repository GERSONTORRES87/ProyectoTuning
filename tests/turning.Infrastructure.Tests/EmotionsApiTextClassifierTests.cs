using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turning.Domain.Entities;
using Turning.Infrastructure.AI.Emotions;
using Xunit;

namespace Turning.Infrastructure.Tests;

public class EmotionsApiTextClassifierTests
{
    private const string Token = "token-secreto-de-prueba";

    private static (EmotionsApiTextClassifier Classifier, FakeHandler Handler, List<string> Logs) Create(
        HttpStatusCode status, string json, string? baseUrl = "http://sue-emotions/", TimeSpan? delay = null)
    {
        var handler = new FakeHandler(status, json, delay ?? TimeSpan.Zero);
        var logs = new List<string>();
        var options = Options.Create(new EmotionsApiOptions { BaseUrl = baseUrl, Token = Token, TimeoutMs = 200 });
        return (new EmotionsApiTextClassifier(new HttpClient(handler), options, new ListLogger(logs)), handler, logs);
    }

    [Fact]
    public async Task PostsTheTextWithBearerAndMapsTheSevenClassesToSueLabels()
    {
        var (classifier, handler, _) = Create(HttpStatusCode.OK, """
            {"emotion":"FEAR","confidence":0.71,
             "probabilities":{"ANGER":0.05,"DISGUST":0.04,"FEAR":0.71,"HAPPINESS":0.03,"NEUTRAL":0.1,"SADNESS":0.05,"SURPRISE":0.02},
             "modelVersions":{"text":"robertuito-emotion-analysis@abc12345"}}
            """);

        var result = await classifier.ClassifyAsync("me da miedo salir");

        handler.Method.Should().Be(HttpMethod.Post);
        handler.Uri!.AbsoluteUri.Should().Be("http://sue-emotions/api/v1/emotions/text");
        handler.Authorization.Should().Be($"Bearer {Token}");
        using (var body = JsonDocument.Parse(handler.Body!))
        {
            body.RootElement.GetProperty("text").GetString().Should().Be("me da miedo salir");
            body.RootElement.GetProperty("language").GetString().Should().Be("es");
        }

        result!.EmotionId.Should().Be(EmotionLabel.Fear);
        result.Confidence.Should().Be(0.71);
        result.ModelVersion.Should().Be("robertuito-emotion-analysis@abc12345");
        result.Scores.Keys.Should().BeEquivalentTo(
            [EmotionLabel.Anger, EmotionLabel.Disgust, EmotionLabel.Fear, EmotionLabel.Joy, EmotionLabel.Neutral, EmotionLabel.Sadness, EmotionLabel.Surprise]);
    }

    [Fact]
    public async Task ReturnsNullWithoutCallingWhenThereIsNoBaseUrl()
    {
        var (classifier, handler, _) = Create(HttpStatusCode.OK, "{}", baseUrl: null);

        (await classifier.ClassifyAsync("hola")).Should().BeNull();
        handler.Requests.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ReturnsNullOnErrorsWithoutLoggingTheTokenOrTheText(HttpStatusCode status)
    {
        var (classifier, _, logs) = Create(status, """{"error":"x"}""");

        (await classifier.ClassifyAsync("texto privado del participante")).Should().BeNull();
        logs.Should().NotBeEmpty();
        logs.Should().NotContain(line => line.Contains(Token) || line.Contains("texto privado"));
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownClass()
    {
        var (classifier, _, _) = Create(HttpStatusCode.OK, """{"emotion":"CONTEMPT","confidence":0.9}""");

        (await classifier.ClassifyAsync("hola")).Should().BeNull();
    }

    [Fact]
    public async Task ReturnsNullWhenTheApiDoesNotAnswerInTime()
    {
        var (classifier, _, _) = Create(HttpStatusCode.OK, """{"emotion":"NEUTRAL","confidence":0.9}""", delay: TimeSpan.FromSeconds(5));

        (await classifier.ClassifyAsync("hola")).Should().BeNull();
    }

    private sealed class FakeHandler(HttpStatusCode status, string json, TimeSpan delay) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ListLogger(List<string> sink) : ILogger<EmotionsApiTextClassifier>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}
