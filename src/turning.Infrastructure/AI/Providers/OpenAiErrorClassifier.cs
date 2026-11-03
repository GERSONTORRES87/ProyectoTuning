using System.Net;
using System.Text.Json;

namespace Turning.Infrastructure.AI.Providers;

/// <summary>
/// Traduce la respuesta de error de un proveedor compatible con OpenAI a una decisión:
/// reintentar, saltar al siguiente o rendirse.
/// </summary>
/// <remarks>
/// El caso que justifica esta clase: <b>OpenAI devuelve 429 tanto cuando vas demasiado
/// rápido como cuando se acabó el saldo</b>, y la diferencia solo está en el cuerpo
/// (<c>error.type == "insufficient_quota"</c>). Sin distinguirlas, una cuenta sin crédito se
/// come el presupuesto entero reintentando y nunca se llega al proveedor gratuito, que es
/// justo lo que la cadena existe para evitar.
/// </remarks>
public static class OpenAiErrorClassifier
{
    /// <summary>
    /// Marcadores que el proveedor usa para indicar que no queda crédito.
    /// </summary>
    private static readonly string[] QuotaMarkers =
    [
        "insufficient_quota",
        "exceeded your current quota",
        "billing_hard_limit_reached",
        "credit",
        "payment required"
    ];

    /// <summary>
    /// Marcadores de peticiones rechazadas por longitud de contexto.
    /// </summary>
    private static readonly string[] ContextMarkers =
    [
        "context_length_exceeded",
        "maximum context length"
    ];

    /// <summary>
    /// Clasifica una respuesta de error.
    /// </summary>
    /// <param name="status">Código HTTP devuelto.</param>
    /// <param name="body">Cuerpo de la respuesta, si se pudo leer.</param>
    public static ProviderFailureKind Classify(HttpStatusCode status, string? body)
    {
        var normalized = (body ?? string.Empty).ToLowerInvariant();
        var errorType = ExtractErrorType(body);

        return status switch
        {
            HttpStatusCode.Unauthorized => ProviderFailureKind.Credentials,
            HttpStatusCode.PaymentRequired => ProviderFailureKind.Quota,
            HttpStatusCode.Forbidden => ProviderFailureKind.Permanent,
            HttpStatusCode.NotFound => ProviderFailureKind.Permanent,

            // El 429 es ambiguo: hay que mirar el cuerpo para saber si es ritmo o saldo.
            HttpStatusCode.TooManyRequests when ContainsAny(normalized, QuotaMarkers)
                || string.Equals(errorType, "insufficient_quota", StringComparison.OrdinalIgnoreCase)
                => ProviderFailureKind.Quota,
            HttpStatusCode.TooManyRequests => ProviderFailureKind.RateLimited,

            HttpStatusCode.BadRequest when ContainsAny(normalized, ContextMarkers) => ProviderFailureKind.Permanent,
            HttpStatusCode.BadRequest when normalized.Contains("content_policy", StringComparison.Ordinal)
                => ProviderFailureKind.ContentPolicy,
            HttpStatusCode.BadRequest => ProviderFailureKind.Permanent,

            HttpStatusCode.RequestTimeout => ProviderFailureKind.Timeout,
            HttpStatusCode.InternalServerError => ProviderFailureKind.Transient,
            HttpStatusCode.BadGateway => ProviderFailureKind.Transient,
            HttpStatusCode.ServiceUnavailable => ProviderFailureKind.Transient,
            HttpStatusCode.GatewayTimeout => ProviderFailureKind.Transient,

            _ when (int)status >= 500 => ProviderFailureKind.Transient,
            _ => ProviderFailureKind.Permanent
        };
    }

    /// <summary>
    /// Indica si el fallo justifica reintentar contra el mismo proveedor.
    /// </summary>
    public static bool IsRetryable(ProviderFailureKind kind) =>
        kind is ProviderFailureKind.Transient or ProviderFailureKind.Timeout;

    private static bool ContainsAny(string haystack, string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.Ordinal));

    private static string? ExtractErrorType(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                    return type.GetString();

                // OpenAI manda "code" como texto; OpenRouter lo manda como número (429).
                if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                    return code.GetString();
            }
        }
        catch (JsonException)
        {
            // Un cuerpo que no es JSON no impide clasificar por código de estado.
        }

        return null;
    }
}
