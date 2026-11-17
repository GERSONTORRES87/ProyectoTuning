namespace Turning.Application.Interfaces;

/// <summary>
/// Mensaje del historial conversacional en el formato que consumen los proveedores
/// de chat (RF-EXP-02).
/// </summary>
/// <param name="Role">Papel del emisor: <c>system</c>, <c>user</c> o <c>assistant</c>.</param>
/// <param name="Content">Texto del mensaje.</param>
/// <param name="TimestampUtc">Momento en que se emitió.</param>
public sealed record ChatMessage(string Role, string Content, DateTime TimestampUtc)
{
    /// <summary>Papel del participante humano.</summary>
    public const string RoleUser = "user";

    /// <summary>Papel del interlocutor mediado por IA.</summary>
    public const string RoleAssistant = "assistant";

    /// <summary>Papel de las instrucciones de sistema.</summary>
    public const string RoleSystem = "system";
}

/// <summary>
/// Contexto emocional del último mensaje del participante, para que la respuesta
/// generada sea coherente con cómo se está sintiendo (RF-EXP-01).
/// </summary>
/// <param name="EmotionId">Etiqueta emocional detectada.</param>
/// <param name="Confidence">Confianza del clasificador, 0..1.</param>
/// <param name="Intensity">Intensidad de la emoción, 0..1.</param>
/// <param name="Source">Origen del análisis.</param>
public sealed record EmotionContext(string EmotionId, double Confidence, double Intensity, string Source);

/// <summary>
/// Petición de generación de una respuesta del interlocutor.
/// </summary>
/// <remarks>
/// Sustituye a la firma anterior, que recibía la entidad <c>ExperimentSession</c> y la
/// lista completa de turnos de dominio. El request-object existe por tres razones: el
/// historial ya llega recortado a la ventana de RF-EXP-02, hay dónde poner el contexto
/// emocional, y los adaptadores dejan de depender de entidades mutables del dominio.
/// </remarks>
public sealed record TextGenerationRequest
{
    /// <summary>Sesión para la que se genera la respuesta.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>Turno del participante que provocó esta generación.</summary>
    public required Guid OriginatingTurnId { get; init; }

    /// <summary>Último mensaje del participante.</summary>
    public required string UserInput { get; init; }

    /// <summary>
    /// Historial previo, ya recortado a la ventana configurada y en orden cronológico.
    /// </summary>
    public required IReadOnlyList<ChatMessage> ConversationHistory { get; init; }

    /// <summary>Contexto emocional del mensaje, si se pudo analizar.</summary>
    public EmotionContext? EmotionContext { get; init; }

    /// <summary>Pautas recuperadas por el filtro para orientar esta respuesta.</summary>
    public IReadOnlyList<string> Guidance { get; init; } = [];
}

/// <summary>
/// Resultado de una generación de texto mediada por IA (o su baseline por reglas).
/// Cumple el contrato definido para el pipeline de datos/AI: texto generado, proveedor
/// usado, latencia medida y si operó en modo degradado.
/// </summary>
/// <param name="Text">Texto generado.</param>
/// <param name="Provider">Proveedor que respondió.</param>
/// <param name="LatencyMs">Latencia medida de la generación.</param>
/// <param name="Degraded">Indica que la respuesta no vino de un proveedor real.</param>
/// <param name="EventId">Evento de trazabilidad asociado, si se registró uno.</param>
/// <param name="Model">Modelo concreto que atendió la petición.</param>
/// <param name="Attempts">Número de intentos consumidos.</param>
/// <param name="FailureReason">Motivo del último fallo, cuando lo hubo.</param>
/// <param name="ProvidersTried">Proveedores recorridos, en orden.</param>
public sealed record TextGenerationResult(
    string Text,
    string Provider,
    long LatencyMs,
    bool Degraded,
    Guid? EventId = null,
    string? Model = null,
    int Attempts = 1,
    string? FailureReason = null,
    IReadOnlyList<string>? ProvidersTried = null);

/// <summary>
/// Puerto para generar texto del interlocutor mediado por IA.
/// </summary>
public interface ITextGenerationPort
{
    /// <summary>
    /// Genera una respuesta del interlocutor a partir del mensaje del participante y
    /// el historial de la conversación.
    /// </summary>
    Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request, CancellationToken cancellationToken = default);
}
