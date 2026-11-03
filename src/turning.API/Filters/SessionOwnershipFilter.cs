using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Turning.Application.Features.ExperimentSessions;

namespace Turning.API.Filters;

/// <summary>
/// Aplica aislamiento por propietario a toda ruta que lleve {sessionId} en la
/// plantilla, sin que cada controller tenga que acordarse de validarlo.
///
/// Se registra globalmente a propósito. Las rutas anidadas
/// (/api/sessions/{sessionId}/emotions, /events, /avatar, /survey, /results)
/// nacieron sin comprobar el dueño, y hacerlo opt-in por atributo repetiría el
/// problema con el próximo controller que alguien añada: lo seguro tiene que ser
/// lo que pasa por omisión. Un controller que necesite quedar fuera lo declara
/// con <see cref="SkipSessionOwnershipAttribute"/>.
///
/// Devuelve 404 —no 403— cuando la sesión es ajena, igual que
/// <c>ExperimentSessionService</c>: quien pregunta no debe poder distinguir una
/// sesión ajena de una inexistente enumerando GUIDs.
///
/// Desde que una sesión puede tener dos personas, el acceso no es un sí o un no: el filtro
/// resuelve un <see cref="SessionAccessLevel"/> y exige <c>Owner</c> salvo que la ruta
/// declare <see cref="AllowSessionInterlocutorAttribute"/>. Bajar el listón a "dueño o
/// interlocutor" en todas las rutas le habría dado al interlocutor permiso para cancelar la
/// sesión y leer los resultados del participante.
/// </summary>
public sealed class SessionOwnershipFilter : IAsyncAuthorizationFilter
{
    /// <summary>
    /// Nombre del parámetro de ruta que dispara la comprobación.
    /// </summary>
    public const string RouteParameterName = "sessionId";

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Result is not null)
            return;

        if (context.ActionDescriptor.EndpointMetadata.OfType<SkipSessionOwnershipAttribute>().Any())
            return;

        // Rutas sin {sessionId} (p. ej. GET /api/results) no son asunto de este filtro.
        if (!context.RouteData.Values.TryGetValue(RouteParameterName, out var valorEnRuta)
            || !Guid.TryParse(valorEnRuta?.ToString(), out var sessionId))
        {
            return;
        }

        var user = context.HttpContext.User;

        // Sin identidad no hay nada que comparar. [Authorize] normalmente ya habrá
        // respondido 401; esto cubre el caso de un endpoint anónimo con {sessionId}.
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var requestingUserId = GetUserId(user);
        if (requestingUserId == Guid.Empty)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var isPrivileged = user.IsInRole("Researcher") || user.IsInRole("Administrator");

        var sessionService = context.HttpContext.RequestServices.GetRequiredService<IExperimentSessionService>();
        var level = await sessionService.GetAccessLevelAsync(
            sessionId,
            requestingUserId,
            isPrivileged,
            context.HttpContext.RequestAborted);

        // Lo seguro sigue siendo lo que pasa por omision: sin el atributo, la ruta exige ser
        // el dueno. El interlocutor solo entra donde alguien lo decidio explicitamente.
        var minimumLevel = context.ActionDescriptor.EndpointMetadata.OfType<AllowSessionInterlocutorAttribute>().Any()
            ? SessionAccessLevel.Interlocutor
            : SessionAccessLevel.Owner;

        // Privileged es el nivel mas alto y por eso supera el minimo Owner de toda ruta, pero
        // eso solo vale para observar: un Researcher/Administrator ajeno no escribe en el
        // experimento de otro participante salvo que la ruta lo declare y su servicio
        // compruebe la participacion real.
        var observesOnly = level == SessionAccessLevel.Privileged
            && !HttpMethods.IsGet(context.HttpContext.Request.Method)
            && !HttpMethods.IsHead(context.HttpContext.Request.Method)
            && !context.ActionDescriptor.EndpointMetadata.OfType<AllowPrivilegedSessionWriteAttribute>().Any();

        if (level < minimumLevel || observesOnly)
        {
            context.Result = new NotFoundObjectResult(new { error = "SESSION_NOT_FOUND" });
            return;
        }

        // El controller necesita saber con que papel entra quien llama, sobre todo para
        // decidir el emisor de un turno; volver a consultarlo seria una consulta de mas.
        context.HttpContext.Items[SessionAccessLevelKey] = level;
    }

    /// <summary>
    /// Clave con la que el nivel de acceso resuelto queda disponible para el controller.
    /// </summary>
    public const string SessionAccessLevelKey = "SessionAccessLevel";

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(claim, out var userId) ? userId : Guid.Empty;
    }
}

/// <summary>
/// Exime a un controller o acción del <see cref="SessionOwnershipFilter"/>.
/// Usar solo con una razón explícita: quita el aislamiento por propietario de esa ruta.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipSessionOwnershipAttribute : Attribute
{
}
