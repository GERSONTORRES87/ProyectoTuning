using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Turning.Application.Features.Events;
using Turning.Application.Features.ExperimentSessions;

namespace Turning.API.Controllers;

/// <summary>
/// Resultados del experimento: el detalle completo de una sesión y el listado
/// para análisis.
///
/// Ambas respuestas usan contratos públicos, nunca entidades de dominio: no
/// exponen <c>ownerUserId</c>, <c>rowVersion</c> ni <c>isDeleted</c>, y los enums
/// salen como texto.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
public sealed class ResultsController : ControllerBase
{
    private readonly IResultsService _svc;

    /// <summary>
    /// Constructor del controller de resultados.
    /// </summary>
    public ResultsController(IResultsService svc) => _svc = svc;

    /// <summary>
    /// Resultado completo de una sesión propia: estado, conversación, lecturas
    /// emocionales, expresiones de avatar, encuesta y eventos degradados.
    /// </summary>
    /// <remarks>
    /// Aplica aislamiento por propietario: sobre una sesión ajena responde 404, el
    /// mismo que si no existiera. Un Researcher o Administrator sí puede consultarla.
    /// </remarks>
    /// <param name="sessionId">Sesión cuyo resultado se consulta.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Resultado completo de la sesión.</response>
    /// <response code="404">La sesión no existe, o existe y es ajena.</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpGet("sessions/{sessionId:guid}/results")]
    [ProducesResponseType(typeof(SessionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetResult(Guid sessionId, CancellationToken ct)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid.TryParse(claim, out var userId);
        var privileged = User.IsInRole("Researcher") || User.IsInRole("Administrator");
        try { var r = await _svc.GetResultAsync(sessionId, ct, userId, privileged); return Ok(r); }
        catch (InvalidOperationException ex) when (ex.Message == "SESSION_NOT_FOUND") { return NotFound(new { error = ex.Message }); }
    }

    /// <summary>
    /// Listado paginado de sesiones para análisis. Reservado a Researcher y
    /// Administrator.
    /// </summary>
    /// <remarks>
    /// A diferencia del resto de rutas de resultados, esta no se limita a las
    /// sesiones propias: es la consulta transversal que usa el equipo de
    /// investigación, y por eso exige rol privilegiado.
    /// </remarks>
    /// <param name="from">Fecha mínima de creación, inclusive.</param>
    /// <param name="to">Fecha máxima de creación, inclusive.</param>
    /// <param name="condition">Condición experimental a filtrar: <c>Human</c> o <c>AI</c>.</param>
    /// <param name="page">Página solicitada, empezando en 1.</param>
    /// <param name="pageSize">Elementos por página.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Página de sesiones que cumplen el filtro.</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    /// <response code="403">El usuario autenticado no tiene rol Researcher ni Administrator.</response>
    [HttpGet("results")]
    [Authorize(Roles="Researcher,Administrator")]
    [ProducesResponseType(typeof(PagedSessionsResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? condition, [FromQuery] int page=1, [FromQuery] int pageSize=50, CancellationToken ct=default)
    {
        try { return Ok(await _svc.ListAsync(from,to,condition,page,pageSize,ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
