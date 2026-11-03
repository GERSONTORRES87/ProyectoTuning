using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace turning.Web.Auth;

internal static class ApiResponse
{
    internal static Task<T> SendAsync<T>(HttpClient client, HttpRequestMessage request, CancellationToken ct) =>
        SendAsync(client, request, ct, async response =>
            await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                ?? throw new ApiException("La API devolvió una respuesta vacía.", response.StatusCode));

    // Para respuestas 202/204 cuyo cuerpo no interesa: solo importa que la petición tuvo éxito.
    internal static Task SendWithoutBodyAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct) =>
        SendAsync(client, request, ct, _ => Task.FromResult(true));

    private static async Task<T> SendAsync<T>(HttpClient client, HttpRequestMessage request, CancellationToken ct, Func<HttpResponseMessage, Task<T>> read)
    {
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return await read(response);

            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Tu sesión ha vencido. Inicia sesión nuevamente.",
                HttpStatusCode.Forbidden => "Tu rol no permite realizar esta acción.",
                HttpStatusCode.NotFound => "No se encontró la sesión o no tienes acceso.",
                _ => $"No se pudo completar la solicitud ({(int)response.StatusCode})."
            };
            try
            {
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = data.RootElement;
                foreach (var key in root.ValueKind == JsonValueKind.Object ? new[] { "message", "detail", "error", "title" } : Array.Empty<string>())
                    if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    { message = value.GetString()!; break; }
            }
            catch (JsonException) { }
            throw new ApiException(message, response.StatusCode);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("No se pudo conectar con la API. Revisa que esté disponible e inténtalo de nuevo.", HttpStatusCode.ServiceUnavailable);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException("La API tardó demasiado. Comprueba la sesión antes de volver a enviar.", HttpStatusCode.RequestTimeout);
        }
        catch (JsonException)
        {
            throw new ApiException("La API devolvió una respuesta que no se pudo leer.", HttpStatusCode.BadGateway);
        }
    }
}
