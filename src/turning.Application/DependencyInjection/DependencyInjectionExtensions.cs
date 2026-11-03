namespace Turning.Application.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Turning.Application.Features.Auth;
using Turning.Application.Features.ConversationTurns;
using Turning.Application.Features.ExperimentSessions;

/// <summary>
/// Extensiones para registrar servicios de Application en el contenedor de inyección de dependencias.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Añade los servicios de la capa Application al contenedor de servicios.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <returns>La colección de servicios para permitir encadenamiento.</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Aquí se registrarán validadores, handlers y mensajería de Wolverine
        // cuando esa integración se incorpore al stack.
        // services.AddValidatorsFromAssembly(typeof(DependencyInjectionExtensions).Assembly);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddSingleton<PasswordResetCooldown>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<Features.Emotions.ITextEmotionAnalysisService, Features.Emotions.TextEmotionAnalysisService>();
        services.AddScoped<IConversationOrchestrator, ConversationOrchestrator>();
        services.AddScoped<IConversationTurnService, ConversationTurnService>();
        services.AddScoped<IExperimentSessionService, ExperimentSessionService>();

        return services;
    }
}
