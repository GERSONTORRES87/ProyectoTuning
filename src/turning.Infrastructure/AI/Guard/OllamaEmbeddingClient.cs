using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Turning.Infrastructure.AI.Guard;

/// <summary>
/// Configuración del filtro previo al modelo de lenguaje.
/// </summary>
public sealed class GuardOptions
{
    /// <summary>Sección de configuración.</summary>
    public const string SectionName = "Guard";

    /// <summary>URL base de Ollama (<c>sue-llm</c>). Vacía: el filtro trabaja solo con palabras clave.</summary>
    public string? EmbeddingsBaseUrl { get; set; }

    /// <summary>Modelo de embeddings multilingüe.</summary>
    public string EmbeddingModel { get; set; } = "paraphrase-multilingual";

    /// <summary>Tiempo máximo para embeber el mensaje de un turno, en milisegundos.</summary>
    public int TimeoutMs { get; set; } = 800;

    /// <summary>Tiempo máximo para embeber toda la base al construir el índice, en milisegundos.</summary>
    public int IndexTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Similitud coseno desde la que una regla bloquea. Calibrado con paraphrase-multilingual sobre 79 frases:
    /// las cotidianas que no deben bloquear llegan a 0.758 («me quiero ir de esta ciudad») y las que deben
    /// bloquear sin palabra clave empiezan en 0.832, así que el corte va en medio.
    /// </summary>
    public double BlockThreshold { get; set; } = 0.80;

    /// <summary>Similitud coseno desde la que una pauta por tema entra al prompt (calibrado: aciertos ≥ 0.68).</summary>
    public double GuideThreshold { get; set; } = 0.65;

    /// <summary>Máximo de pautas por tema por turno.</summary>
    public int MaxGuides { get; set; } = 2;
}

/// <summary>Calcula embeddings de textos.</summary>
public interface IEmbeddingClient
{
    /// <summary>Indica si hay un servicio de embeddings configurado.</summary>
    bool IsConfigured { get; }

    /// <summary>Devuelve un vector por texto, en el mismo orden. Lanza si el servicio falla o no responde a tiempo.</summary>
    Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, int timeoutMs, CancellationToken cancellationToken);
}

/// <summary>
/// Embeddings con <c>POST /api/embed</c> de Ollama.
/// </summary>
public sealed class OllamaEmbeddingClient : IEmbeddingClient
{
    /// <summary>Nombre del cliente HTTP registrado.</summary>
    public const string ClientName = "guard-embeddings";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<GuardOptions> _options;

    /// <summary>
    /// Constructor del cliente de embeddings.
    /// </summary>
    public OllamaEmbeddingClient(IHttpClientFactory httpClientFactory, IOptions<GuardOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Value.EmbeddingsBaseUrl);

    /// <inheritdoc />
    public async Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, int timeoutMs, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var baseUrl = options.EmbeddingsBaseUrl!.EndsWith('/') ? options.EmbeddingsBaseUrl : options.EmbeddingsBaseUrl + "/";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        var client = _httpClientFactory.CreateClient(ClientName);
        using var response = await client.PostAsJsonAsync(new Uri(new Uri(baseUrl), "api/embed"),
            new { model = options.EmbeddingModel, input = inputs }, timeout.Token);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<EmbedResponse>(timeout.Token);
        if (body?.Embeddings is not { } embeddings || embeddings.Length != inputs.Count)
            throw new InvalidOperationException("Ollama devolvió un número de embeddings distinto al pedido.");

        return embeddings;
    }

    private sealed record EmbedResponse(float[][]? Embeddings);
}
