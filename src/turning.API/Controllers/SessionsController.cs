using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.Application.Features.ExperimentSessions;
using AppEx = Turning.Application.Exceptions.ApplicationException;

namespace Turning.API.Controllers;

/// <summary>
/// Ciclo de vida de las sesiones experimentales: creación, activación,
/// finalización, cancelación y consulta.
///
/// El propietario activa y completa; el interlocutor asignado puede consultar.
/// Researcher y Administrator pueden observar, y Administrator puede cancelar.
/// Los terceros sin acceso reciben <c>404 SESSION_NOT_FOUND</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sessions")]
[Produces("application/json")]
public sealed class SessionsController : ControllerBase
{
    private readonly IExperimentSessionService _svc;

    /// <summary>
    /// Constructor del controller de sesiones.
    /// </summary>
    public SessionsController(IExperimentSessionService svc) => _svc = svc;

    /// <summary>
    /// Crea una sesión para el usuario autenticado, en estado <c>Created</c>.
    /// </summary>
    /// <remarks>
    /// La condición experimental (Human / AI) se asigna de forma balanceada si no se
    /// indica preferencia en el cuerpo. La sesión todavía no corre: hay que activarla
    /// para que arranque el temporizador.
    /// </remarks>
    /// <response code="201">Sesión creada. La cabecera Location apunta a su consulta.</response>
    /// <response code="400">Petición inválida, por ejemplo una condición experimental desconocida.</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateExperimentSessionRequest? req, CancellationToken ct)
    {
        var uid = GetUserId(); if (uid == Guid.Empty) return Unauthorized();
        try { var r = await _svc.CreateBootstrapSessionAsync(uid, req, ct, IsPrivileged()); return CreatedAtAction(nameof(GetById), new { id = r.Id }, r); }
        catch (AppEx ex) { return StatusCode(Map(ex.Code ?? "SESSION_ERROR"), new { error = ex.Code, message = ex.Message }); }
    }

