using Microsoft.JSInterop;
using turning.Web.Auth;
using Xunit;

namespace turning.Web.Tests;

public class AuthStateTests
{
    [Theory]
    [InlineData("not-a-token")]
    [InlineData("header.%%%invalid.signature")]
    [InlineData("header.e30.signature")]
    public async Task InvalidOrExpiryFreePersistedToken_IsClearedInsteadOfBreakingTheCircuit(string token)
    {
        var js = new TokenStorage(token);
        var session = new AuthSession();
        var provider = new TokenAuthenticationStateProvider(new BrowserTokenStore(js), session);
        await provider.InitializeAsync();
        var state = await provider.GetAuthenticationStateAsync();
        Assert.False(state.User.Identity?.IsAuthenticated);
        Assert.True(js.Removed);
        Assert.Null(session.AccessToken);
    }

    [Fact]
    public async Task ValidPersistedToken_RestoresIdentityRoleAndSession()
    {
        var token = BuildToken(DateTimeOffset.UtcNow.AddHours(1), "Participant");
        var js = new TokenStorage(token);
        var session = new AuthSession();
        var provider = new TokenAuthenticationStateProvider(new BrowserTokenStore(js), session);
        await provider.InitializeAsync();
        var state = await provider.GetAuthenticationStateAsync();
        Assert.True(state.User.Identity?.IsAuthenticated);
        Assert.True(state.User.IsInRole("Participant"));
        Assert.Equal("user-1", state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.False(js.Removed);
        Assert.Equal(token, session.AccessToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingPersistedToken_StaysAnonymous(string? token)
    {
        var js = new TokenStorage(token!);
        var session = new AuthSession();
        var provider = new TokenAuthenticationStateProvider(new BrowserTokenStore(js), session);
        await provider.InitializeAsync();
        var state = await provider.GetAuthenticationStateAsync();
        Assert.False(state.User.Identity?.IsAuthenticated);
        Assert.Null(session.AccessToken);
    }

    [Fact]
    public async Task ExpiredPersistedToken_IsClearedAndAnonymous()
    {
        var js = new TokenStorage(BuildToken(DateTimeOffset.UtcNow.AddMinutes(-5), "Participant"));
        var session = new AuthSession();
        var provider = new TokenAuthenticationStateProvider(new BrowserTokenStore(js), session);
        await provider.InitializeAsync();
        var state = await provider.GetAuthenticationStateAsync();
        Assert.False(state.User.Identity?.IsAuthenticated);
        Assert.True(js.Removed);
        Assert.Null(session.AccessToken);
    }

    private static string BuildToken(DateTimeOffset expires, string role)
    {
        static string B64(string json) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = $"{{\"sub\":\"user-1\",\"name\":\"Test\",\"role\":\"{role}\",\"exp\":{expires.ToUnixTimeSeconds()}}}";
        return $"{B64("{\"alg\":\"HS256\"}")}.{B64(payload)}.signature";
    }

    private sealed class TokenStorage(string token) : IJSRuntime
    {
        internal bool Removed { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "turningAuthStorage.get") return ValueTask.FromResult((TValue)(object)token);
            if (identifier == "turningAuthStorage.remove") Removed = true;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
