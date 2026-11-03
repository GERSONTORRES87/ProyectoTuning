using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace turning.Web.Auth;

public sealed class AuthApiClient(HttpClient httpClient, AuthSession authSession)
{
    public Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default) =>
        SendAsync<AuthResultDto>(HttpMethod.Post, "api/auth/login", request, cancellationToken);

    public Task<AuthResultDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default) =>
        SendAsync<AuthResultDto>(HttpMethod.Post, "api/auth/register", request, cancellationToken);

    public Task<AuthenticatedUserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AuthenticatedUserDto>(HttpMethod.Get, "api/auth/me", null, cancellationToken);

    public Task<AuthenticatedUserDto> SetRoleAsync(Guid userId, string role, CancellationToken ct = default) =>
        SendAsync<AuthenticatedUserDto>(HttpMethod.Put, $"api/auth/users/{userId}/role", new { role }, ct);

    public Task<PagedUsersDto> ListUsersAsync(string? query, int page = 1, CancellationToken ct = default) =>
        SendAsync<PagedUsersDto>(HttpMethod.Get, $"api/auth/users?page={page}&pageSize=20&q={Uri.EscapeDataString(query ?? "")}", null, ct);

    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/auth/forgot-password", new { email });
        await ApiResponse.SendWithoutBodyAsync(httpClient, request, ct);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/auth/reset-password", new { token, newPassword });
        await ApiResponse.SendWithoutBodyAsync(httpClient, request, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken ct)
    {
        using var request = CreateRequest(method, uri, body);
        return await ApiResponse.SendAsync<T>(httpClient, request, ct);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri, object? body)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(authSession.AccessToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authSession.AccessToken);
        return request;
    }
}

public sealed class ApiException(string message, HttpStatusCode statusCode, string? details = null) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? Details { get; } = details;
}
