using FluentAssertions;
using Turning.Application.Features.AI;
using Xunit;

namespace Turning.Application.Tests;

/// <summary>
/// Pruebas de la configuración de la cadena de proveedores de IA.
/// </summary>
public class AiOptionsTests
{
    private static AiProviderOptions Provider(string? apiKey) => new()
    {
        Name = "openai",
        Enabled = true,
        BaseUrl = "https://api.openai.com/v1/",
        Model = "gpt-4o-mini",
        ApiKey = apiKey
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dummy")]
    [InlineData("DUMMY")]
    [InlineData("dummy-openai-key")]
    [InlineData("DUMMY-REEMPLAZAR-Ai__OpenAi__ApiKey")]
    [InlineData("changeme")]
    [InlineData("your-api-key-here")]
    [InlineData("sk-xxx")]
    [InlineData("TODO")]
    public void IsPlaceholderApiKey_ShouldRecognizeValuesThatAreNotRealKeys(string? apiKey)
    {
        // Sin esto, un marcador de posición haría salir al proveedor a la red para recibir
        // un 401: una llamada inútil y su latencia en el primer turno de cada sesión.
        AiProviderOptions.IsPlaceholderApiKey(apiKey).Should().BeTrue();
    }

    [Theory]
    [InlineData("sk-proj-abc123def456")]
    [InlineData("sk-or-v1-9f8e7d6c5b4a")]
    public void IsPlaceholderApiKey_ShouldAcceptRealLookingKeys(string apiKey)
    {
        AiProviderOptions.IsPlaceholderApiKey(apiKey).Should().BeFalse();
    }

    [Fact]
    public void IsUsable_ShouldBeFalse_WithAPlaceholderKey()
    {
        Provider("DUMMY-REEMPLAZAR-Ai__OpenAi__ApiKey").IsUsable.Should().BeFalse();
    }

    [Fact]
    public void IsUsable_ShouldBeTrue_WithARealKey()
    {
        Provider("sk-proj-abc123def456").IsUsable.Should().BeTrue();
    }

    [Fact]
    public void IsUsable_ShouldBeFalse_WhenTheProviderIsDisabled()
    {
        var provider = Provider("sk-proj-abc123def456");
        provider.Enabled = false;

        provider.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldAcceptTheDefaultConfiguration()
    {
        // Los marcadores de posición no son un error de configuración: la cadena está
        // pensada para funcionar degradada mientras no haya claves.
        new AiOptions().Validate().Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldRequireTheRuleBasedProviderInTheChain()
    {
        var options = new AiOptions { ProviderOrder = ["openai", "openrouter"] };

        // Sin el eslabón terminal, una caída de los externos deja la conversación sin respuesta.
        options.Validate().Should().ContainSingle(e => e.Contains("rule-based"));
    }

    [Theory]
    [InlineData("rule-based", "openai")]
    [InlineData("openai", "rule-based", "openrouter")]
    public void Validate_ShouldRequireTheRuleBasedProviderToBeLast(params string[] order)
    {
        // El router sigue este orden y rule-based nunca falla: si no va ultimo, los externos
        // configurados despues no se llamarian jamas, sin ningun error visible.
        var options = new AiOptions { ProviderOrder = order };

        options.Validate().Should().ContainSingle(e => e.Contains("debe terminar en"));
    }

    [Theory]
    [InlineData("rule-based", "openai", "rule-based")]
    [InlineData("openai", "RULE-BASED", "rule-based")]
    public void Validate_ShouldRejectADuplicatedRuleBasedProvider(params string[] order)
    {
        var options = new AiOptions { ProviderOrder = order };

        options.Validate().Should().ContainSingle(e => e.Contains("solo puede incluir"));
    }

    [Theory]
    [InlineData("rule-based")]
    [InlineData("openai", "rule-based")]
    [InlineData("OPENROUTER", "Rule-Based")]
    public void Validate_ShouldAcceptAChainThatEndsInTheRuleBasedProvider(params string[] order)
    {
        new AiOptions { ProviderOrder = order }.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldRejectATimeoutLargerThanTheTotalBudget()
    {
        var options = new AiOptions { TotalBudgetMs = 3000 };
        options.OpenAi.RequestTimeoutMs = 9000;

        options.Validate().Should().Contain(e => e.Contains("presupuesto total"));
    }

    [Fact]
    public void Validate_ShouldRejectAnInvalidBaseUrl()
    {
        var options = new AiOptions();
        options.OpenRouter.BaseUrl = "no-es-una-url";

        options.Validate().Should().Contain(e => e.Contains("BaseUrl"));
    }
}
