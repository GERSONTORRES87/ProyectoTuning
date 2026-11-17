namespace Turning.Application.Features.Emotions;

/// <summary>
/// Lectura emocional derivada del texto de un turno, en la forma que define RF-AES-01.
/// </summary>
/// <param name="EmotionId">Etiqueta emocional detectada.</param>
/// <param name="Confidence">Confianza del clasificador, 0..1.</param>
/// <param name="Intensity">Intensidad de la emoción, 0..1.</param>
/// <param name="Polarity">Valencia del texto, -1..1.</param>
/// <param name="Source">
/// Origen del resultado: <c>text_analysis</c> cuando el análisis funcionó,
/// <c>fallback</c> cuando se aplicó el resultado neutral obligatorio.
/// </param>
/// <param name="FallbackUsed">Indica si se recurrió al fallback.</param>
/// <param name="ModelVersion">Versión del modelo o del léxico usado, para poder reproducir el análisis.</param>
/// <param name="LatencyMs">Tiempo que tomó el análisis.</param>
/// <param name="Scores">Distribución por etiqueta, serializada por el llamante si la persiste.</param>
public sealed record TextEmotionDto(
    string EmotionId,
    double Confidence,
    double Intensity,
    double Polarity,
    string Source,
    bool FallbackUsed,
    string ModelVersion,
    int LatencyMs,
    IReadOnlyDictionary<string, double> Scores)
{
    /// <summary>Origen cuando el análisis del texto produjo resultado.</summary>
    public const string SourceTextAnalysis = "text_analysis";

    /// <summary>Origen cuando se aplicó el resultado neutral obligatorio.</summary>
    public const string SourceFallback = "fallback";
}

/// <summary>
/// Analiza el texto de la conversación para obtener su carga emocional.
/// </summary>
/// <remarks>
/// Es un caso de uso, no un puerto a un servicio externo: RF-AES-01 define este análisis
/// como interno y dice expresamente que no pasa por <c>IEmotionAnalysisPort</c>, que es el
/// canal de video y recibe bytes, no texto.
/// </remarks>
public interface ITextEmotionAnalysisService
{
    /// <summary>
    /// Analiza un texto con el clasificador externo y, si no responde, con el léxico local. Solo
    /// lanza si el llamante cancela: ante cualquier otro problema devuelve el resultado neutral de
    /// fallback, porque una emoción no detectada no puede costar el turno del participante.
    /// </summary>
    Task<TextEmotionDto> AnalyzeAsync(string? text, CancellationToken cancellationToken = default);
}
