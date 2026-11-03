using System.Net.Mail;
using Turning.Application.Exceptions;
using Turning.Application.Interfaces;
using Turning.Domain.Common;
using Turning.Domain.Entities;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;

namespace Turning.Application.Features.Auth;

/// <summary>
/// Implementa los casos de uso de registro e inicio de sesión.
/// </summary>
public sealed class AuthService : IAuthService
{
    /// <summary>
    /// Longitud mínima de contraseña, compartida por el registro y el restablecimiento.
    /// </summary>
    public const int MinimumPasswordLength = 8;

    /// <summary>
    /// Indica si el texto es una única dirección de correo, sin nombre visible ni listas.
    /// </summary>
    /// <remarks>
    /// Una lista como "a@x.com, b@y.com" guardada como correo de una cuenta haría que el
    /// correo de restablecimiento saliera a todas esas direcciones.
    /// </remarks>
    public static bool IsPlainEmailAddress(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && MailAddress.TryCreate(email.Trim(), out var parsed)
        && parsed.DisplayName.Length == 0
        && parsed.Address == email.Trim();

    private readonly IUserAccountRepository _userAccountRepository;
    private readonly IPasswordHasherService _passwordHasherService;
    private readonly ITokenService _tokenService;

    /// <summary>
    /// Constructor del servicio de autenticación.
    /// </summary>
    public AuthService(
        IUserAccountRepository userAccountRepository,
        IPasswordHasherService passwordHasherService,
        ITokenService tokenService)
    {
        _userAccountRepository = userAccountRepository;
        _passwordHasherService = passwordHasherService;
        _tokenService = tokenService;
    }

    /// <inheritdoc />
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRegisterRequest(request);

        var exists = await _userAccountRepository.ExistsByEmailAsync(request.Email, cancellationToken);
        if (exists)
        {
            throw new TurningApplicationException("Ya existe un usuario registrado con ese correo.", "AUTH_USER_ALREADY_EXISTS");
        }

        var provisionalUser = UserAccount.Create(request.Email, request.FullName, "pending", UserRoles.Participant);
        var passwordHash = _passwordHasherService.HashPassword(provisionalUser, request.Password);
        provisionalUser.UpdatePasswordHash(passwordHash);
        provisionalUser.RecordSuccessfulLogin();

        await _userAccountRepository.AddAsync(provisionalUser, cancellationToken);
        await _userAccountRepository.SaveChangesAsync(cancellationToken);

        return _tokenService.CreateToken(provisionalUser);
    }

    /// <inheritdoc />
    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ValidateLoginRequest(request);

        var userAccount = await _userAccountRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (userAccount is null || !_passwordHasherService.VerifyPassword(userAccount, request.Password))
        {
            throw new TurningApplicationException("Las credenciales proporcionadas no son válidas.", "AUTH_INVALID_CREDENTIALS");
        }

        userAccount.RecordSuccessfulLogin();
        await _userAccountRepository.SaveChangesAsync(cancellationToken);

        return _tokenService.CreateToken(userAccount);
    }

    /// <inheritdoc />
    public async Task<AuthenticatedUser> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userAccount = await _userAccountRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("UserAccount", userId.ToString());

        return new AuthenticatedUser
        {
            Id = userAccount.Id,
            FullName = userAccount.FullName,
            Email = userAccount.Email,
            Role = userAccount.Role
        };
    }

    private static void ValidateRegisterRequest(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            throw new TurningApplicationException("El nombre completo es obligatorio.", "AUTH_INVALID_REQUEST");

        if (string.IsNullOrWhiteSpace(request.Email))
            throw new TurningApplicationException("El correo es obligatorio.", "AUTH_INVALID_REQUEST");

        if (!IsPlainEmailAddress(request.Email))
            throw new TurningApplicationException("Escribe un correo válido.", "AUTH_INVALID_REQUEST");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinimumPasswordLength)
            throw new TurningApplicationException($"La contraseña debe tener al menos {MinimumPasswordLength} caracteres.", "AUTH_INVALID_REQUEST");

        if (string.IsNullOrWhiteSpace(request.Role)) return;
        if (!UserRoles.IsKnown(request.Role))
            throw new TurningApplicationException(
                $"El rol '{request.Role}' no es un rol reconocido. Roles válidos: {string.Join(", ", UserRoles.All)}.",
                "AUTH_ROLE_UNKNOWN");
        if (UserRoles.Normalize(request.Role) != UserRoles.Participant)
            throw new TurningApplicationException("Solo un administrador puede asignar roles privilegiados.", "AUTH_ROLE_FORBIDDEN");
    }

    public async Task<AuthenticatedUser> AssignRoleAsync(Guid actorId, Guid userId, string role, CancellationToken ct = default)
    {
        var actor = await _userAccountRepository.GetByIdAsync(actorId, ct);
        if (actor?.Role != UserRoles.Administrator)
            throw new TurningApplicationException("Se requiere un administrador.", "AUTH_FORBIDDEN");
        if (!UserRoles.IsKnown(role))
            throw new TurningApplicationException("El rol no es válido.", "AUTH_ROLE_UNKNOWN");
        var user = await _userAccountRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("UserAccount", userId.ToString());
        if (actorId == userId && UserRoles.Normalize(role) != UserRoles.Administrator)
            throw new TurningApplicationException("No puedes quitar tu propio acceso de administrador.", "AUTH_ROLE_SELF_CHANGE");
        user.AssignRole(role);
        await _userAccountRepository.SaveChangesAsync(ct);
        return new AuthenticatedUser { Id = user.Id, Email = user.Email, FullName = user.FullName, Role = user.Role };
    }

    /// <inheritdoc />
    public async Task<PagedUsersResult> ListUsersAsync(Guid actorId, string? query, int page, int pageSize, CancellationToken ct = default)
    {
        var actor = await _userAccountRepository.GetByIdAsync(actorId, ct);
        if (actor?.Role != UserRoles.Administrator)
            throw new TurningApplicationException("Se requiere un administrador.", "AUTH_FORBIDDEN");
        if (page < 1 || pageSize < 1 || pageSize > 50)
            throw new TurningApplicationException("Paginación inválida.", "AUTH_INVALID_PAGE");

        var (items, total) = await _userAccountRepository.SearchAsync(query, page, pageSize, ct);
        return new PagedUsersResult
        {
            Items = items.Select(user => new AuthenticatedUser { Id = user.Id, Email = user.Email, FullName = user.FullName, Role = user.Role }).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private static void ValidateLoginRequest(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw new TurningApplicationException("Correo y contraseña son obligatorios.", "AUTH_INVALID_REQUEST");
    }
}
