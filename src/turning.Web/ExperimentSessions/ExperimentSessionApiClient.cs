using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using turning.Web.Auth;

namespace turning.Web.ExperimentSessions;

public sealed class ExperimentSessionApiClient(HttpClient httpClient, AuthSession authSession)
{
    public async Task<ExperimentSessionSnapshotDto?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        try { return await SendAsync<ExperimentSessionSnapshotDto>(HttpMethod.Get, "api/experiment-sessions/latest", null, cancellationToken); }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    public Task<ExperimentSessionSnapshotDto> CreateBootstrapAsync(CreateExperimentSessionRequestDto request, CancellationToken cancellationToken = default) =>
        SendAsync<ExperimentSessionSnapshotDto>(HttpMethod.Post, "api/experiment-sessions/bootstrap", request, cancellationToken);

    public Task<ExperimentSessionSnapshotDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<ExperimentSessionSnapshotDto>(HttpMethod.Get, $"api/sessions/{id}", null, ct);

    public Task<ParticipantSessionsDto> ListOwnAsync(Guid userId, int page = 1, CancellationToken ct = default) =>
        SendAsync<ParticipantSessionsDto>(HttpMethod.Get, $"api/sessions/participant/{userId}?page={page}&pageSize=20", null, ct);

    public Task<PagedSessionsDto> ListAwaitingAsync(int page = 1, CancellationToken ct = default) =>
        SendAsync<PagedSessionsDto>(HttpMethod.Get, $"api/sessions/awaiting-interlocutor?page={page}&pageSize=20", null, ct);

    public Task<ExperimentSessionSnapshotDto> JoinAsync(string sessionCode, CancellationToken ct = default) =>
        SendAsync<ExperimentSessionSnapshotDto>(HttpMethod.Post, "api/sessions/join", new { sessionCode }, ct);

    public Task<ExperimentSessionSnapshotDto> AssignAsync(Guid id, Guid interlocutorUserId, CancellationToken ct = default) =>
        SendAsync<ExperimentSessionSnapshotDto>(HttpMethod.Post, $"api/sessions/{id}/interlocutor", new { interlocutorUserId }, ct);

    public Task<ExperimentSessionSnapshotDto> ActivateAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<ExperimentSessionSnapshotDto>(HttpMethod.Post, $"api/sessions/{id}/activate", null, ct);

    public Task<ExperimentSessionSnapshotDto> CompleteAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<ExperimentSessionSnapshotDto>(HttpMethod.Post, $"api/sessions/{id}/complete", null, ct);

    public Task<ExperimentSessionSnapshotDto> CancelAsync(Guid id, string reason, CancellationToken ct = default) =>
        SendAsync<ExperimentSessionSnapshotDto>(HttpMethod.Post, $"api/sessions/{id}/cancel", new { reason }, ct);

    public async Task<IReadOnlyList<ConversationTurnSnapshotDto>> GetConversationTurnsAsync(Guid id, CancellationToken cancellationToken = default, int? afterSequence = null) =>
        await SendAsync<ConversationTurnSnapshotDto[]>(HttpMethod.Get, $"api/experiment-sessions/{id}/conversation-turns" + (afterSequence.HasValue ? $"?afterSequence={afterSequence.Value}&limit=200" : ""), null, cancellationToken);

    public Task<ConversationTurnSnapshotDto> AddConversationTurnAsync(Guid id, AddConversationTurnRequestDto request, CancellationToken cancellationToken = default) =>
        SendAsync<ConversationTurnSnapshotDto>(HttpMethod.Post, $"api/experiment-sessions/{id}/conversation-turns", request, cancellationToken);

    public Task<SurveyDefinitionDto> GetSurveyAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<SurveyDefinitionDto>(HttpMethod.Get, $"api/sessions/{id}/survey", null, ct);

    public async Task SubmitSurveyAsync(Guid id, SurveySubmitRequestDto request, CancellationToken ct = default) =>
        _ = await SendAsync<System.Text.Json.JsonElement>(HttpMethod.Post, $"api/sessions/{id}/survey/responses", request, ct);

    public Task<SessionResultDto> GetResultAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<SessionResultDto>(HttpMethod.Get, $"api/sessions/{id}/results", null, ct);

    public Task<PagedSessionsDto> ListResultsAsync(int page = 1, CancellationToken ct = default) =>
        SendAsync<PagedSessionsDto>(HttpMethod.Get, $"api/results?page={page}&pageSize=20", null, ct);

    public Task<EmotionDto[]> GetEmotionsAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<EmotionDto[]>(HttpMethod.Get, $"api/sessions/{id}/emotions", null, ct);

    public async Task AddEmotionAsync(Guid id, string emotion, double score, CancellationToken ct = default) =>
        _ = await SendAsync<System.Text.Json.JsonElement>(HttpMethod.Post, $"api/sessions/{id}/emotions", new { emotion, score, source = "self-report" }, ct);

    public Task<AvatarDto> GetAvatarAsync(Guid id, CancellationToken ct = default) =>
        SendAsync<AvatarDto>(HttpMethod.Get, $"api/sessions/{id}/avatar/current", null, ct);

    public Task<PagedSessionsDto> ListSessionsAsync(int pageSize = 100, CancellationToken ct = default) =>
        SendAsync<PagedSessionsDto>(HttpMethod.Get, $"api/results?page=1&pageSize={pageSize}", null, ct);

    public async Task<SessionListDto> ListAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        const int pageSize = 100;
        var items = new List<ExperimentSessionSnapshotDto>();
        var total = 0;

        for (var page = 1; ; page++)
        {
            var result = await SendAsync<SessionListDto>(HttpMethod.Get,
                $"api/results?page={page}&pageSize={pageSize}", null, cancellationToken);
            total = result.Total;
            items.AddRange(result.Items);
            if (result.Items.Count == 0 || items.Count >= total) break;
        }

        return new SessionListDto { Items = items.DistinctBy(session => session.Id).ToList(), Total = total };
    }

    public Task<PagedSessionsDto> ListAwaitingInterlocutorAsync(CancellationToken ct = default) =>
        SendAsync<PagedSessionsDto>(HttpMethod.Get, "api/sessions/awaiting-interlocutor?page=1&pageSize=100", null, ct);

    public Task<ExperimentSessionSnapshotDto> JoinAsInterlocutorAsync(string sessionCode, CancellationToken ct = default) =>
        JoinAsync(sessionCode, ct);

    public Task<SessionResultDto> GetSessionResultAsync(Guid id, CancellationToken ct = default) =>
        GetResultAsync(id, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(authSession.AccessToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authSession.AccessToken);
        return await ApiResponse.SendAsync<T>(httpClient, request, ct);
    }
}
