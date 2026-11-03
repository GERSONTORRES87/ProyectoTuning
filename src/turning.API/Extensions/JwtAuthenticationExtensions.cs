using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Turning.Application.Features.Auth;
using Turning.Application.Interfaces;

namespace Turning.API.Extensions;

/// <summary>
/// Configura la autenticación JWT tomando el secreto de firma desde el entorno
/// (o user-secrets en desarrollo), nunca desde appsettings*.json.
/// </summary>
public static class JwtAuthenticationExtensions
{
    private const string HowToSetTheKey = """
        Configura el secreto fuera del repositorio, por ejemplo:

          # desarrollo (recomendado, queda fuera del arbol de trabajo)
          dotnet user-secrets --project src/turning.API set "Jwt:SigningKey" "<clave-de-64-chars>"

          # o por variable de entorno (PowerShell)
          $env:Jwt__SigningKey = "<clave-de-64-chars>"

        Para generar una clave nueva (PowerShell):
          [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
        """;

    /// <summary>
    /// Valida la sección Jwt, resuelve la clave de firma efectiva y registra el
    /// esquema Bearer. Fuera de Development, una configuración inválida impide el
    /// arranque en vez de degradarse a una clave conocida.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger? logger = null)
    {
        var options = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(options);

        var errors = options.Validate();
        if (errors.Count > 0)
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"Configuracion JWT invalida en el entorno '{environment.EnvironmentName}':{Environment.NewLine}" +
                    string.Join(Environment.NewLine, errors.Select(e => $"  - {e}")) +
                    Environment.NewLine + Environment.NewLine + HowToSetTheKey);
            }

            // En desarrollo no bloqueamos el arranque, pero tampoco caemos a una clave
            // conocida: se firma con una clave efimera distinta en cada arranque, de modo
            // que los tokens emitidos antes dejan de servir y el problema se nota.
            options.SigningKey = GenerateEphemeralKey();
            LogDevelopmentFallback(logger, errors);
        }

        services.PostConfigure<JwtOptions>(configured =>
        {
            configured.Issuer = options.Issuer;
            configured.Audience = options.Audience;
            configured.SigningKey = options.SigningKey;
            configured.ExpirationMinutes = options.ExpirationMinutes;
        });

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey!));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var claim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                        if (!Guid.TryParse(claim, out var userId) || userId == Guid.Empty)
                        {
                            context.Fail("El token no identifica un usuario válido.");
                            return;
                        }
                        var users = context.HttpContext.RequestServices.GetRequiredService<IUserAccountRepository>();
                        var user = await users.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                        if (user is null || context.Principal?.IsInRole(user.Role) != true)
                        {
                            context.Fail("El usuario o su rol cambió. Inicia sesión de nuevo.");
                            return;
                        }

                        // shortcut: un token sin huella (emitido antes de este cambio) se acepta hasta que expire;
                        // pasados Jwt:ExpirationMinutes desde el despliegue todos la llevan.
                        var stamp = context.Principal?.FindFirstValue(PasswordStamp.ClaimType);
                        if (stamp is not null && stamp != PasswordStamp.Of(user))
                            context.Fail("La contraseña cambió. Inicia sesión de nuevo.");
                    }
                };
            });

        return services;
    }

    private static string GenerateEphemeralKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static void LogDevelopmentFallback(ILogger? logger, IReadOnlyList<string> errors)
    {
        var detail = string.Join(" | ", errors);

        // El texto de ayuda va como argumento, no dentro de la plantilla: contiene
        // caracteres que el logger estructurado interpretaria como propiedades.
        const string Message =
            "*** Configuracion JWT invalida en Development: {Errores} *** Se firmara con una clave " +
            "EFIMERA generada en este arranque: los tokens emitidos dejaran de ser validos al " +
            "reiniciar la API. {ComoConfigurar}";

        if (logger is not null)
        {
            logger.LogWarning(Message, detail, HowToSetTheKey);
        }
        else
        {
            Console.Error.WriteLine(
                Message.Replace("{Errores}", detail).Replace("{ComoConfigurar}", Environment.NewLine + HowToSetTheKey));
        }
    }
}
