using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using turning.Web.Auth;

namespace turning.Web.ExperimentSessions;

public sealed class ExperimentSessionApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthSession _authSession;

    public ExperimentSessionApiClient(HttpClient httpClient, AuthSession authSession)
    {
        _httpClient = httpClient;
        _authSession = authSession;
    }

    public async Task<ExperimentSessionSnapshotDto?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/experiment-sessions/latest");
        AttachAccessToken(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadResponseAsync<ExperimentSessionSnapshotDto>(response, cancellationToken);
    }

    public async Task<ExperimentSessionSnapshotDto> CreateBootstrapAsync(CreateExperimentSessionRequestDto requestDto, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/experiment-sessions/bootstrap")
        {
            Content = JsonContent.Create(requestDto)
        };

        AttachAccessToken(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<ExperimentSessionSnapshotDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationTurnSnapshotDto>> GetConversationTurnsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/experiment-sessions/{sessionId}/conversation-turns");
        AttachAccessToken(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await ReadResponseAsync<ConversationTurnSnapshotDto[]>(response, cancellationToken);
        return result;
    }

    public async Task<ConversationTurnSnapshotDto> AddConversationTurnAsync(Guid sessionId, AddConversationTurnRequestDto requestDto, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/experiment-sessions/{sessionId}/conversation-turns")
        {
            Content = JsonContent.Create(requestDto)
        };

        AttachAccessToken(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<ConversationTurnSnapshotDto>(response, cancellationToken);
    }

    public async Task<SessionListDto> ListSessionsAsync(int pageSize = 100, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/results?pageSize={pageSize}");
        AttachAccessToken(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<SessionListDto>(response, cancellationToken);
    }

    public async Task<SessionListDto> ListAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        const int pageSize = 100;
        var items = new List<ExperimentSessionSnapshotDto>();
        var total = 0;

        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/results?page={page}&pageSize={pageSize}");
            AttachAccessToken(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var result = await ReadResponseAsync<SessionListDto>(response, cancellationToken);
            total = result.Total;
            items.AddRange(result.Items);

            if (result.Items.Count == 0 || items.Count >= total)
            {
                break;
            }
        }

        return new SessionListDto { Items = items.DistinctBy(session => session.Id).ToList(), Total = total };
    }

    public async Task<SessionListDto> ListAwaitingInterlocutorAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/sessions/awaiting-interlocutor");
        AttachAccessToken(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<SessionListDto>(response, cancellationToken);
    }

    public async Task<ExperimentSessionSnapshotDto> JoinAsInterlocutorAsync(string sessionCode, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sessions/join")
        {
            Content = JsonContent.Create(new { sessionCode })
        };

        AttachAccessToken(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<ExperimentSessionSnapshotDto>(response, cancellationToken);
    }

    public async Task<SessionResultDto> GetSessionResultAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/sessions/{sessionId}/results");
        AttachAccessToken(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<SessionResultDto>(response, cancellationToken);
    }

    private void AttachAccessToken(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_authSession.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authSession.AccessToken);
        }
    }

    private static async Task<TResponse> ReadResponseAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
            return result ?? throw new ApiException("La API devolvio una respuesta vacia.", response.StatusCode);
        }

        ApiErrorResponse? apiError = null;

        try
        {
            apiError = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
        }
        catch
        {
        }

        throw new ApiException(
            apiError?.Message ?? $"No fue posible completar la solicitud ({(int)response.StatusCode}).",
            response.StatusCode,
            apiError?.Details);
    }
}
