using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.API.Filters;
using Turning.API.Middleware;
using Turning.Application.Features.ExperimentSessions;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;

namespace Turning.API.Controllers;

/// <summary>
/// Petición para unirse a una sesión como interlocutor humano.
/// </summary>
public sealed class JoinSessionRequest
{
    /// <summary>
    /// Código de la sesión, con el formato <c>EXP-XXXXXXXX</c>.
    /// </summary>
    public string SessionCode { get; init; } = string.Empty;
}

/// <summary>
/// Petición para asignar explícitamente un interlocutor.
/// </summary>
public sealed class AssignInterlocutorRequest
{
    /// <summary>
    /// Usuario que actuará como interlocutor.
    /// </summary>
    public Guid InterlocutorUserId { get; init; }
}

/// <summary>
/// Emparejamiento de un interlocutor humano con una sesión de condición Human.
/// </summary>
/// <remarks>
/// Es lo que hace real la condición Human: hasta ahora significaba solo "no generes una
/// respuesta automática", y el propio dueño escribía los dos lados de la conversación.
/// </remarks>
[ApiController]
[Route("api/sessions")]
[Authorize]
[Produces("application/json")]
public sealed class SessionPairingController : ControllerBase
{
    private readonly IExperimentSessionService _sessions;

    /// <summary>
    /// Constructor del controlador de emparejamiento.
    /// </summary>
    public SessionPairingController(IExperimentSessionService sessions) => _sessions = sessions;

    /// <summary>
    /// Se une a una sesión como interlocutor humano, usando su código.
    /// </summary>
    /// <remarks>
    /// La ruta <b>no</b> lleva <c>{sessionId}</c> a propósito. El filtro de aislamiento por
    /// propietario se aplica a toda ruta que lo lleve, y quien se está uniendo todavía no
    /// tiene acceso: recibiría un 404 antes de que este código llegara a ejecutarse.
    ///
    /// El código de sesión hace de invitación: lo comparte el participante o el
    /// experimentador.
    /// </remarks>
    /// <response code="200">Sesión a la que el usuario acaba de unirse.</response>
    /// <response code="400">Falta el código (<c>SESSION_INVALID_CODE</c>) o es la propia sesión del usuario (<c>SESSION_SELF_PAIRING</c>).</response>
    /// <response code="404">No existe ninguna sesión con ese código.</response>
    /// <response code="409">La sesión no es de condición Human, ya está terminada o ya tiene otro interlocutor (<c>SESSION_CONFLICT</c>).</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpPost("join")]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Join([FromBody] JoinSessionRequest request, CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(out var userId))
            return Unauthorized();

        try
        {
            var snapshot = await _sessions.JoinAsInterlocutorAsync(request.SessionCode, userId, cancellationToken);
            return Ok(snapshot);
        }
        catch (TurningApplicationException ex)
        {
            return StatusCode(MapStatusCode(ex.Code), new { error = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Asigna un interlocutor a una sesión. Solo para roles privilegiados.
    /// </summary>
    /// <response code="200">Sesión con su interlocutor asignado.</response>
    /// <response code="400">Falta el identificador del interlocutor.</response>
    /// <response code="404">La sesión no existe.</response>
    /// <response code="409">La sesión no admite interlocutor o ya tiene otro.</response>
    /// <response code="403">El usuario no tiene rol Researcher ni Administrator.</response>
    [HttpPost("{sessionId:guid}/interlocutor")]
    [Authorize(Roles = "Researcher,Administrator")]
    [AllowPrivilegedSessionWrite]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Assign(Guid sessionId, [FromBody] AssignInterlocutorRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _sessions.AssignInterlocutorAsync(sessionId, request.InterlocutorUserId, cancellationToken);
            return Ok(snapshot);
        }
        catch (TurningApplicationException ex)
        {
            return StatusCode(MapStatusCode(ex.Code), new { error = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Libera al interlocutor de una sesión, dejándola disponible para otro.
    /// </summary>
    /// <response code="200">Sesión sin interlocutor.</response>
    /// <response code="404">La sesión no existe.</response>
    /// <response code="403">El usuario no tiene rol Researcher ni Administrator.</response>
    [HttpDelete("{sessionId:guid}/interlocutor")]
    [Authorize(Roles = "Researcher,Administrator")]
    [AllowPrivilegedSessionWrite]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Release(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _sessions.ReleaseInterlocutorAsync(sessionId, cancellationToken);
            return Ok(snapshot);
        }
        catch (TurningApplicationException ex)
        {
            return StatusCode(MapStatusCode(ex.Code), new { error = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Lista las sesiones Human que todavía esperan un interlocutor.
    /// </summary>
    /// <remarks>
    /// Requiere rol privilegiado a propósito: una cola abierta a cualquier usuario
    /// autenticado sería una forma cómoda de enumerar sesiones ajenas.
    /// </remarks>
    /// <response code="200">Sesiones en espera, de la más antigua a la más reciente.</response>
    /// <response code="400">Paginación inválida (<c>SESSION_INVALID_PAGE</c>).</response>
    /// <response code="403">El usuario no tiene rol Researcher ni Administrator.</response>
    [HttpGet("awaiting-interlocutor")]
    [Authorize(Roles = "Researcher,Administrator")]
    [ProducesResponseType(typeof(PagedSessionsResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Awaiting([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _sessions.ListAwaitingInterlocutorAsync(page, pageSize, cancellationToken);
            return Ok(result);
        }
        catch (TurningApplicationException ex)
        {
            return StatusCode(MapStatusCode(ex.Code), new { error = ex.Code, message = ex.Message });
        }
    }

    private bool TryResolveUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(claim, out userId);
    }

    private static int MapStatusCode(string? code) => code switch
    {
        "SESSION_NOT_FOUND" => StatusCodes.Status404NotFound,
        "SESSION_CONFLICT" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}
