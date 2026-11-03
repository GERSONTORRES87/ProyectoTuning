using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.API.Middleware;
using Turning.Application.Features.ConversationTurns;

using Turning.API.Filters;

namespace Turning.API.Controllers;

/// <summary>
/// Conversación de una sesión experimental: listar y registrar mensajes.
///
/// Igual que el resto de rutas con <c>{sessionId}</c>, aplica aislamiento por
/// propietario: sobre una sesión ajena responde <c>404</c>, el mismo que si no
/// existiera.
/// </summary>
[ApiController]
[Authorize]
[Route("api/experiment-sessions/{sessionId:guid}/conversation-turns")]
[AllowSessionInterlocutor]
[AllowPrivilegedSessionWrite] // el servicio decide la participación real; un observador sin asignar recibe 404
[Produces("application/json")]
public sealed class ConversationTurnsController : ControllerBase
{
    private readonly IConversationTurnService _conversationTurnService;

    /// <summary>
    /// Constructor del controller de conversación.
    /// </summary>
    public ConversationTurnsController(IConversationTurnService conversationTurnService)
    {
        _conversationTurnService = conversationTurnService;
    }

    /// <summary>
    /// Lista la conversación persistida de una sesión propia, en orden de secuencia.
    /// </summary>
    /// <param name="sessionId">Sesión cuya conversación se consulta.</param>
    /// <param name="afterSequence">
    /// Devuelve solo los turnos con secuencia mayor que esta. Pensado para sondear la
    /// conversación sin volver a descargarla entera: el cliente pasa la última secuencia que
    /// ya tiene. Omitirlo devuelve la conversación completa, como siempre.
    /// </param>
    /// <param name="limit">Máximo de turnos por respuesta. Se limita a 200.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <response code="200">Turnos de la conversación, del más antiguo al más reciente.</response>
    /// <response code="404">La sesión no existe, o existe y es ajena: <c>SESSION_NOT_FOUND</c> en ambos casos.</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ConversationTurnSnapshot>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ConversationTurnSnapshot>>> List(
        Guid sessionId,
        [FromQuery] int? afterSequence,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        var turns = await _conversationTurnService.ListAsync(userId, sessionId, afterSequence, limit, cancellationToken, IsPrivileged());
        return Ok(turns);
    }

    /// <summary>
    /// Registra un nuevo mensaje en una sesión propia.
    /// </summary>
    /// <remarks>
    /// El emisor debe ser <c>Participant</c> o <c>Interlocutor</c> y el mensaje no
    /// puede ir vacío ni superar los 4000 caracteres. Una sesión ya terminada no
    /// admite turnos nuevos (<c>SESSION_TERMINAL</c>).
    /// </remarks>
    /// <param name="sessionId">Sesión donde se registra el mensaje.</param>
    /// <param name="request">Emisor y contenido del mensaje.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <response code="200">
    /// Turno registrado. En condicion AI la respuesta incluye ademas
    /// <c>interlocutorTurn</c> con el mensaje generado y <c>ai</c> con proveedor y latencia;
    /// en condicion Human <c>source</c> vale <c>none</c> porque la respuesta llega despues,
    /// escrita por una persona. <c>degraded</c> indica que la generacion no vino del
    /// proveedor previsto.
    /// </response>
    /// <response code="400">
    /// Mensaje vacío (<c>CONVERSATION_EMPTY_MESSAGE</c>), de más de 4000 caracteres
    /// (<c>CONVERSATION_MESSAGE_TOO_LONG</c>), emisor no válido
    /// (<c>CONVERSATION_INVALID_SENDER</c>) o sesión terminal (<c>SESSION_TERMINAL</c>).
    /// </response>
    /// <response code="404">La sesión no existe, o existe y es ajena.</response>
    /// <response code="401">Falta el token o no trae un identificador de usuario válido.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ConversationTurnResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiExceptionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ConversationTurnResult>> Add(Guid sessionId, [FromBody] AddConversationTurnRequest request, CancellationToken cancellationToken)
    {
        if (!TryResolveAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        var turn = await _conversationTurnService.AddAsync(userId, sessionId, request, cancellationToken, IsPrivileged());
        return Ok(turn);
    }

    private bool TryResolveAuthenticatedUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return Guid.TryParse(userIdClaim, out userId);
    }

    private bool IsPrivileged() => User.IsInRole("Researcher") || User.IsInRole("Administrator");
}
