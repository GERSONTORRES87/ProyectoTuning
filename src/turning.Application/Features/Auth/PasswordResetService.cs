using System.Buffers.Text;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turning.Application.Features.ExperimentSessions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;

namespace Turning.Application.Features.Auth;

/// <summary>
/// Restablecimiento de contraseña con un enlace enviado por correo.
/// </summary>
/// <remarks>
/// El token no se guarda en ningún lado: firma con <c>Jwt:SigningKey</c> el usuario, el
/// vencimiento y la <see cref="PasswordStamp"/> de la contraseña actual. En cuanto la contraseña
/// cambia, la huella cambia, así que el token sirve una sola vez sin tabla ni migración.
/// </remarks>
public sealed class PasswordResetService : IPasswordResetService
{
    private const string InvalidTokenCode = "AUTH_RESET_TOKEN_INVALID";
    private const string Subject = "SUE: restablece tu contraseña";

    private readonly IUserAccountRepository _users;
    private readonly IPasswordHasherService _hasher;
    private readonly IEmailSender _email;
    private readonly PasswordResetCooldown _cooldown;
    private readonly IOptions<JwtOptions> _jwt;
    private readonly IOptions<PasswordResetOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PasswordResetService> _logger;

    /// <summary>
    /// Constructor del servicio de restablecimiento.
    /// </summary>
    public PasswordResetService(
        IUserAccountRepository users,
        IPasswordHasherService hasher,
        IEmailSender email,
        PasswordResetCooldown cooldown,
        IOptions<JwtOptions> jwt,
        IOptions<PasswordResetOptions> options,
        TimeProvider time,
        ILogger<PasswordResetService> logger)
    {
        _users = users;
        _hasher = hasher;
        _email = email;
        _cooldown = cooldown;
        _jwt = jwt;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RequestAsync(string email, CancellationToken cancellationToken = default)
    {
        if (!AuthService.IsPlainEmailAddress(email))
            throw new TurningApplicationException("Escribe un correo válido.", "AUTH_INVALID_REQUEST");

        var user = await _users.GetByEmailAsync(email.Trim(), cancellationToken);
        if (user is null)
            return;

        var options = _options.Value;
        if (options.Validate().Count > 0)
        {
            _logger.LogWarning("No se envió el correo de restablecimiento: falta configurar la sección PasswordReset.");
            return;
        }

        var now = _time.GetUtcNow();
        if (!_cooldown.TryReserve(user.NormalizedEmail, now))
            return;

        var token = CreateToken(user, now.AddMinutes(options.TokenMinutes));
        var link = $"{options.BaseUrl!.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";

        // No se espera el envío: si la respuesta tardara solo cuando la cuenta existe, el tiempo
        // delataría qué correos están registrados.
        _ = SendInBackgroundAsync(user.Email, user.NormalizedEmail, now, Body(link, options.TokenMinutes));
    }

    /// <inheritdoc />
    public async Task ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < AuthService.MinimumPasswordLength)
            throw new TurningApplicationException(
                $"La contraseña debe tener al menos {AuthService.MinimumPasswordLength} caracteres.", "AUTH_INVALID_REQUEST");

        // Todo token no válido termina en el mismo error, para que la respuesta sea idéntica en todos los casos.
        var invalid = new TurningApplicationException("El enlace de restablecimiento no es válido o ya venció. Pide uno nuevo.", InvalidTokenCode);
        if (!TryReadToken(token, out var parsed))
            throw invalid;

        // Sin el candado, dos peticiones con el mismo token validarían contra el hash viejo y pasarían las dos.
        using var operation = await SessionOperationLock.AcquireAsync(parsed.UserId, cancellationToken);

        var user = await _users.GetByIdAsync(parsed.UserId, cancellationToken);
        if (user is null || !CryptographicOperations.FixedTimeEquals(parsed.Signature, Sign(user, parsed.Payload)))
            throw invalid;

        user.UpdatePasswordHash(_hasher.HashPassword(user, newPassword));
        await _users.SaveChangesAsync(cancellationToken);
    }

    // Corre después de responder: solo usa dependencias singleton (correo, candado de enfriamiento y log).
    private async Task SendInBackgroundAsync(string to, string accountKey, DateTimeOffset reservedAt, string body)
    {
        try
        {
            await _email.SendAsync(to, Subject, body, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Se libera la reserva para que la cuenta pueda reintentar sin esperar el minuto.
            _cooldown.Release(accountKey, reservedAt);

            // Ni el enlace ni el mensaje de la excepción: SMTP pone ahí la dirección del destinatario.
            var status = ex is SmtpException smtp ? $" {smtp.StatusCode}" : string.Empty;
            _logger.LogWarning("No se pudo enviar el correo de restablecimiento ({Error}{Estado}).", ex.GetType().Name, status);
        }
    }

    private string CreateToken(UserAccount user, DateTimeOffset expiresAt)
    {
        var payload = $"{user.Id:N}|{expiresAt.ToUnixTimeSeconds()}";
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{payload}|{Convert.ToHexString(Sign(user, payload))}"));
    }

    private bool TryReadToken(string token, out ParsedToken parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(token) || !Base64Url.IsValid(token))
            return false;

        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)).Split('|');
        if (parts.Length != 3
            || !Guid.TryParseExact(parts[0], "N", out var userId)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnix)
            || _time.GetUtcNow().ToUnixTimeSeconds() >= expiresAtUnix)
            return false;

        try { parsed = new ParsedToken(userId, $"{parts[0]}|{parts[1]}", Convert.FromHexString(parts[2])); }
        catch (FormatException) { return false; }

        return true;
    }

    private byte[] Sign(UserAccount user, string payload)
    {
        var key = _jwt.Value.SigningKey ?? throw new InvalidOperationException("Falta Jwt:SigningKey para firmar el enlace de restablecimiento.");
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes($"pwreset|{payload}|{PasswordStamp.Of(user)}"));
    }

    // Sin el nombre de la cuenta: es texto que elige quien se registra y no debe aparecer en un correo oficial.
    private static string Body(string link, int minutes) => $"""
        Hola:

        Recibimos una solicitud para restablecer la contraseña de tu cuenta de SUE.
        Abre este enlace para elegir una contraseña nueva. Vence en {minutes} minutos y solo sirve una vez:

        {link}

        Si no pediste el cambio, ignora este correo: tu contraseña actual sigue funcionando.
        """;

    private readonly record struct ParsedToken(Guid UserId, string Payload, byte[] Signature);
}
