using Turning.Domain.Common;

namespace Turning.Domain.Entities;

/// <summary>
/// Expresion facial derivada de una lectura emocional.
/// </summary>
public sealed class AvatarExpression : BaseEntity
{
    private AvatarExpression() { }

    /// <summary>Sesion a la que pertenece.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>Lectura emocional que la origino.</summary>
    public Guid EmotionReadingId { get; private set; }

    /// <summary>Nombre de la expresion que debe mostrar el avatar.</summary>
    public string ExpressionName { get; private set; } = "Neutral";

    /// <summary>Intensidad de la expresion, 0..1.</summary>
    public double Intensity { get; private set; }

    /// <summary>Parametros adicionales de la expresion, en JSON.</summary>
    public string ParametersJson { get; private set; } = "{}";

    /// <summary>
    /// Indica que la emocion detectada no tiene expresion propia y se degrado a Neutral.
    /// </summary>
    public bool IsFallback { get; private set; }

    /// <summary>
    /// Expresiones que el avatar sabe representar.
    /// </summary>
    /// <remarks>
    /// Son cinco y el vocabulario emocional de RF-AES-01 son seis: <c>confusion</c> no
    /// tiene expresion propia y cae aqui en Neutral con <see cref="IsFallback"/>. Anadir la
    /// sexta expresion exige un asset del lado del avatar, asi que es una decision del
    /// equipo y no de este mapeo; mientras tanto, la frecuencia con que ocurre queda medible.
    /// </remarks>
    public static readonly IReadOnlyList<string> KnownExpressions = ["Joy", "Sadness", "Anger", "Surprise", "Neutral"];

    /// <summary>
    /// Traduce una etiqueta emocional al nombre de expresion del avatar.
    /// </summary>
    public static string MapExpressionName(string emotion) => emotion.ToLowerInvariant() switch
    {
        EmotionLabel.Joy => "Joy",
        EmotionLabel.Sadness => "Sadness",
        EmotionLabel.Anger => "Anger",
        EmotionLabel.Surprise => "Surprise",
        _ => "Neutral"
    };

    /// <summary>
    /// Deriva la expresion de avatar correspondiente a una lectura emocional.
    /// </summary>
    public static AvatarExpression FromReading(EmotionReading r)
    {
        ArgumentNullException.ThrowIfNull(r);

        var name = MapExpressionName(r.Emotion);
        // Una lectura degradada (proveedor caido o texto sin senal) llega como Neutral con la
        // intensidad de respaldo: sin esta marca el avatar la presentaria como una medicion.
        var fallback = r.IsDegraded
            || (name == "Neutral" && !string.Equals(r.Emotion, EmotionLabel.Neutral, StringComparison.OrdinalIgnoreCase));

        return new()
        {
            Id = Guid.NewGuid(),
            SessionId = r.SessionId,
            EmotionReadingId = r.Id,
            ExpressionName = name,
            Intensity = Math.Clamp(r.Score, 0, 1),
            IsFallback = fallback,
            CreatedAt = DateTime.UtcNow
        };
    }
}
