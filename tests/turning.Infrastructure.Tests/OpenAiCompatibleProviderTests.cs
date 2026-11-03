using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Turning.Application.Features.AI;
using Turning.Application.Interfaces;
using Turning.Infrastructure.AI.Providers;
using Xunit;

namespace Turning.Infrastructure.Tests;

/// <summary>
/// Pruebas del proveedor HTTP contra un servidor simulado.
/// </summary>
/// <remarks>
/// Todo ocurre contra un <see cref="HttpMessageHandler"/> falso: estas pruebas no tocan la
/// red ni gastan tokens, y por eso pueden correr en cualquier máquina sin claves.
/// </remarks>
public class OpenAiCompatibleProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<string> CapturedBodies { get; } = [];
        public List<HttpRequestMessage> CapturedRequests { get; } = [];

        public StubHandler Enqueue(HttpStatusCode status, string body)
        {
            _responses.Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
            return this;
        }

        public StubHandler EnqueueThrow(Exception exception)
        {
            _responses.Enqueue(_ => throw exception);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            if (request.Content is not null)
                CapturedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            if (_responses.Count == 0)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

            return _responses.Dequeue()(request);
        }
    }

    private const string SuccessBody = """
        {"model":"gpt-test","choices":[{"message":{"role":"assistant","content":"Hola, te escucho."}}]}
        """;

    private static AiProviderOptions Options() => new()
    {
        Name = "openai",
        Enabled = true,
        BaseUrl = "https://api.test.local/v1/",
        Model = "gpt-test",
        ApiKey = "sk-test",
        RequestTimeoutMs = 2000
    };

    private static OpenAiCompatibleTextGenerationProvider CreateProvider(
        StubHandler handler, int maxRetries = 3, AiProviderOptions? options = null)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test.local/v1/") };
        return new OpenAiCompatibleTextGenerationProvider(
            client, options ?? Options(), "Eres un interlocutor.", maxRetries, NullLogger.Instance);
    }

    private static TextGenerationRequest Request() => new()
    {
        SessionId = Guid.NewGuid(),
        OriginatingTurnId = Guid.NewGuid(),
        UserInput = "Hola",
        ConversationHistory = [new ChatMessage(ChatMessage.RoleUser, "Mensaje previo", DateTime.UtcNow)]
    };

    [Fact]
    public async Task GenerateAsync_ShouldReturnTheGeneratedText()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, SuccessBody);

        var reply = await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        reply.Success.Should().BeTrue();
        reply.Text.Should().Be("Hola, te escucho.");
        reply.Model.Should().Be("gpt-test");
        reply.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task GenerateAsync_ShouldSendAWellFormedChatRequest()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, SuccessBody);

        await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        var body = handler.CapturedBodies.Single();
        body.Should().Contain("\"model\":\"gpt-test\"");
        body.Should().Contain("\"messages\"");
        body.Should().Contain("\"role\":\"system\"");
        body.Should().Contain("\"role\":\"user\"");

        var request = handler.CapturedRequests.Single();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("sk-test");
        request.RequestUri!.AbsoluteUri.Should().EndWith("/chat/completions");
    }

    [Fact]
    public async Task GenerateAsync_ShouldNotSendReasoning_ByDefault()
    {
        // OpenAI rechaza parámetros que no conoce: el campo solo debe salir si se pide.
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, SuccessBody);

        await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        handler.CapturedBodies.Single().Should().NotContain("reasoning");
    }

    [Fact]
    public async Task GenerateAsync_ShouldDisableReasoning_WhenConfigured()
    {
        // Los modelos de razonamiento gastan max_tokens pensando y devuelven content null.
        var options = Options();
        options.DisableReasoning = true;
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, SuccessBody);

        await CreateProvider(handler, options: options).GenerateAsync(Request(), CancellationToken.None);

        handler.CapturedBodies.Single().Should().Contain("\"reasoning\":{\"enabled\":false}");
    }

    [Fact]
    public async Task GenerateAsync_ShouldIncludeEmotionContextInTheSystemPrompt()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, SuccessBody);
        var request = Request() with { EmotionContext = new EmotionContext("sadness", 0.9, 0.8, "text_analysis") };

        await CreateProvider(handler).GenerateAsync(request, CancellationToken.None);

        // RF-EXP-01: el contexto emocional viaja con la petición.
        handler.CapturedBodies.Single().Should().Contain("sadness");
    }

    [Fact]
    public async Task GenerateAsync_ShouldNotRetry_WhenCredentialsAreInvalid()
    {
        var handler = new StubHandler()
            .Enqueue(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API key"}}""");

        var reply = await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        reply.Success.Should().BeFalse();
        reply.Failure.Should().Be(ProviderFailureKind.Credentials);
        reply.Attempts.Should().Be(1);
        reply.DisablesProvider.Should().BeTrue();
        handler.CapturedRequests.Should().HaveCount(1, "reintentar con una clave inválida no arregla nada");
    }

    [Fact]
    public async Task GenerateAsync_ShouldNotRetry_WhenQuotaIsExhausted()
    {
        var handler = new StubHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, """{"error":{"type":"insufficient_quota"}}""");

        var reply = await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        reply.Failure.Should().Be(ProviderFailureKind.Quota);
        reply.DisablesProvider.Should().BeTrue();
        handler.CapturedRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GenerateAsync_ShouldRetryTransientErrors_AndGiveUpAfterTheLimit()
    {
        var handler = new StubHandler()
            .Enqueue(HttpStatusCode.InternalServerError, "{}")
            .Enqueue(HttpStatusCode.BadGateway, "{}")
            .Enqueue(HttpStatusCode.ServiceUnavailable, "{}");

        var reply = await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        reply.Success.Should().BeFalse();
        reply.Failure.Should().Be(ProviderFailureKind.Transient);
        reply.Attempts.Should().Be(3);
        handler.CapturedRequests.Should().HaveCount(3);
    }

    [Fact]
    public async Task GenerateAsync_ShouldSucceedOnRetry_AfterATransientError()
    {
        var handler = new StubHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable, "{}")
            .Enqueue(HttpStatusCode.OK, SuccessBody);

        var reply = await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        reply.Success.Should().BeTrue();
        reply.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task GenerateAsync_ShouldFail_WhenTheProviderAnswersWithoutText()
    {
        var handler = new StubHandler().Enqueue(HttpStatusCode.OK, """{"model":"gpt-test","choices":[]}""");

        var reply = await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        // Un 200 sin texto no puede pasar por éxito: persistiría un turno vacío.
        reply.Success.Should().BeFalse();
        reply.Failure.Should().Be(ProviderFailureKind.Permanent);
    }

    [Fact]
    public async Task GenerateAsync_ShouldTreatNetworkFailuresAsTransient()
    {
        var handler = new StubHandler()
            .EnqueueThrow(new HttpRequestException("sin conexión"))
            .Enqueue(HttpStatusCode.OK, SuccessBody);

        var reply = await CreateProvider(handler).GenerateAsync(Request(), CancellationToken.None);

        reply.Success.Should().BeTrue();
        reply.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task IsConfigured_ShouldBeFalse_WithoutApiKey()
    {
        var options = Options();
        options.ApiKey = null;

        var provider = new OpenAiCompatibleTextGenerationProvider(
            new HttpClient(new StubHandler()), options, "prompt", 3, NullLogger.Instance);

        provider.IsConfigured.Should().BeFalse();
        await Task.CompletedTask;
    }
}
