using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Turning.API.Controllers;
using Xunit;

namespace Turning.API.Tests;

/// <summary>
/// Contrato del listado de cuentas para administración.
/// </summary>
public class AdminUsersEndpointTests
{
    [Fact]
    public void ListUsers_RequiresTheAdministratorRole()
    {
        var authorize = typeof(AuthController).GetMethod(nameof(AuthController.ListUsers))!.GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("Administrator");
    }
}
