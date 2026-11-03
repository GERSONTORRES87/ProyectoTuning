using Turning.Domain.Common;

namespace Turning.Domain.Entities;

/// <summary>
/// Representa un usuario autenticable persistido en la base de datos.
/// </summary>
public sealed class UserAccount : BaseEntity
{
    private UserAccount()
    {
    }

    private UserAccount(string email, string fullName, string passwordHash, string role)
    {
        Email = email.Trim();
        NormalizedEmail = NormalizeEmail(email);
        FullName = fullName.Trim();
        PasswordHash = passwordHash;
        Role = role.Trim();
    }

    /// <summary>
    /// Correo electrónico original del usuario.
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Correo electrónico normalizado para búsquedas y unicidad.
    /// </summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>
    /// Nombre visible del usuario.
    /// </summary>
    public string FullName { get; private set; } = string.Empty;

    /// <summary>
    /// Hash de la contraseña.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Rol asignado al usuario.
    /// </summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>
    /// Marca de la última autenticación exitosa.
    /// </summary>
    public DateTime? LastLoginAt { get; private set; }

    /// <summary>
    /// Crea una nueva cuenta de usuario válida.
    /// </summary>
    public static UserAccount Create(string email, string fullName, string passwordHash, string role)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("El correo es obligatorio.", nameof(email));

        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("El nombre completo es obligatorio.", nameof(fullName));

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("El hash de contraseña es obligatorio.", nameof(passwordHash));

        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("El rol es obligatorio.", nameof(role));

        if (!UserRoles.IsKnown(role))
            throw new ArgumentException($"El rol '{role}' no es un rol reconocido del sistema.", nameof(role));

        return new UserAccount(email, fullName, passwordHash, UserRoles.Normalize(role));
    }

    /// <summary>
    /// Registra un inicio de sesión exitoso.
    /// </summary>
    public void RecordSuccessfulLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        UpdatedAt = LastLoginAt;
    }

    public void AssignRole(string role)
    {
        if (!UserRoles.IsKnown(role)) throw new ArgumentException("El rol no es válido.", nameof(role));
        Role = UserRoles.Normalize(role);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Actualiza el hash de la contraseña.
    /// </summary>
    public void UpdatePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("El hash de contraseña es obligatorio.", nameof(passwordHash));

        PasswordHash = passwordHash;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Normaliza un correo para uso persistente.
    /// </summary>
    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("El correo es obligatorio.", nameof(email));

        return email.Trim().ToUpperInvariant();
    }
}
