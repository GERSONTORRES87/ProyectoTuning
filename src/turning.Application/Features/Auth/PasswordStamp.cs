using System.Security.Cryptography;
using System.Text;
using Turning.Domain.Entities;

namespace Turning.Application.Features.Auth;

/// <summary>
/// Huella corta del hash de contraseña de una cuenta: cambia en cuanto cambia la contraseña.
/// </summary>
/// <remarks>
/// Firma el token de restablecimiento (que así sirve una sola vez) y viaja en el JWT, de modo
/// que las sesiones emitidas antes de un cambio de contraseña dejan de ser aceptadas.
/// </remarks>
public static class PasswordStamp
{
    /// <summary>Nombre del claim del JWT que lleva la huella.</summary>
    public const string ClaimType = "pwd_stamp";

    /// <summary>Calcula la huella de la contraseña actual de la cuenta.</summary>
    public static string Of(UserAccount user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash)))[..16];
}
