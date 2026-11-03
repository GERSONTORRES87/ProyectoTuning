using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace turning.Web.Auth;

public sealed class TokenAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly BrowserTokenStore _browserTokenStore;
    private readonly AuthSession _authSession;
    private readonly TaskCompletionSource<AuthenticationState> _initializationSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private AuthenticationState _currentState = AnonymousState;
    private bool _isInitialized;

    public TokenAuthenticationStateProvider(BrowserTokenStore browserTokenStore, AuthSession authSession)
    {
        _browserTokenStore = browserTokenStore;
        _authSession = authSession;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_isInitialized)
        {
            return _currentState;
        }

        await InitializeAsync();
        return _currentState;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized)
            {
                return;
            }

            try
            {
                var accessToken = await _browserTokenStore.GetAsync();

                if (string.IsNullOrWhiteSpace(accessToken) || IsTokenExpired(accessToken))
                {
                    await ClearPersistedTokenAsync();
                    SetAuthenticationState(AnonymousState, null);
                    return;
                }

                SetAuthenticationState(CreateAuthenticationState(accessToken), accessToken);
            }
            catch (JSException)
            {
                SetAuthenticationState(AnonymousState, null);
            }
            catch (InvalidOperationException)
            {
                SetAuthenticationState(AnonymousState, null);
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentOutOfRangeException)
            {
                await ClearPersistedTokenAsync();
                SetAuthenticationState(AnonymousState, null);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SignInAsync(AuthResultDto authResult)
    {
        await _browserTokenStore.SetAsync(authResult.AccessToken);
        SetAuthenticationState(CreateAuthenticationState(authResult.AccessToken, authResult.User), authResult.AccessToken);
    }

    public async Task SignOutAsync()
    {
        await ClearPersistedTokenAsync();
        SetAuthenticationState(AnonymousState, null);
    }

    private async Task ClearPersistedTokenAsync()
    {
        try
        {
            await _browserTokenStore.RemoveAsync();
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SetAuthenticationState(AuthenticationState authenticationState, string? accessToken)
    {
        _currentState = authenticationState;
        _authSession.SetAccessToken(accessToken);
        _isInitialized = true;
        _initializationSource.TrySetResult(authenticationState);
        NotifyAuthenticationStateChanged(Task.FromResult(authenticationState));
    }

    private static AuthenticationState CreateAuthenticationState(string accessToken, AuthenticatedUserDto? user = null)
    {
        var claims = ParseClaims(accessToken).ToList();

        if (user is not null)
        {
            AddFallbackClaim(claims, ClaimTypes.NameIdentifier, user.Id.ToString());
            AddFallbackClaim(claims, ClaimTypes.Name, user.FullName);
            AddFallbackClaim(claims, ClaimTypes.Email, user.Email);
            AddFallbackClaim(claims, ClaimTypes.Role, user.Role);
        }

        var identity = claims.Count == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "jwt", ClaimTypes.Name, ClaimTypes.Role);

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private static void AddFallbackClaim(ICollection<Claim> claims, string claimType, string value)
    {
        if (!claims.Any(existing => existing.Type == claimType))
        {
            claims.Add(new Claim(claimType, value));
        }
    }

    private static bool IsTokenExpired(string accessToken)
    {
        var expClaim = ParseClaims(accessToken).FirstOrDefault(claim => claim.Type == "exp")?.Value;

        return !long.TryParse(expClaim, out var expValue)
            || DateTimeOffset.UtcNow >= DateTimeOffset.FromUnixTimeSeconds(expValue);
    }

    private static IEnumerable<Claim> ParseClaims(string accessToken)
    {
        var tokenParts = accessToken.Split('.');

        if (tokenParts.Length < 2)
        {
            return Array.Empty<Claim>();
        }

        var payloadBytes = DecodeBase64(tokenParts[1]);
        using var payloadDocument = JsonDocument.Parse(payloadBytes);
        var claims = new List<Claim>();

        foreach (var property in payloadDocument.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    claims.Add(MapClaim(property.Name, item.ToString()));
                }

                continue;
            }

            claims.Add(MapClaim(property.Name, property.Value.ToString()));
        }

        return claims;
    }

    private static Claim MapClaim(string claimType, string? claimValue)
    {
        var value = claimValue ?? string.Empty;

        return claimType switch
        {
            "sub" => new Claim(ClaimTypes.NameIdentifier, value),
            "name" or "unique_name" => new Claim(ClaimTypes.Name, value),
            "email" => new Claim(ClaimTypes.Email, value),
            "role" => new Claim(ClaimTypes.Role, value),
            _ => new Claim(claimType, value)
        };
    }

    private static byte[] DecodeBase64(string payload)
    {
        var base64 = payload.Replace('-', '+').Replace('_', '/');

        switch (base64.Length % 4)
        {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        return Convert.FromBase64String(base64);
    }
}
