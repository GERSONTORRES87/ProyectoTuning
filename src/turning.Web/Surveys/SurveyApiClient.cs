using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using turning.Web.Auth;

namespace turning.Web.Surveys;

public sealed class SurveyApiClient(HttpClient httpClient, AuthSession authSession)
{
    public async Task<IReadOnlyList<AdminSurveyResultDto>> GetAdminResultsAsync(CancellationToken cancellationToken = default)
    {
        const int pageSize = 50;
        var results = new List<AdminSurveyResultDto>();
        for (var page = 1; ; page++)
        {
            var result = await ReadPageAsync($"api/surveys/results?page={page}&pageSize={pageSize}", cancellationToken);
            if (result.Items is null)
                throw new ApiException("El servicio devolvió una página de resultados de encuestas incompleta.", HttpStatusCode.BadGateway);
            results.AddRange(result.Items);
            if (result.Items.Count == 0 || results.Count >= result.Total) break;
        }

        return results.DistinctBy(result => result.ResponseId).ToArray();
    }

    private async Task<AdminSurveyResultsPage> ReadPageAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(authSession.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authSession.AccessToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) throw await ReadErrorAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<AdminSurveyResultsPage>(cancellationToken: cancellationToken)
                ?? throw new ApiException("El servicio de resultados de encuestas devolvió una respuesta vacía.", HttpStatusCode.BadGateway);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("No fue posible conectar con el servicio de resultados de encuestas. Intenta de nuevo en unos momentos.", HttpStatusCode.ServiceUnavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException("El servicio de resultados de encuestas tardó demasiado en responder. Intenta actualizar de nuevo.", HttpStatusCode.GatewayTimeout);
        }
        catch (JsonException)
        {
            throw new ApiException("No fue posible leer los resultados de las encuestas.", HttpStatusCode.BadGateway);
        }
    }

    private static async Task<ApiException> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        SurveyApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<SurveyApiError>(cancellationToken: cancellationToken); }
        catch (JsonException) { }
        var code = error?.Error ?? error?.Code;
        var message = !string.IsNullOrWhiteSpace(error?.Message) ? error.Message : response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Tu sesión expiró. Inicia sesión nuevamente.",
            HttpStatusCode.Forbidden => "Tu cuenta no tiene permiso para consultar los resultados de las encuestas.",
            HttpStatusCode.BadRequest => "No fue posible cargar la página de resultados de encuestas solicitada.",
            _ => $"No fue posible consultar los resultados de las encuestas ({(int)response.StatusCode})."
        };
        return new ApiException(message, response.StatusCode, error?.Details ?? code);
    }

    private sealed class AdminSurveyResultsPage
    {
        public List<AdminSurveyResultDto>? Items { get; init; }
        public int Total { get; init; }
    }

    private sealed class SurveyApiError
    {
        public string? Error { get; init; }
        public string? Code { get; init; }
        public string? Message { get; init; }
        public string? Details { get; init; }
    }
}
