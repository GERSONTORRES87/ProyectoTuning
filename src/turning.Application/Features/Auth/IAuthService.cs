namespace Turning.Application.Features.Auth;

/// <summary>
/// Caso de uso de autenticación de usuarios.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registra un usuario persistido y devuelve su sesión autenticada.
    /// </summary>
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inicia sesión con credenciales persistidas.
    /// </summary>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el usuario autenticado actual.
    /// </summary>
    Task<AuthenticatedUser> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AuthenticatedUser> AssignRoleAsync(Guid actorId, Guid userId, string role, CancellationToken ct = default);

    /// <summary>
    /// Lista las cuentas para administración. Solo para Administrator.
    /// </summary>
    Task<PagedUsersResult> ListUsersAsync(Guid actorId, string? query, int page, int pageSize, CancellationToken ct = default);
}
