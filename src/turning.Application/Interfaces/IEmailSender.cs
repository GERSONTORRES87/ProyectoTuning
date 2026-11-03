namespace Turning.Application.Interfaces;

/// <summary>
/// Puerto de envío de correo electrónico. El adaptador vive en Infrastructure.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Envía un correo de texto plano a un destinatario.
    /// </summary>
    Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken = default);
}
