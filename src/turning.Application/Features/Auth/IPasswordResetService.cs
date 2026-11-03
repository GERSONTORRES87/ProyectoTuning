namespace Turning.Application.Features.Auth;

/// <summary>
/// Restablecimiento de contraseña con un enlace enviado por correo.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Envía el enlace de restablecimiento si el correo pertenece a una cuenta. No revela si existe.
    /// </summary>
    Task RequestAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reemplaza la contraseña usando el token del enlace. No inicia sesión.
    /// </summary>
    Task ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}
