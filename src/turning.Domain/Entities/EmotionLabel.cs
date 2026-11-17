namespace Turning.Domain.Entities;

/// <summary>
/// Vocabulario de etiquetas emocionales que el análisis de texto puede producir,
/// según RF-AES-01.
/// </summary>
/// <remarks>
/// Deliberadamente NO se valida en <see cref="EmotionReading.Create"/>: esa ruta la usa
/// el canal directo del cliente y el de video, que hoy aceptan cualquier cadena y tienen
/// filas persistidas. Endurecerla rompería <c>POST /api/sessions/{id}/emotions</c>.
/// La validación se aplica solo en la ruta de análisis de texto.
///
/// <see cref="Confusion"/>, <see cref="Fear"/> y <see cref="Disgust"/> no tienen expresión
/// propia de avatar: se mapean a Neutral con marca de fallback. Añadir la sexta expresión exige un asset del lado del avatar y es
/// una decisión del equipo, no de este código.
/// </remarks>
public static class EmotionLabel
{
    /// <summary>Alegría.</summary>
    public const string Joy = "joy";

    /// <summary>Tristeza.</summary>
    public const string Sadness = "sadness";

    /// <summary>Enojo.</summary>
    public const string Anger = "anger";

    /// <summary>Sorpresa.</summary>
    public const string Surprise = "surprise";

    /// <summary>Miedo. Sin expresión propia de avatar; lo produce la API de emociones.</summary>
    public const string Fear = "fear";

    /// <summary>Asco. Sin expresión propia de avatar; lo produce la API de emociones.</summary>
    public const string Disgust = "disgust";

    /// <summary>Confusión. Sin expresión propia de avatar.</summary>
    public const string Confusion = "confusion";

    /// <summary>Neutral. Es también el valor de fallback obligatorio.</summary>
    public const string Neutral = "neutral";

    /// <summary>
    /// Intensidad del fallback obligatorio de RF-AES-01.
    /// </summary>
    public const double FallbackIntensity = 0.3;

    /// <summary>
    /// Todas las etiquetas reconocidas.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Joy, Sadness, Anger, Surprise, Fear, Disgust, Confusion, Neutral];

    /// <summary>
    /// Indica si la etiqueta pertenece al vocabulario conocido.
    /// </summary>
    public static bool IsKnown(string? label) =>
        label is not null && All.Contains(label.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Devuelve la etiqueta con la grafía canónica, o <see cref="Neutral"/> si no se reconoce.
    /// </summary>
    public static string Normalize(string? label) =>
        label is null
            ? Neutral
            : All.FirstOrDefault(known => known.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Neutral;
}
