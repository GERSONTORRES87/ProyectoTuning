namespace Turning.Application.Features.Auth;

/// <summary>
/// Configuración del restablecimiento de contraseña por correo.
/// </summary>
public sealed class PasswordResetOptions
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string SectionName = "PasswordReset";

    /// <summary>URL pública de la Web; el enlace del correo apunta a {BaseUrl}/reset-password.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Vigencia del enlace en minutos.</summary>
    public int TokenMinutes { get; set; } = 30;

    /// <summary>
    /// Valida la configuración y devuelve los problemas encontrados.
    /// Lista vacía significa configuración utilizable.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add("Falta 'PasswordReset:BaseUrl' (URL pública de la Web, p. ej. https://...); sin ella no se puede armar el enlace del correo.");

        if (TokenMinutes <= 0)
            errors.Add($"'PasswordReset:TokenMinutes' debe ser mayor que cero; recibido: {TokenMinutes}.");

        return errors;
    }
}
