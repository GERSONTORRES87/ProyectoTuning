namespace Turning.Infrastructure.Email;

/// <summary>
/// Configuración del envío de correo. Solo <c>Smtp:Password</c> es secreto: va por entorno
/// (<c>Email__Smtp__Password</c>) o user-secrets, nunca en appsettings*.json.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string SectionName = "Email";

    /// <summary>Remitente. Con Gmail debe ser la misma cuenta autenticada.</summary>
    public string? From { get; set; }

    /// <summary>Servidor SMTP con STARTTLS.</summary>
    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>
    /// Solo para Development: si tiene valor, el correo se escribe como .eml en esta carpeta
    /// en lugar de enviarse. Una ruta relativa cuelga de la carpeta local de la aplicación.
    /// </summary>
    public string? PickupDirectory { get; set; }

    /// <summary>
    /// Valida la configuración para enviar correo de verdad y devuelve los problemas encontrados.
    /// Lista vacía significa configuración utilizable. Solo se llama fuera de Development.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(From))
            errors.Add("Falta 'Email:From' (dirección de la cuenta Gmail dedicada a SUE).");

        if (!string.IsNullOrWhiteSpace(PickupDirectory))
        {
            errors.Add("'Email:PickupDirectory' está activo: los correos se guardan en disco y no se envía ninguno.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(Smtp.Host))
            errors.Add("Falta 'Email:Smtp:Host' (smtp.gmail.com para la cuenta Gmail dedicada).");

        if (string.IsNullOrWhiteSpace(Smtp.Username) || string.IsNullOrWhiteSpace(Smtp.Password))
            errors.Add("Faltan 'Email:Smtp:Username' o 'Email:Smtp:Password'; la contraseña es la de aplicación y va por entorno (Email__Smtp__Password).");

        return errors;
    }

    /// <summary>Datos del servidor SMTP.</summary>
    public sealed class SmtpSettings
    {
        /// <summary>Servidor, p. ej. smtp.gmail.com.</summary>
        public string? Host { get; set; }

        /// <summary>Puerto STARTTLS.</summary>
        public int Port { get; set; } = 587;

        /// <summary>Usuario de la cuenta.</summary>
        public string? Username { get; set; }

        /// <summary>Contraseña de aplicación. Secreto.</summary>
        public string? Password { get; set; }
    }
}
