using System.Net;
using FluentAssertions;
using Turning.Infrastructure.AI.Providers;
using Xunit;

namespace Turning.Infrastructure.Tests;

/// <summary>
/// Pruebas de la clasificación de errores de proveedor.
/// </summary>
public class OpenAiErrorClassifierTests
{
    [Fact]
    public void Classify_ShouldTreatUnauthorizedAsCredentials()
    {
        OpenAiErrorClassifier.Classify(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API key"}}""")
            .Should().Be(ProviderFailureKind.Credentials);
    }

    [Fact]
    public void Classify_ShouldTreatPaymentRequiredAsQuota()
    {
        // OpenRouter usa 402 cuando se acaban los créditos.
        OpenAiErrorClassifier.Classify(HttpStatusCode.PaymentRequired, """{"error":{"message":"Insufficient credits"}}""")
            .Should().Be(ProviderFailureKind.Quota);
    }

    [Fact]
    public void Classify_ShouldDistinguishQuotaFromRateLimit_OnTheSame429()
    {
        // Este es el caso que justifica todo el clasificador: el mismo código HTTP significa
        // dos cosas distintas, y confundirlas hace que una cuenta sin saldo se coma el
        // presupuesto reintentando en vez de saltar al proveedor gratuito.
        var quota = OpenAiErrorClassifier.Classify(
            HttpStatusCode.TooManyRequests,
            """{"error":{"message":"You exceeded your current quota","type":"insufficient_quota"}}""");

        var rateLimit = OpenAiErrorClassifier.Classify(
            HttpStatusCode.TooManyRequests,
            """{"error":{"message":"Rate limit reached for requests","type":"requests"}}""");

        quota.Should().Be(ProviderFailureKind.Quota);
        rateLimit.Should().Be(ProviderFailureKind.RateLimited);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void Classify_ShouldTreatServerErrorsAsTransient(HttpStatusCode status)
    {
        var kind = OpenAiErrorClassifier.Classify(status, null);

        kind.Should().Be(ProviderFailureKind.Transient);
        OpenAiErrorClassifier.IsRetryable(kind).Should().BeTrue();
    }

    [Fact]
    public void Classify_ShouldTreatContextLengthAsPermanent()
    {
        OpenAiErrorClassifier.Classify(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"This model's maximum context length is 8192 tokens","code":"context_length_exceeded"}}""")
            .Should().Be(ProviderFailureKind.Permanent);
    }

    [Fact]
    public void Classify_ShouldTreatOpenRouterUpstream429AsRateLimited()
    {
        // Cuerpo real de OpenRouter: a diferencia de OpenAI, "code" es numérico. Leerlo
        // como texto lanzaba una excepción y el 429 terminaba clasificado como permanente.
        const string body = """
            {"error":{"message":"Provider returned error","code":429,"metadata":{"raw":"google/gemma-4-31b-it:free is temporarily rate-limited upstream.","provider_name":"Google AI Studio"}}}
            """;

        OpenAiErrorClassifier.Classify(HttpStatusCode.TooManyRequests, body)
            .Should().Be(ProviderFailureKind.RateLimited);
    }

    [Fact]
    public void Classify_ShouldNotCrashOnNonJsonBody()
    {
        OpenAiErrorClassifier.Classify(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>")
            .Should().Be(ProviderFailureKind.Transient);
    }

    [Theory]
    [InlineData(ProviderFailureKind.Credentials)]
    [InlineData(ProviderFailureKind.Quota)]
    [InlineData(ProviderFailureKind.Permanent)]
    [InlineData(ProviderFailureKind.RateLimited)]
    public void IsRetryable_ShouldBeFalse_ForFailuresThatRetryingCannotFix(ProviderFailureKind kind)
    {
        OpenAiErrorClassifier.IsRetryable(kind).Should().BeFalse();
    }
}
