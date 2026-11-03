namespace Turning.Application.Interfaces;

/// <summary>
/// Tipos de evento que publica la orquestación del experimento.
/// </summary>
public static class ExperimentEventTypes
{
    public const string SessionStateChanged = "SessionStateChanged";
    public const string SessionEnded = "SessionEnded";
    public const string SurveyAvailable = "SurveyAvailable";
    public const string EmotionReadingAdded = "EmotionReadingAdded";
    public const string AvatarExpressionChanged = "AvatarExpressionChanged";
    /// <summary>Se registró un turno de conversación.</summary>
    public const string ConversationTurnAdded = "ConversationTurnAdded";

    /// <summary>La IA generó una respuesta del interlocutor.</summary>
    public const string AiReplyGenerated = "AiReplyGenerated";

    /// <summary>Una operación se resolvió en modo degradado.</summary>
    public const string DegradedOperation = "DegradedOperation";

    /// <summary>La cadena de proveedores saltó de uno al siguiente.</summary>
    public const string AiProviderFailover = "AiProviderFailover";
}

/// <summary>
/// Puerto para dejar constancia de lo que ocurre en una sesión.
/// </summary>
/// <remarks>
/// Existe porque la orquestación vive en Application y no puede tocar
/// <c>TurningDbContext</c> directamente. La implementación encola el evento en la misma
/// unidad de trabajo que los repositorios: no guarda por su cuenta, para que un evento
/// nunca quede persistido sin el dato que lo motivó.
/// </remarks>
public interface IExperimentEventPublisher
{
    /// <summary>
    /// Encola un evento de la sesión. No persiste: lo hace el <c>SaveChanges</c> del flujo.
    /// </summary>
    /// <returns>Identificador del evento encolado, para poder referenciarlo.</returns>
    Guid Publish(Guid sessionId, string type, object payload);
}
