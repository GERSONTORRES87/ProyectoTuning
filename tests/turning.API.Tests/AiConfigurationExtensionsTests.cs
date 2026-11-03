using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Turning.API.Extensions;
using Turning.Application.Features.AI;
using Xunit;

namespace Turning.API.Tests;

/// <summary>
/// Pruebas del registro de la cadena de IA según la configuración recibida.
/// </summary>
public class AiConfigurationExtensionsTests
{
    private static ServiceProvider Build(string environmentName, Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiTextGeneration(configuration, environment);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void InvalidDevelopmentSection_ShouldLeaveTheRouterOptionsOnTheValidDefaults()
    {
        // Antes los proveedores se armaban con los valores por defecto pero IOptions seguia
        // atado a la seccion invalida: el router y su cadena veian configuraciones distintas.
        using var provider = Build("Development", new()
        {
            ["Ai:ProviderOrder:0"] = "rule-based",
            ["Ai:ProviderOrder:1"] = "openai",
            ["Ai:TotalBudgetMs"] = "1234"
        });

        var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;

        options.Validate().Should().BeEmpty();
        options.ProviderOrder.Should().Equal("openai", "openrouter", "rule-based");
        options.TotalBudgetMs.Should().Be(new AiOptions().TotalBudgetMs);
    }

    [Fact]
    public void InvalidSectionOutsideDevelopment_ShouldStopTheStartup()
    {
        var act = () => Build("Production", new()
        {
            ["Ai:ProviderOrder:0"] = "rule-based",
            ["Ai:ProviderOrder:1"] = "openai"
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*ProviderOrder*");
    }

    [Fact]
    public void ValidSection_ShouldReachTheRouterOptions()
    {
        using var provider = Build("Production", new() { ["Ai:TotalBudgetMs"] = "7000" });

        provider.GetRequiredService<IOptions<AiOptions>>().Value.TotalBudgetMs.Should().Be(7000);
    }

    [Fact]
    public void ConfiguredProviderOrder_ShouldReplaceTheDefaultsInsteadOfAppendingToThem()
    {
        using var provider = Build("Production", new()
        {
            ["Ai:ProviderOrder:0"] = "openrouter",
            ["Ai:ProviderOrder:1"] = "rule-based"
        });

        provider.GetRequiredService<IOptions<AiOptions>>().Value.ProviderOrder
            .Should().Equal("openrouter", "rule-based");
    }
}
