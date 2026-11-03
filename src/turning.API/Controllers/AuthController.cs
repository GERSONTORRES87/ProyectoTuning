using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.Application.Features.Auth;

namespace Turning.API.Controllers;

/// <summary>
/// Endpoints de autenticación y sesión de usuario.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    /// <summary>
    /// Constructor del controller de autenticación.
    /// </summary>
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Registra un nuevo usuario persistido en base de datos.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResult>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Inicia sesión con credenciales persistidas.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResult>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Devuelve el usuario autenticado actual.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthenticatedUser), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthenticatedUser>> Me(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _authService.GetCurrentUserAsync(userId, cancellationToken);
        return Ok(user);
    }

    /// <summary>
    /// Envía un enlace de restablecimiento si el correo pertenece a una cuenta.
    /// </summary>
    /// <response code="202">Siempre la misma respuesta, exista o no la cuenta.</response>
    /// <response code="400">Correo vacío o mal formado (<c>AUTH_INVALID_REQUEST</c>).</response>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, [FromServices] IPasswordResetService resets, CancellationToken cancellationToken)
    {
        await resets.RequestAsync(request.Email, cancellationToken);
        return Accepted(new { });
    }

    /// <summary>
    /// Reemplaza la contraseña con el token del enlace. No inicia sesión.
    /// </summary>
    /// <response code="204">Contraseña reemplazada.</response>
    /// <response code="400">Token no válido (<c>AUTH_RESET_TOKEN_INVALID</c>) o contraseña corta (<c>AUTH_INVALID_REQUEST</c>).</response>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, [FromServices] IPasswordResetService resets, CancellationToken cancellationToken)
    {
        await resets.ResetAsync(request.Token, request.NewPassword, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Lista las cuentas con su rol para administración, con búsqueda por nombre o correo.
    /// </summary>
    /// <response code="200">Página de cuentas, sin contraseñas ni hashes.</response>
    /// <response code="400">Paginación inválida (<c>AUTH_INVALID_PAGE</c>).</response>
    /// <response code="403">El usuario no es Administrator.</response>
    [Authorize(Roles = "Administrator")]
    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedUsersResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedUsersResult>> ListUsers(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(claim, out var actorId) || actorId == Guid.Empty) return Unauthorized();
        return Ok(await _authService.ListUsersAsync(actorId, q, page, pageSize, ct));
    }

    [Authorize(Roles = "Administrator")]
    [HttpPut("users/{userId:guid}/role")]
    public async Task<ActionResult<AuthenticatedUser>> AssignRole(Guid userId, [FromBody] AssignUserRoleRequest request, CancellationToken ct)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(claim, out var actorId) || actorId == Guid.Empty) return Unauthorized();
        return Ok(await _authService.AssignRoleAsync(actorId, userId, request.Role, ct));
    }
}

public sealed class AssignUserRoleRequest
{
    public string Role { get; init; } = string.Empty;
}

/// <summary>
/// Petición de enlace de restablecimiento.
/// </summary>
public sealed class ForgotPasswordRequest
{
    /// <summary>Correo de la cuenta.</summary>
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// Petición para reemplazar la contraseña con el token del enlace.
/// </summary>
public sealed class ResetPasswordRequest
{
    /// <summary>Token recibido en el enlace del correo.</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>Contraseña nueva, de al menos 8 caracteres.</summary>
    public string NewPassword { get; init; } = string.Empty;
}
