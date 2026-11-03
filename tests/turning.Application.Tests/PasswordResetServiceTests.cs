using System.Buffers.Text;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Turning.Application.Features.Auth;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;
using Xunit;

namespace Turning.Application.Tests;

/// <summary>
/// Pruebas del restablecimiento de contraseña por correo.
/// </summary>
public class PasswordResetServiceTests
{
    private readonly IUserAccountRepository _users = Substitute.For<IUserAccountRepository>();
    private readonly IPasswordHasherService _hasher = Substitute.For<IPasswordHasherService>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly PasswordResetCooldown _cooldown = new();
    private readonly FakeTime _time = new();
    private readonly ListLogger _logger = new();
    private readonly List<(string To, string Body)> _sent = [];
    private readonly UserAccount _user = UserAccount.Create($"{Guid.NewGuid():N}@test.test", "Ana Prueba", "hash-inicial", "Participant");

    public PasswordResetServiceTests()
    {
        _users.GetByEmailAsync(_user.Email, Arg.Any<CancellationToken>()).Returns(_user);
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _hasher.HashPassword(Arg.Any<UserAccount>(), Arg.Any<string>()).Returns(call => "hash-" + call.ArgAt<string>(1));
        _email.When(e => e.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(call => _sent.Add((call.ArgAt<string>(0), call.ArgAt<string>(2))));
    }

    private PasswordResetService CreateService() => new(
        _users,
        _hasher,
        _email,
        _cooldown,
        Options.Create(new JwtOptions { SigningKey = "clave-de-prueba-con-mas-de-32-caracteres-0123456789" }),
        Options.Create(new PasswordResetOptions { BaseUrl = "https://sue.test/" }),
        _time,
        new Logger(_logger));

    // El envío corre sin esperarlo, pero con el sustituto termina antes de que RequestAsync devuelva.
    private async Task<string> RequestTokenAsync(PasswordResetService service)
    {
        await service.RequestAsync(_user.Email);
        var match = Regex.Match(_sent.Last().Body, @"https://sue\.test/reset-password\?token=(\S+)");
        match.Success.Should().BeTrue("el correo debe traer el enlace a BaseUrl");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("abc@")]
    [InlineData("Ana <ana@test.test>")]
    [InlineData("a@test.test, b@test.test")]
    public async Task RequestAsync_WithInvalidEmail_IsRejectedAndSendsNothing(string email)
    {
        var act = () => CreateService().RequestAsync(email);

        (await act.Should().ThrowAsync<TurningApplicationException>()).Which.Code.Should().Be("AUTH_INVALID_REQUEST");
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestAsync_WithUnknownEmail_SendsNothing()
    {
        await CreateService().RequestAsync($"{Guid.NewGuid():N}@test.test");

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestAsync_WithKnownEmail_SendsOneMessageWithoutTheAccountName()
    {
        await RequestTokenAsync(CreateService());

        _sent.Should().ContainSingle().Which.To.Should().Be(_user.Email);
        _sent[0].Body.Should().Contain("30 minutos").And.NotContain("Ana Prueba");
    }

    [Fact]
    public async Task RequestAsync_WithinCooldown_SendsOnlyOneMessage()
    {
        var service = CreateService();

        await service.RequestAsync(_user.Email);
        _time.Advance(TimeSpan.FromSeconds(59));
        await service.RequestAsync(_user.Email);
        _sent.Should().HaveCount(1);

        _time.Advance(TimeSpan.FromSeconds(2));
        await service.RequestAsync(_user.Email);
        _sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task ResetAsync_WithValidToken_ReplacesTheHash()
    {
        var service = CreateService();
        var token = await RequestTokenAsync(service);

        await service.ResetAsync(token, "ClaveNueva123");

        _user.PasswordHash.Should().Be("hash-ClaveNueva123");
        await _users.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetAsync_WithReusedToken_Fails()
    {
        var service = CreateService();
        var token = await RequestTokenAsync(service);
        await service.ResetAsync(token, "ClaveNueva123");

        var act = () => service.ResetAsync(token, "OtraClave456");

        (await act.Should().ThrowAsync<TurningApplicationException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
        _user.PasswordHash.Should().Be("hash-ClaveNueva123");
    }

    [Fact]
    public async Task ResetAsync_ConcurrentRequestsWithTheSameToken_OnlyOneSucceeds()
    {
        var service = CreateService();
        var token = await RequestTokenAsync(service);
        // La lectura tarda: sin candado, las dos validarían contra el hash viejo antes de que una lo cambie.
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(50);
            return (UserAccount?)_user;
        });

        var results = await Task.WhenAll(TryResetAsync(service, token, "ClaveNueva123"), TryResetAsync(service, token, "OtraClave456"));

        results.Count(succeeded => succeeded).Should().Be(1);
        await _users.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetAsync_WithExpiredToken_Fails()
    {
        var service = CreateService();
        var token = await RequestTokenAsync(service);
        _time.Advance(TimeSpan.FromMinutes(30));

        var act = () => service.ResetAsync(token, "ClaveNueva123");

        (await act.Should().ThrowAsync<TurningApplicationException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task ResetAsync_WithTamperedOrInventedToken_Fails()
    {
        var service = CreateService();
        var token = await RequestTokenAsync(service);
        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)).Split('|');
        string Encode(string raw) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
        var otherSignature = Encode($"{parts[0]}|{parts[1]}|{(parts[2][0] == '0' ? '1' : '0')}{parts[2][1..]}");
        var longerExpiry = Encode($"{parts[0]}|{long.Parse(parts[1]) + 3600}|{parts[2]}");

        foreach (var bad in new[] { otherSignature, longerExpiry, "abc", "", "no|es|base64url!" })
        {
            var act = () => service.ResetAsync(bad, "ClaveNueva123");
            (await act.Should().ThrowAsync<TurningApplicationException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
        }

        _user.PasswordHash.Should().Be("hash-inicial");
    }

    [Fact]
    public async Task ResetAsync_ForMissingUser_Fails()
    {
        var service = CreateService();
        var token = await RequestTokenAsync(service);
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns((UserAccount?)null);

        var act = () => service.ResetAsync(token, "ClaveNueva123");

        (await act.Should().ThrowAsync<TurningApplicationException>()).Which.Code.Should().Be("AUTH_RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task ResetAsync_WithShortPassword_FailsAndKeepsTheTokenUsable()
    {
        var service = CreateService();
        var token = await RequestTokenAsync(service);

        var act = () => service.ResetAsync(token, "corta");

        (await act.Should().ThrowAsync<TurningApplicationException>()).Which.Code.Should().Be("AUTH_INVALID_REQUEST");
        await service.ResetAsync(token, "ClaveNueva123");
        _user.PasswordHash.Should().Be("hash-ClaveNueva123");
    }

    [Fact]
    public async Task RequestAsync_WhenSendingFails_LogsNoPersonalDataAndAllowsARetry()
    {
        string? body = null;
        _email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Do<string>(b => body = b), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException($"buzón no disponible: {_user.Email}"));
        var service = CreateService();

        var act = () => service.RequestAsync(_user.Email);

        await act.Should().NotThrowAsync();
        var token = Regex.Match(body!, @"token=(\S+)").Groups[1].Value;
        token.Should().NotBeEmpty();
        _logger.Messages.Should().NotBeEmpty()
            .And.OnlyContain(m => !m.Contains(token) && !m.Contains("reset-password") && !m.Contains(_user.Email));

        // El fallo libera el minuto de espera: el reintento inmediato vuelve a enviar.
        await service.RequestAsync(_user.Email);
        await _email.Received(2).SendAsync(_user.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Cooldown_ReleasedReservation_CanBeTakenAgain()
    {
        var now = DateTimeOffset.UtcNow;

        _cooldown.TryReserve("ANA", now).Should().BeTrue();
        _cooldown.TryReserve("ANA", now.AddSeconds(1)).Should().BeFalse();
        _cooldown.Release("ANA", now);
        _cooldown.TryReserve("ANA", now.AddSeconds(2)).Should().BeTrue();
    }

    private static async Task<bool> TryResetAsync(PasswordResetService service, string token, string password)
    {
        try
        {
            await service.ResetAsync(token, password);
            return true;
        }
        catch (TurningApplicationException e) when (e.Code == "AUTH_RESET_TOKEN_INVALID")
        {
            return false;
        }
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class ListLogger
    {
        public List<string> Messages { get; } = [];
    }

    private sealed class Logger(ListLogger sink) : ILogger<PasswordResetService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink.Messages.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}
