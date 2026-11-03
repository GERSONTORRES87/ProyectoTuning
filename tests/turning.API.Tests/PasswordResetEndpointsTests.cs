using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Turning.API.Controllers;
using Turning.Application.Features.Auth;
using Xunit;

namespace Turning.API.Tests;

/// <summary>
/// Contrato de los endpoints de restablecimiento de contraseña.
/// </summary>
public class PasswordResetEndpointsTests
{
    private readonly IPasswordResetService _resets = Substitute.For<IPasswordResetService>();
    private readonly AuthController _controller = new(Substitute.For<IAuthService>());

    [Theory]
    [InlineData(nameof(AuthController.ForgotPassword))]
    [InlineData(nameof(AuthController.ResetPassword))]
    public void Endpoints_AreAnonymous(string action)
    {
        typeof(AuthController).GetMethod(action)!.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
    }

    [Fact]
    public async Task ForgotPassword_AnswersTheSameBodyForKnownAndUnknownEmails()
    {
        // El servicio no informa si la cuenta existe; el controller tampoco debe distinguir.
        var known = await _controller.ForgotPassword(new() { Email = "ana@test.test" }, _resets, CancellationToken.None);
        var unknown = await _controller.ForgotPassword(new() { Email = "nadie@test.test" }, _resets, CancellationToken.None);

        var first = known.Should().BeOfType<AcceptedResult>().Subject;
        var second = unknown.Should().BeOfType<AcceptedResult>().Subject;
        first.StatusCode.Should().Be(202);
        JsonSerializer.Serialize(first.Value).Should().Be("{}").And.Be(JsonSerializer.Serialize(second.Value));
    }

    [Fact]
    public async Task ResetPassword_AnswersNoContentWithoutASessionToken()
    {
        var result = await _controller.ResetPassword(new() { Token = "token", NewPassword = "ClaveNueva123" }, _resets, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await _resets.Received(1).ResetAsync("token", "ClaveNueva123", Arg.Any<CancellationToken>());
    }
}
