namespace Turning.Application.Interfaces;

/// <summary>
/// Emoción del texto que devuelve un clasificador externo, ya traducida al vocabulario de SUE.
/// </summary>
/// <param name="EmotionId">Etiqueta de mayor probabilidad, de <c>EmotionLabel</c>.</param>
/// <param name="Confidence">Probabilidad de esa etiqueta, 0..1.</param>
/// <param name="Scores">Distribución por etiqueta de SUE.</param>
/// <param name="ModelVersion">Versión del modelo que informa el clasificador.</param>
public sealed record TextEmotionClassification(
    string EmotionId,
    double Confidence,
    IReadOnlyDictionary<string, double> Scores,
    string ModelVersion);

/// <summary>
/// Clasificador emocional externo del texto (la API de emociones del equipo).
/// </summary>
public interface ITextEmotionClassifierPort
{
    /// <summary>
    /// Clasifica un texto. Devuelve <c>null</c> cuando el clasificador no está configurado, falla o
    /// no responde a tiempo: quien llama aplica entonces su respaldo local.
    /// </summary>
    Task<TextEmotionClassification?> ClassifyAsync(string text, CancellationToken cancellationToken = default);
}
