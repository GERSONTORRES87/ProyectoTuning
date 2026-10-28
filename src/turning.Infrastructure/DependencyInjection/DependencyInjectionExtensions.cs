using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Turning.Application.Interfaces;
using Turning.Infrastructure.AI;
using Turning.Infrastructure.Persistence;
using Turning.Infrastructure.Repositories;
using Turning.Infrastructure.Security;

namespace Turning.Infrastructure.DependencyInjection;

/// <summary>
/// Extensiones para registrar servicios de Infrastructure en el contenedor de inyección de dependencias.
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// Añade los servicios de la capa Infrastructure al contenedor de servicios.
    /// </summary>
    /// <param name="services">Colección de servicios.</param>
    /// <param name="configuration">Configuración de la aplicación para registrar persistencia y seguridad.</param>
    /// <returns>La colección de servicios para permitir encadenamiento.</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Falta la connection string 'DefaultConnection'. SQL Server es el único proveedor soportado.");
        services.AddDbContext<TurningDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        // Registra los repositorios
        // IMPORTANTE: En producción, reemplaza InMemorySampleRepository con una implementación real
        services.AddScoped<IConversationTurnRepository, ConversationTurnRepository>();
        services.AddScoped<ISampleRepository, InMemorySampleRepository>();
        services.AddScoped<IExperimentSessionRepository, ExperimentSessionRepository>();
        services.AddScoped<IAssignmentService, Services.BalancedAssignmentService>();
        // El adaptador por reglas ya no es el puerto: pasa a ser el ultimo eslabon de la
        // cadena que arma AddAiTextGeneration en la API, que es quien registra
        // ITextGenerationPort. Se registra la clase concreta para que ese eslabon la reciba.
        services.AddScoped<RuleBasedTextGenerationAdapter>();
        services.AddScoped<IEmotionAnalysisPort, AI.MockEmotionAnalysisAdapter>();
        services.AddScoped<IEmotionReadingRepository, EmotionReadingRepository>();
        services.AddScoped<IExperimentEventPublisher, Services.ExperimentEventPublisher>();

        // El lexico se lee del ensamblado una sola vez; el analizador no tiene estado.
        services.AddSingleton<Turning.Domain.Emotions.IEmotionLexicon, AI.Lexicon.EmbeddedJsonEmotionLexicon>();
        services.AddSingleton<Turning.Domain.Emotions.TextEmotionAnalyzer>();
        services.AddScoped<Turning.Application.Features.Emotions.IEmotionService, Services.EmotionService>();
        services.AddScoped<Turning.Application.Features.Emotions.IAvatarService, Services.AvatarService>();
        services.AddScoped<Turning.Application.Features.Surveys.ISurveyService, Services.SurveyAppService>();
        services.AddScoped<Turning.Application.Features.Surveys.IAdminSurveyResultsService, Services.AdminSurveyResultsService>();
        services.AddScoped<Turning.Application.Features.Events.IEventService, Services.EventService>();
        services.AddScoped<Turning.Application.Features.Events.IResultsService, Services.ResultsService>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.Configure<Turning.Application.Features.Auth.JwtOptions>(
            configuration.GetSection(Turning.Application.Features.Auth.JwtOptions.SectionName));
        services.Configure<Turning.Application.Features.ExperimentSessions.SessionOptions>(configuration.GetSection("Session"));
        services.Configure<Turning.Infrastructure.Services.SessionOptions>(configuration.GetSection("Session"));
        services.AddHostedService<Services.SessionSchedulerService>();

        // Aquí se registrarían otros servicios de infraestructura:
        // - DbContext
        // - Unit of Work
        // - External API clients
        // - Email services
        // - etc.

        return services;
    }
}
