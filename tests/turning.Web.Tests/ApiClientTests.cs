using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using turning.Web.Auth;
using turning.Web.ExperimentSessions;
using Xunit;

namespace turning.Web.Tests;

public class ApiClientTests
{
    private static (ExperimentSessionApiClient Client, RecordingHandler Handler) Create(HttpStatusCode status, string json)
    {
        var handler = new RecordingHandler(status, json);
        var auth = new AuthSession();
        auth.SetAccessToken("test-token");
        return (new(new HttpClient(handler) { BaseAddress = new("http://localhost/") }, auth), handler);
    }

    [Fact]
    public async Task Create_DoesNotForceAnInvalidOrPreferredCondition()
    {
        var (client, handler) = Create(HttpStatusCode.OK, "{}");
        await client.CreateBootstrapAsync(new());
        Assert.Equal("/api/experiment-sessions/bootstrap", handler.Uri!.AbsolutePath);
        Assert.Equal("{}", handler.Body);
        Assert.Equal("Bearer test-token", handler.Authorization);
    }

    [Fact]
    public async Task SendTurn_LeavesSenderToTheAuthenticatedIdentity()
    {
        var (client, handler) = Create(HttpStatusCode.OK, "{}");
        var id = Guid.NewGuid();
        await client.AddConversationTurnAsync(id, new() { Message = "Hola" });
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.False(body.RootElement.TryGetProperty("sender", out _));
        Assert.Equal("Hola", body.RootElement.GetProperty("message").GetString());
        Assert.Contains(id.ToString(), handler.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task Poll_UsesTheExplicitSessionAndLastKnownSequence()
    {
        var (client, handler) = Create(HttpStatusCode.OK, "[]");
        var id = Guid.NewGuid();
        await client.GetConversationTurnsAsync(id, afterSequence: 42);
        Assert.Equal($"/api/experiment-sessions/{id}/conversation-turns", handler.Uri!.AbsolutePath);
        Assert.Equal("?afterSequence=42&limit=200", handler.Uri.Query);
    }

    [Fact]
    public async Task Survey_SendsOneDefinitiveRequestWithAnswers()
    {
        var (client, handler) = Create(HttpStatusCode.Created, "{\"responseId\":\"00000000-0000-0000-0000-000000000001\"}");
        var sessionId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        await client.SubmitSurveyAsync(sessionId, new(definitionId, new() { ["empathy"] = "4", ["perceived_interlocutor"] = "Unsure" }));
        Assert.Equal(1, handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/sessions/{sessionId}/survey/responses", handler.Uri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(definitionId, body.RootElement.GetProperty("surveyDefinitionId").GetGuid());
        Assert.Equal("4", body.RootElement.GetProperty("answers").GetProperty("empathy").GetString());
    }

    [Theory]
    [InlineData("{\"error\":\"SESSION_NOT_FOUND\"}", "SESSION_NOT_FOUND")]
    [InlineData("{\"error\":\"CONFLICT\",\"message\":\"La sesión terminó.\"}", "La sesión terminó.")]
    public async Task Errors_KeepTheApiReasonVisible(string json, string expected)
    {
        var (client, _) = Create(HttpStatusCode.Conflict, json);
        var error = await Assert.ThrowsAsync<ApiException>(() => client.GetByIdAsync(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public async Task UnavailableApi_ReturnsARecoverableUserError()
    {
        var auth = new AuthSession();
        var client = new ExperimentSessionApiClient(new HttpClient(new UnavailableHandler()) { BaseAddress = new("http://localhost/") }, auth);
        var error = await Assert.ThrowsAsync<ApiException>(() => client.GetLatestAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }

    [Fact]
    public async Task Registration_DefaultsToParticipant()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"accessToken\":\"token\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"user\":{\"id\":\"00000000-0000-0000-0000-000000000001\",\"fullName\":\"Ana\",\"email\":\"ana@test.test\",\"role\":\"Participant\"}}");
        var client = new AuthApiClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }, new());
        await client.RegisterAsync(new() { FullName = "Ana", Email = "ana@test.test", Password = "Password123!" });
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Participant", body.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task AdminRoleChange_UsesAuthenticatedPut()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler(HttpStatusCode.OK, $"{{\"id\":\"{id}\",\"fullName\":\"Ana\",\"email\":\"ana@test.test\",\"role\":\"Researcher\"}}");
        var auth = new AuthSession(); auth.SetAccessToken("admin-token");
        var client = new AuthApiClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }, auth);
        await client.SetRoleAsync(id, "Researcher");
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal($"/api/auth/users/{id}/role", handler.Uri!.AbsolutePath);
        Assert.Equal("Bearer admin-token", handler.Authorization);
    }

    [Fact]
    public async Task AdminUserList_UsesAuthenticatedGetWithTheSearch()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            "{\"items\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"fullName\":\"Ana\",\"email\":\"ana@test.test\",\"role\":\"Participant\"}],\"total\":1,\"page\":2,\"pageSize\":20}");
        var auth = new AuthSession(); auth.SetAccessToken("admin-token");
        var client = new AuthApiClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }, auth);

        var result = await client.ListUsersAsync("ana lópez", 2);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/api/auth/users", handler.Uri!.AbsolutePath);
        Assert.Contains("page=2", handler.Uri.Query);
        Assert.Contains("q=ana%20l%C3%B3pez", handler.Uri.Query);
        Assert.Equal("Bearer admin-token", handler.Authorization);
        Assert.Equal("Participant", Assert.Single(result.Items).Role);
    }

    [Fact]
    public async Task PasswordReset_PostsTheRequestsWithoutReadingABody()
    {
        var forgot = new RecordingHandler(HttpStatusCode.Accepted, "{}");
        await new AuthApiClient(new HttpClient(forgot) { BaseAddress = new("http://localhost/") }, new()).ForgotPasswordAsync("ana@test.test");
        Assert.Equal(HttpMethod.Post, forgot.Method);
        Assert.Equal("/api/auth/forgot-password", forgot.Uri!.AbsolutePath);
        Assert.Equal("ana@test.test", JsonDocument.Parse(forgot.Body!).RootElement.GetProperty("email").GetString());
        Assert.Null(forgot.Authorization);

        var reset = new RecordingHandler(HttpStatusCode.NoContent, "");
        await new AuthApiClient(new HttpClient(reset) { BaseAddress = new("http://localhost/") }, new()).ResetPasswordAsync("tok", "ClaveNueva123");
        Assert.Equal("/api/auth/reset-password", reset.Uri!.AbsolutePath);
        using var body = JsonDocument.Parse(reset.Body!);
        Assert.Equal("tok", body.RootElement.GetProperty("token").GetString());
        Assert.Equal("ClaveNueva123", body.RootElement.GetProperty("newPassword").GetString());
    }

    [Fact]
    public async Task PasswordReset_InvalidTokenKeepsTheApiMessage()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest,
            "{\"error\":\"AUTH_RESET_TOKEN_INVALID\",\"code\":\"AUTH_RESET_TOKEN_INVALID\",\"message\":\"El enlace de restablecimiento no es válido o ya venció. Pide uno nuevo.\"}");
        var client = new AuthApiClient(new HttpClient(handler) { BaseAddress = new("http://localhost/") }, new());

        var error = await Assert.ThrowsAsync<ApiException>(() => client.ResetPasswordAsync("tok", "ClaveNueva123"));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Contains("no es válido", error.Message);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        internal Uri? Uri { get; private set; }
        internal HttpMethod? Method { get; private set; }
        internal string? Body { get; private set; }
        internal string? Authorization { get; private set; }
        internal int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri; Method = request.Method; Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests++;
            return new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }
    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("offline");
    }
}
