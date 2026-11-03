using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Turning.Application.Features.AI;
using Turning.Application.Interfaces;

namespace Turning.Infrastructure.AI.Providers;

/// <summary>
/// Proveedor de generación de texto contra cualquier API compatible con el endpoint
/// <c>/chat/completions</c> de OpenAI.
/// </summary>
/// <remarks>
/// Una sola clase cubre OpenAI y OpenRouter porque comparten el protocolo: lo que cambia
/// —URL base, modelo, clave y un par de cabeceras de atribución— está en las opciones. Dos
/// clases separadas serían el mismo mapeo de errores escrito dos veces, que es justo la
/// parte con lógica y la que más cuesta mantener.
/// </remarks>
public sealed class OpenAiCompatibleTextGenerationProvider : ITextGenerationProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Los parámetros opcionales que valen null no se envían: OpenAI rechaza los que no conoce.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;
    private readonly string _systemPrompt;
    private readonly int _maxRetries;
    private readonly ILogger _logger;

    /// <summary>
    /// Constructor del proveedor.
    /// </summary>
    public OpenAiCompatibleTextGenerationProvider(
        HttpClient httpClient,
        AiProviderOptions options,
        string systemPrompt,
        int maxRetries,
        ILogger logger)
    {
        _httpClient = httpClient;
        _options = options;
        _systemPrompt = systemPrompt;
        _maxRetries = Math.Max(1, maxRetries);
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => _options.Name;

    /// <inheritdoc />
    public bool IsConfigured => _options.IsUsable;

    /// <inheritdoc />
    public async Task<ProviderReply> GenerateAsync(TextGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;
        var lastFailure = ProviderFailureKind.Transient;
        string? lastDetail = null;

        while (attempt < _maxRetries)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.RequestTimeoutMs);

                using var httpRequest = BuildRequest(request);
                using var response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var (text, model) = ParseCompletion(body);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        // Un 200 sin texto utilizable no es un éxito: se trata como fallo
                        // definitivo para no devolver un turno vacío a la conversación.
                        stopwatch.Stop();
                        return ProviderReply.Failed(
                            ProviderFailureKind.Permanent,
                            "El proveedor respondió sin texto.",
                            stopwatch.ElapsedMilliseconds,
                            attempt);
                    }

                    stopwatch.Stop();
                    return ProviderReply.Ok(text!, model, stopwatch.ElapsedMilliseconds, attempt);
                }

                lastFailure = OpenAiErrorClassifier.Classify(response.StatusCode, body);
                lastDetail = $"HTTP {(int)response.StatusCode}";

                _logger.LogWarning(
                    "El proveedor {Provider} respondió {StatusCode}, clasificado como {Failure} (intento {Attempt})",
                    Name, (int)response.StatusCode, lastFailure, attempt);

                if (!OpenAiErrorClassifier.IsRetryable(lastFailure))
                    break;

                await WaitBeforeRetryAsync(response.Headers.RetryAfter, attempt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancelación del llamante: no es un fallo del proveedor.
                throw;
            }
            catch (OperationCanceledException)
            {
                lastFailure = ProviderFailureKind.Timeout;
                lastDetail = $"Se agotaron los {_options.RequestTimeoutMs} ms del intento.";
                _logger.LogWarning("El proveedor {Provider} agotó el tiempo (intento {Attempt})", Name, attempt);
            }
            catch (HttpRequestException ex)
            {
                lastFailure = ProviderFailureKind.Transient;
                lastDetail = ex.Message;
                _logger.LogWarning(ex, "Fallo de red con el proveedor {Provider} (intento {Attempt})", Name, attempt);
            }
            catch (JsonException ex)
            {
                lastFailure = ProviderFailureKind.Permanent;
                lastDetail = "La respuesta del proveedor no es JSON válido.";
                _logger.LogError(ex, "Respuesta ilegible del proveedor {Provider}", Name);
                break;
            }

            if (!OpenAiErrorClassifier.IsRetryable(lastFailure))
                break;
        }

        stopwatch.Stop();
        return ProviderReply.Failed(lastFailure, lastDetail, stopwatch.ElapsedMilliseconds, attempt);
    }

    private HttpRequestMessage BuildRequest(TextGenerationRequest request)
    {
        var messages = new List<object>
        {
            new { role = ChatMessage.RoleSystem, content = BuildSystemPrompt(request) }
        };

        messages.AddRange(request.ConversationHistory.Select(message => new
        {
            role = message.Role,
            content = message.Content
        }));

        messages.Add(new { role = ChatMessage.RoleUser, content = request.UserInput });

        var payload = new
        {
            model = _options.Model,
            messages,
            max_tokens = _options.MaxOutputTokens,
            temperature = _options.Temperature,
            reasoning = _options.DisableReasoning ? new { enabled = false } : null
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, SerializerOptions),
                Encoding.UTF8,
                "application/json")
        };

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        // Cabeceras de atribución de OpenRouter; OpenAI las ignora.
        if (!string.IsNullOrWhiteSpace(_options.HttpReferer))
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", _options.HttpReferer);

        if (!string.IsNullOrWhiteSpace(_options.Title))
            httpRequest.Headers.TryAddWithoutValidation("X-Title", _options.Title);

        return httpRequest;
    }

    /// <summary>
    /// Añade el contexto emocional a la instrucción de sistema (RF-EXP-01).
    /// </summary>
    private string BuildSystemPrompt(TextGenerationRequest request)
    {
        if (request.EmotionContext is null)
            return _systemPrompt;

        var emotion = request.EmotionContext;
        return $"{_systemPrompt} El último mensaje del participante transmite {emotion.EmotionId} " +
               $"con intensidad {emotion.Intensity:0.00}. Ten en cuenta ese estado emocional al responder.";
    }

    private static (string? Text, string? Model) ParseCompletion(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : null;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return (null, model);

        var message = choices[0].TryGetProperty("message", out var messageElement) ? messageElement : default;

        var text = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;

        return (text, model);
    }

    private async Task WaitBeforeRetryAsync(RetryConditionHeaderValue? retryAfter, int attempt, CancellationToken cancellationToken)
    {
        // Backoff exponencial con algo de dispersión, para no sincronizar reintentos.
        var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 100));

        if (retryAfter?.Delta is { } serverDelay && serverDelay > delay)
            delay = serverDelay;

        // Nunca esperar más que el propio intento: si no, un Retry-After generoso se
        // comería el presupuesto que le toca al siguiente proveedor.
        var cap = TimeSpan.FromMilliseconds(_options.RequestTimeoutMs);
        if (delay > cap)
            delay = cap;

        await Task.Delay(delay, cancellationToken);
    }
}
