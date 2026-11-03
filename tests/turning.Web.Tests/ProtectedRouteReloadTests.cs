using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using turning.Web.Auth;
using Xunit;

namespace turning.Web.Tests;

public class ProtectedRouteReloadTests
{
    [Theory]
    [InlineData("", "/surveys", "?sessionId=123")]
    [InlineData("", "/conversations", "?sessionId=123&view=chat")]
    [InlineData("/tuning", "/workspace", "")]
    public async Task Challenge_RedirectsToLoginAndPreservesTheLocalRoute(string pathBase, string path, string query)
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var handlerType = typeof(AuthApiClient).Assembly.GetType("BlazorServerAuthHandler", throwOnError: true)!;
        var handler = (IAuthenticationHandler)ActivatorUtilities.CreateInstance(services, handlerType, UrlEncoder.Default);
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.PathBase = pathBase;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        await handler.InitializeAsync(new AuthenticationScheme("BlazorServer", null, handlerType), context);

        await handler.ChallengeAsync(new AuthenticationProperties());

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal($"{pathBase}/login?returnUrl={Uri.EscapeDataString(pathBase + path + query)}", context.Response.Headers.Location.ToString());
    }
}
