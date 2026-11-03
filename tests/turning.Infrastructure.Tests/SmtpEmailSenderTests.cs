using FluentAssertions;
using Microsoft.Extensions.Options;
using Turning.Application.Features.Auth;
using Turning.Infrastructure.Email;
using Xunit;

namespace Turning.Infrastructure.Tests;

/// <summary>
/// Pruebas del adaptador de correo y de la validación de su configuración.
/// </summary>
public class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_WithPickupDirectory_CreatesTheFolderAndWritesTheMessage()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"turning-mail-{Guid.NewGuid():N}");
        try
        {
            var sender = new SmtpEmailSender(Options.Create(new EmailOptions { From = "sue@example.com", PickupDirectory = folder }));

            // Sin '=' ni acentos en el cuerpo: así el .eml no depende de la codificación de transferencia elegida.
            await sender.SendAsync("ana@example.com", "Prueba de restablecimiento", "Enlace: http://localhost/reset-password/token-abc");

            var file = Directory.GetFiles(folder, "*.eml").Should().ContainSingle().Subject;
            var content = await File.ReadAllTextAsync(file);
            content.Should().Contain("To: ana@example.com")
                .And.Contain("Subject: Prueba de restablecimiento")
                .And.Contain("http://localhost/reset-password/token-abc");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Validate_WithCompleteGmailSettings_HasNoProblems()
    {
        var options = new EmailOptions
        {
            From = "sue@gmail.com",
            Smtp = new() { Host = "smtp.gmail.com", Username = "sue@gmail.com", Password = "app-password" }
        };

        options.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithoutHost_ReportsIt()
    {
        var options = new EmailOptions { From = "sue@gmail.com", Smtp = new() { Username = "sue@gmail.com", Password = "app-password" } };

        options.Validate().Should().ContainSingle().Which.Should().Contain("Email:Smtp:Host");
    }

    [Fact]
    public void Validate_WithPickupDirectory_WarnsThatNothingIsSent()
    {
        var options = new EmailOptions { From = "sue@gmail.com", PickupDirectory = "Turning/mail" };

        options.Validate().Should().ContainSingle().Which.Should().Contain("Email:PickupDirectory");
    }

    [Fact]
    public async Task SendAsync_WithARecipientList_IsRejected()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"turning-mail-{Guid.NewGuid():N}");
        try
        {
            var sender = new SmtpEmailSender(Options.Create(new EmailOptions { From = "sue@example.com", PickupDirectory = folder }));

            var act = () => sender.SendAsync("ana@example.com, otra@example.com", "Prueba", "Cuerpo");

            await act.Should().ThrowAsync<FormatException>();
            (Directory.Exists(folder) ? Directory.GetFiles(folder) : []).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void JwtToken_CarriesThePasswordStamp()
    {
        var user = Turning.Domain.Entities.UserAccount.Create("stamp@example.com", "Stamp", "hash-actual", "Participant");
        var tokens = new Turning.Infrastructure.Security.JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('x', 64) }));

        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(tokens.CreateToken(user).AccessToken);

        jwt.Claims.Should().ContainSingle(c => c.Type == PasswordStamp.ClaimType).Which.Value.Should().Be(PasswordStamp.Of(user));
    }

    [Fact]
    public void PasswordResetOptions_WithoutBaseUrl_ReportsIt()
    {
        new PasswordResetOptions().Validate().Should().ContainSingle().Which.Should().Contain("PasswordReset:BaseUrl");
        new PasswordResetOptions { BaseUrl = "https://sue.example" }.Validate().Should().BeEmpty();
    }
}
