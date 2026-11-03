using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Turning.API.Extensions;
using Turning.Application.Features.Auth;
using Turning.Application.Interfaces;
using Turning.Domain.Common;
using Turning.Domain.Entities;
using Xunit;

namespace Turning.API.Tests;

public class JwtRoleValidationTests
{
    [Theory]
    [InlineData(UserRoles.Researcher, true)]
    [InlineData(UserRoles.Participant, false)]
    public async Task TokenRole_ShouldMatchTheCurrentStoredRole(string storedRole, bool accepted)
    {
        var users = Substitute.For<IUserAccountRepository>();
        var user = UserAccount.Create("role@example.com", "Role", "hash", storedRole);
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = new string('x', 64)
        }).Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(users);
        services.AddJwtAuthentication(configuration, environment);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var context = new TokenValidatedContext(new DefaultHttpContext { RequestServices = provider },
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, UserRoles.Researcher)
            ], "test"))
        };

        await options.Events.TokenValidated(context);

        if (accepted) context.Result.Should().BeNull();
        else context.Result!.Failure.Should().NotBeNull();
    }

    [Theory]
    [InlineData("current", true)]
    [InlineData("old", false)]
    [InlineData(null, true)]
    public async Task PasswordStamp_ShouldRejectSessionsIssuedBeforeAPasswordChange(string? stampKind, bool accepted)
    {
        var users = Substitute.For<IUserAccountRepository>();
        var user = UserAccount.Create("stamp@example.com", "Stamp", "hash-actual", UserRoles.Participant);
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = new string('x', 64)
        }).Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(users);
        services.AddJwtAuthentication(configuration, environment);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Role, UserRoles.Participant)];
        // "old" simula un token emitido antes de cambiar la contraseña; null, uno emitido antes de este cambio.
        if (stampKind == "current") claims.Add(new(PasswordStamp.ClaimType, PasswordStamp.Of(user)));
        if (stampKind == "old") claims.Add(new(PasswordStamp.ClaimType, "0000000000000000"));
        var context = new TokenValidatedContext(new DefaultHttpContext { RequestServices = provider },
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };

        await options.Events.TokenValidated(context);

        if (accepted) context.Result.Should().BeNull();
        else context.Result!.Failure.Should().NotBeNull();
    }
}
