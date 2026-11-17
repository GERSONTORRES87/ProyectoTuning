using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;

namespace Turning.Infrastructure.AI.Emotions;

/// <summary>
/// Configuración del cliente de la API de emociones (<c>services/emotions-api</c>).
/// </summary>
public sealed class EmotionsApiOptions
{
    /// <summary>Sección de configuración.</summary>
    public const string SectionName = "EmotionsApi";

    /// <summary>URL base de la API. Vacía: no se llama y se usa el léxico local.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Token Bearer de la API (<c>API_TOKEN</c> del servicio). Secreto: nunca en appsettings.</summary>
    public string? Token { get; set; }

    /// <summary>Tiempo máximo de la llamada, en milisegundos.</summary>
    public int TimeoutMs { get; set; } = 1500;
}

/// <summary>
/// Clasifica el texto con <c>POST /api/v1/emotions/text</c> de la API de emociones y traduce sus siete
/// clases al vocabulario de SUE. Cualquier fallo devuelve <c>null</c> para que el servicio use el léxico.
/// </summary>
public sealed class EmotionsApiTextClassifier : ITextEmotionClassifierPort
{
    private static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HAPPINESS"] = EmotionLabel.Joy,
            ["SADNESS"] = EmotionLabel.Sadness,
            ["ANGER"] = EmotionLabel.Anger,
            ["SURPRISE"] = EmotionLabel.Surprise,
            ["FEAR"] = EmotionLabel.Fear,
            ["DISGUST"] = EmotionLabel.Disgust,
            ["NEUTRAL"] = EmotionLabel.Neutral
        };

    private readonly HttpClient _httpClient;
    private readonly IOptions<EmotionsApiOptions> _options;
    private readonly ILogger<EmotionsApiTextClassifier> _logger;

    /// <summary>
    /// Constructor del clasificador.
    /// </summary>
    public EmotionsApiTextClassifier(HttpClient httpClient, IOptions<EmotionsApiOptions> options, ILogger<EmotionsApiTextClassifier> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TextEmotionClassification?> ClassifyAsync(string text, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.BaseUrl) || !Uri.TryCreate(EnsureTrailingSlash(options.BaseUrl), UriKind.Absolute, out var baseUri))
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.TimeoutMs);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/v1/emotions/text"))
            {
                Content = JsonContent.Create(new { text, language = "es" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);

            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("La API de emociones respondió {StatusCode}; se usa el léxico local", (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<TextResponse>(timeout.Token);
            if (body?.Emotion is null || !Labels.TryGetValue(body.Emotion, out var emotion))
            {
                _logger.LogWarning("La API de emociones devolvió una clase desconocida ({Emotion}); se usa el léxico local", body?.Emotion);
                return null;
            }

            var scores = (body.Probabilities ?? [])
                .Where(pair => Labels.ContainsKey(pair.Key))
                .ToDictionary(pair => Labels[pair.Key], pair => pair.Value);

            var version = body.ModelVersions?.GetValueOrDefault("text") ?? "emotions-api";
            return new TextEmotionClassification(emotion, body.Confidence, scores, version);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("La API de emociones no respondió en {TimeoutMs} ms; se usa el léxico local", options.TimeoutMs);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            // Solo el tipo: el mensaje de la excepción podría arrastrar la URL o el cuerpo.
            _logger.LogWarning("No se pudo usar la API de emociones ({Error}); se usa el léxico local", ex.GetType().Name);
            return null;
        }
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    private sealed record TextResponse(
        string? Emotion,
        double Confidence,
        Dictionary<string, double>? Probabilities,
        Dictionary<string, string>? ModelVersions);
}
