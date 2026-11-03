using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Turning.API.Middleware;
using Xunit;

namespace Turning.API.Tests;

public class ApiErrorContractTests
{
    [Theory]
    [InlineData("CONVERSATION_EMPTY_MESSAGE", 400)]
    [InlineData("CONVERSATION_MESSAGE_TOO_LONG", 400)]
    [InlineData("SESSION_TERMINAL", 409)]
    [InlineData("AUTH_ROLE_FORBIDDEN", 403)]
    [InlineData("AUTH_RESET_TOKEN_INVALID", 400)]
    public async Task BusinessErrors_ShouldExposeTheStableCode(string code, int status)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new Turning.Application.Exceptions.ApplicationException("Mensaje seguro", code),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(status);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("error").GetString().Should().Be(code);
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    [Fact]
    public async Task UnexpectedFailure_ShouldNotExposeInternalDetails()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new InvalidOperationException("private database connection details"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        context.Response.StatusCode.Should().Be(500);
        text.Should().NotContain("private database");
        text.Should().Contain("INTERNAL_ERROR");
    }
}
