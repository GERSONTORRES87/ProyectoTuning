using FluentAssertions;
using NSubstitute;
using Turning.Application.Features.Auth;
using Turning.Application.Interfaces;
using Turning.Domain.Common;
using Turning.Domain.Entities;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;
using Xunit;

namespace Turning.Application.Tests;

/// <summary>
/// Pruebas del servicio de autenticación.
/// </summary>
public class AuthServiceTests
{
    private readonly IUserAccountRepository _userRepository = Substitute.For<IUserAccountRepository>();
    private readonly IPasswordHasherService _passwordHasherService = Substitute.For<IPasswordHasherService>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();

    [Fact]
    public async Task RegisterAsync_WithNewEmail_ShouldPersistUserAndReturnToken()
    {
        // Arrange
        var service = new AuthService(_userRepository, _passwordHasherService, _tokenService);
        var request = new RegisterRequest
        {
            FullName = "Ada Lovelace",
            Email = "ada@example.com",
            Password = "secret123",
            Role = UserRoles.Participant
        };

        _userRepository.ExistsByEmailAsync(request.Email, Arg.Any<CancellationToken>()).Returns(false);
        _passwordHasherService.HashPassword(Arg.Any<UserAccount>(), request.Password).Returns("hashed-password");
        _tokenService.CreateToken(Arg.Any<UserAccount>()).Returns(call =>
        {
            var user = call.Arg<UserAccount>();

            return new AuthResult
            {
                AccessToken = "token",
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                User = new AuthenticatedUser
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role
                }
            };
        });

        // Act
        var result = await service.RegisterAsync(request);

        // Assert
        result.AccessToken.Should().Be("token");
        result.User.Email.Should().Be(request.Email);
        await _userRepository.Received(1).AddAsync(Arg.Is<UserAccount>(user =>
            user.Email == request.Email &&
            user.FullName == request.FullName &&
            user.Role == request.Role), Arg.Any<CancellationToken>());
        await _userRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginAsync_WithInvalidPassword_ShouldThrowApplicationException()
    {
        // Arrange
        var service = new AuthService(_userRepository, _passwordHasherService, _tokenService);
        var user = UserAccount.Create("grace@example.com", "Grace Hopper", "stored-hash", UserRoles.Participant);

        _userRepository.GetByEmailAsync("grace@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasherService.VerifyPassword(user, "wrong-password").Returns(false);

        // Act
        Func<Task> act = async () => await service.LoginAsync(new LoginRequest
        {
            Email = "grace@example.com",
            Password = "wrong-password"
        });

        // Assert
        var exceptionAssertion = await act.Should().ThrowAsync<TurningApplicationException>();
        exceptionAssertion.Which.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }
    [Theory]
    [InlineData("Researcher")]
    [InlineData("Administrator")]
    public async Task RegisterAsync_WithPrivilegedRole_ShouldRejectWithoutPersisting(string rolePedido)
    {
        // Arrange
        var service = new AuthService(_userRepository, _passwordHasherService, _tokenService);
        var request = new RegisterRequest
        {
            FullName = "Mallory",
            Email = "mallory@example.com",
            Password = "secret123",
            Role = rolePedido
        };

        _userRepository.ExistsByEmailAsync(request.Email, Arg.Any<CancellationToken>()).Returns(false);
        _passwordHasherService.HashPassword(Arg.Any<UserAccount>(), request.Password).Returns("hashed-password");
        _tokenService.CreateToken(Arg.Any<UserAccount>()).Returns(new AuthResult
        {
            AccessToken = "token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new AuthenticatedUser
            {
                Id = Guid.NewGuid(),
                FullName = request.FullName,
                Email = request.Email,
                Role = rolePedido
            }
        });

        // Act
        var act = () => service.RegisterAsync(request);

        // Assert
        (await act.Should().ThrowAsync<TurningApplicationException>()).Which.Code.Should().Be("AUTH_ROLE_FORBIDDEN");
        await _userRepository.DidNotReceive().AddAsync(Arg.Any<UserAccount>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterAsync_WithoutRole_ShouldDefaultToParticipant(string rolePedido)
    {
        // Arrange
        var service = new AuthService(_userRepository, _passwordHasherService, _tokenService);
        var request = new RegisterRequest
        {
            FullName = "Sin Rol",
            Email = "sin-rol@example.com",
            Password = "secret123",
            Role = rolePedido
        };

        // Act
        _passwordHasherService.HashPassword(Arg.Any<UserAccount>(), request.Password).Returns("hashed-password");

        // Assert
        await service.RegisterAsync(request);
        await _userRepository.Received(1).AddAsync(Arg.Is<UserAccount>(u => u.Role == UserRoles.Participant), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_WithUnknownRole_ShouldRejectBeforeTouchingRepository()
    {
        // Arrange
        var service = new AuthService(_userRepository, _passwordHasherService, _tokenService);
        var request = new RegisterRequest
        {
            FullName = "Rol Inventado",
            Email = "inventado@example.com",
            Password = "secret123",
            Role = "SuperAdmin"
        };

        // Act
        Func<Task> act = async () => await service.RegisterAsync(request);

        // Assert
        var exceptionAssertion = await act.Should().ThrowAsync<TurningApplicationException>();
        exceptionAssertion.Which.Code.Should().Be("AUTH_ROLE_UNKNOWN");

        await _userRepository.DidNotReceive().AddAsync(Arg.Any<UserAccount>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("a@test.test, b@test.test")]
    [InlineData("Ana <ana@test.test>")]
    public async Task RegisterAsync_WithEmailThatIsNotASingleAddress_ShouldRejectBeforeTouchingRepository(string email)
    {
        // Una lista guardada como correo haría que el restablecimiento escribiera a varias direcciones.
        var service = new AuthService(_userRepository, _passwordHasherService, _tokenService);

        Func<Task> act = async () => await service.RegisterAsync(new RegisterRequest { FullName = "Ana", Email = email, Password = "secret123" });

        (await act.Should().ThrowAsync<TurningApplicationException>()).Which.Code.Should().Be("AUTH_INVALID_REQUEST");
        await _userRepository.DidNotReceive().AddAsync(Arg.Any<UserAccount>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("participant")]
    [InlineData("PARTICIPANT")]
    [InlineData("  Participant  ")]
    public async Task RegisterAsync_WithParticipantInAnyCasing_ShouldPersistCanonicalRole(string rolePedido)
    {
        // Arrange
        var service = new AuthService(_userRepository, _passwordHasherService, _tokenService);
        var request = new RegisterRequest
        {
            FullName = "Casing Libre",
            Email = "casing@example.com",
            Password = "secret123",
            Role = rolePedido
        };

        _userRepository.ExistsByEmailAsync(request.Email, Arg.Any<CancellationToken>()).Returns(false);
        _passwordHasherService.HashPassword(Arg.Any<UserAccount>(), request.Password).Returns("hashed-password");
        _tokenService.CreateToken(Arg.Any<UserAccount>()).Returns(new AuthResult
        {
            AccessToken = "token",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            User = new AuthenticatedUser
            {
                Id = Guid.NewGuid(),
                FullName = request.FullName,
                Email = request.Email,
                Role = UserRoles.Participant
            }
        });

        // Act
        await service.RegisterAsync(request);

        // Assert
        await _userRepository.Received(1).AddAsync(
            Arg.Is<UserAccount>(user => user.Role == UserRoles.Participant),
            Arg.Any<CancellationToken>());
    }
}
