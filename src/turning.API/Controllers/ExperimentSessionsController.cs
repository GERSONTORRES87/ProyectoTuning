using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.API.Middleware;
using Turning.Application.Features.ExperimentSessions;

namespace Turning.API.Controllers;

/// <summary>
/// Atajos de arranque para el cliente: crear la sesión inicial del usuario
/// autenticado y recuperar la más reciente.
///
/// Ambas operan siempre sobre el usuario del token, así que no reciben ningún
/// identificador de sesión ni de participante.
/// </summary>
[ApiController]
[Authorize]
[Route("api/experiment-sessions")]
[Produces("application/json")]
public sealed class ExperimentSessionsController : ControllerBase
{
    private readonly IExperimentSessionService _experimentSessionService;

    /// <summary>
    /// Constructor del controller de sesiones experimentales.
    /// </summary>
    public ExperimentSessionsController(IExperimentSessionService experimentSessionService)
    {
        _experimentSessionService = experimentSessionService;
    }

    /// <summary>
    /// Crea una sesión experimental inicial para el usuario autenticado.
    /// </summary>
    /// <remarks>
    /// Equivale a <c>POST /api/sessions</c>, con dos diferencias: responde 200 en vez
    /// de 201 y no devuelve cabecera Location. La sesión queda en <c>Created</c>, a
    /// la espera de activación.
    /// </remarks>
    /// <param name="request">Preferencia de condición experimental. Puede omitirse.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <response code="200">Sesión creada.</response>
    /// <response code="400">Petición inválida, por ejemplo una condición experimental desconocida.</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpPost("bootstrap")]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiExceptionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ExperimentSessionSnapshot>> Bootstrap([FromBody] CreateExperimentSessionRequest? request, CancellationToken cancellationToken)
    {
        if (!TryResolveAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        var snapshot = await _experimentSessionService.CreateBootstrapSessionAsync(userId, request, cancellationToken, IsPrivileged());
        return Ok(snapshot);
    }

    /// <summary>
    /// Devuelve la sesión más reciente del usuario autenticado.
    /// </summary>
    /// <remarks>
    /// Sirve para que el cliente retome donde quedó tras recargar. Solo mira las
    /// sesiones propias: nunca devuelve la de otro participante.
    /// </remarks>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <response code="200">Sesión más reciente del usuario.</response>
    /// <response code="404">El usuario no tiene ninguna sesión todavía.</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpGet("latest")]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ExperimentSessionSnapshot>> GetLatest(CancellationToken cancellationToken)
    {
        if (!TryResolveAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        var snapshot = await _experimentSessionService.GetLatestSessionAsync(userId, cancellationToken, IsPrivileged());
        return snapshot is null ? NotFound() : Ok(snapshot);
    }

    private bool TryResolveAuthenticatedUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(userIdClaim, out userId);
    }
    private bool IsPrivileged() => User.IsInRole("Researcher") || User.IsInRole("Administrator");
}