    /// <summary>
    /// Activa una sesión propia: pasa de <c>Created</c> a <c>Active</c> e inicia el
    /// temporizador de duración.
    /// </summary>
    /// <remarks>
    /// Desde ese momento <c>expiresAtUtc</c> indica cuándo se completará por
    /// temporizador. Solo el dueño puede activarla.
    /// </remarks>
    /// <response code="200">Sesión activada.</response>
    /// <response code="404">La sesión no existe, o existe y es ajena: <c>SESSION_NOT_FOUND</c> en ambos casos.</response>
    /// <response code="409">La sesión no está en un estado que admita activación (<c>SESSION_CONFLICT</c>).</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var uid = GetUserId(); if (uid == Guid.Empty) return Unauthorized();
        try { var r = await _svc.ActivateAsync(id, uid, IsPrivileged(), ct); return Ok(r); }
        catch (AppEx ex) { return StatusCode(Map(ex.Code ?? "SESSION_ERROR"), new { error = ex.Code, message = ex.Message }); }
    }

    /// <summary>
    /// Completa una sesión propia antes de que venza su temporizador.
    /// </summary>
    /// <response code="200">Sesión completada.</response>
    /// <response code="404">La sesión no existe, o existe y es ajena: <c>SESSION_NOT_FOUND</c> en ambos casos.</response>
    /// <response code="409">La sesión no está en un estado que admita completarse (<c>SESSION_CONFLICT</c>).</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        var uid = GetUserId(); if (uid == Guid.Empty) return Unauthorized();
        try { var r = await _svc.CompleteAsync(id, uid, IsPrivileged(), ct); return Ok(r); }
        catch (AppEx ex) { return StatusCode(Map(ex.Code ?? "SESSION_ERROR"), new { error = ex.Code, message = ex.Message }); }
    }

    /// <summary>
    /// Cancela una sesión indicando el motivo. Reservado a administradores.
    /// </summary>
    /// <remarks>
    /// El motivo es obligatorio: queda registrado para auditoría. A diferencia del
    /// resto de rutas, esta no aplica el 404 por propietario, porque un administrador
    /// puede cancelar la sesión de cualquier participante.
    /// </remarks>
    /// <response code="200">Sesión cancelada.</response>
    /// <response code="400">Falta el motivo (<c>SESSION_INVALID_REASON</c>).</response>
    /// <response code="404">La sesión no existe (<c>SESSION_NOT_FOUND</c>).</response>
    /// <response code="409">La sesión ya está completada y no admite cancelación (<c>SESSION_CONFLICT</c>).</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    /// <response code="403">El usuario autenticado no tiene rol Administrator.</response>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Administrator")]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelRequest req, CancellationToken ct)
    {
        var uid = GetUserId();
        try { var r = await _svc.CancelAsync(id, req.Reason, uid, ct); return Ok(r); }
        catch (AppEx ex) { return StatusCode(Map(ex.Code ?? "SESSION_ERROR"), new { error = ex.Code, message = ex.Message }); }
    }

    /// <summary>
    /// Consulta el estado de una sesión propia.
    /// </summary>
    /// <response code="200">Estado actual de la sesión.</response>
    /// <response code="404">La sesión no existe, o existe y es ajena: <c>SESSION_NOT_FOUND</c> en ambos casos.</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpGet("{id:guid}")]
    [Turning.API.Filters.AllowSessionInterlocutor]
    [ProducesResponseType(typeof(ExperimentSessionSnapshot), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var uid = GetUserId(); if (uid == Guid.Empty) return Unauthorized();
        try { var r = await _svc.GetByIdAsync(id, uid, IsPrivileged(), ct); return Ok(r); }
        catch (AppEx ex) { return NotFound(new { error = ex.Code }); }
    }

    /// <summary>
    /// Lista paginada de las sesiones de un participante.
    /// </summary>
    /// <remarks>
    /// Un participante solo puede listar las suyas; consultar las de otro exige rol
    /// Researcher o Administrator. Aquí la respuesta es 403 y no 404 porque el
    /// identificador lo aporta quien llama, así que negarlo no revela nada nuevo.
    /// </remarks>
    /// <param name="participantId">Participante dueño de las sesiones.</param>
    /// <param name="page">Página solicitada, empezando en 1.</param>
    /// <param name="pageSize">Elementos por página, entre 1 y 50.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Página de sesiones del participante.</response>
    /// <response code="400">Paginación inválida (<c>SESSION_INVALID_PAGE</c>).</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    /// <response code="403">Se piden sesiones de otro participante sin rol privilegiado (<c>SESSION_FORBIDDEN</c>).</response>
    [HttpGet("participant/{participantId:guid}")]
    [ProducesResponseType(typeof(ParticipantSessionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListByParticipant(Guid participantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var uid = GetUserId(); if (uid == Guid.Empty) return Unauthorized();
        try { var r = await _svc.ListByParticipantAsync(participantId, uid, IsPrivileged(), page, pageSize, ct); return Ok(new { participantId, sessions = r.Items, r.Total, r.Page, r.PageSize }); }
        catch (AppEx ex) when (ex.Code == "SESSION_FORBIDDEN") { return Forbid(); }
        catch (AppEx ex) { return BadRequest(new { error = ex.Code }); }
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(claim, out var g) ? g : Guid.Empty;
    }
    private bool IsPrivileged() => User.IsInRole("Researcher") || User.IsInRole("Administrator");
    private static int Map(string code) => code switch { "SESSION_NOT_FOUND" => 404, "SESSION_CONFLICT" => 409, "SESSION_INVALID_REASON" or "SESSION_INVALID_PAGE" => 400, _ => 400 };
}

/// <summary>
/// Cuerpo para cancelar una sesión.
/// </summary>
public sealed class CancelRequest
{
    /// <summary>
    /// Motivo de la cancelación. Obligatorio; queda registrado para auditoría.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Forma de los errores de negocio de la API: un código estable para el cliente y
/// un mensaje legible.
/// </summary>
public sealed class ApiErrorResponse
{
    /// <summary>
    /// Código estable del error, por ejemplo <c>SESSION_NOT_FOUND</c>. Es lo que debe
    /// interpretar el cliente; el mensaje puede cambiar de redacción.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Descripción legible del error.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Página de sesiones pertenecientes a un participante.
/// </summary>
public sealed class ParticipantSessionsResponse
{
    /// <summary>
    /// Participante dueño de las sesiones listadas.
    /// </summary>
    public Guid ParticipantId { get; init; }

    /// <summary>
    /// Sesiones de la página solicitada.
    /// </summary>
    public required IReadOnlyList<ExperimentSessionSnapshot> Sessions { get; init; }

    /// <summary>
    /// Total de sesiones del participante, más allá de esta página.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    /// Página devuelta, empezando en 1.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Tamaño de página aplicado.
    /// </summary>
    public int PageSize { get; init; }
}
