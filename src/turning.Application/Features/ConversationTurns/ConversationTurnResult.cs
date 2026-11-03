namespace Turning.Application.Features.ConversationTurns;

/// <summary>
/// Metadatos de la generación mediada por IA que produjo la respuesta del interlocutor.
/// </summary>
/// <remarks>
/// Antes se descartaban: el servicio recibía proveedor, latencia y estado degradado del
/// puerto y devolvía solo el turno del participante. Sin esto no hay forma de saber, desde
/// fuera, si el participante habló con el modelo previsto o con el baseline.
/// </remarks>
public sealed class AiMetadataDto
{
    /// <summary>Proveedor que respondió.</summary>
    public required string Provider { get; init; }

    /// <summary>Modelo concreto que atendió la petición, si el proveedor lo informa.</summary>
    public string? Model { get; init; }

    /// <summary>Latencia medida de la generación, en milisegundos.</summary>
    public long LatencyMs { get; init; }

    /// <summary>Número de intentos consumidos.</summary>
    public int Attempts { get; init; }

    /// <summary>Proveedores recorridos antes de obtener respuesta, en orden.</summary>
    public IReadOnlyList<string>? ProvidersTried { get; init; }
}

/// <summary>
/// Resultado de registrar un mensaje en una sesión.
/// </summary>
/// <remarks>
/// Conserva en la raíz los mismos campos que devolvía <see cref="ConversationTurnSnapshot"/>
/// y añade el resto. Es deliberadamente aditivo: un cliente escrito contra el contrato
/// anterior sigue funcionando sin cambios, y puede adoptar los campos nuevos cuando quiera.
/// Cumple la forma de RF-EXP-04 (texto, emoción, fuente y marca de tiempo unificados).
/// </remarks>
public sealed class ConversationTurnResult
{
    public const string SourceInterlocutor = "interlocutor";
    public const string SourcePending = "pending";
    /// <summary>Identificador del turno del participante.</summary>
    public Guid Id { get; init; }

    /// <summary>Sesión a la que pertenece.</summary>
    public Guid SessionId { get; init; }

    /// <summary>Orden secuencial del turno.</summary>
    public int SequenceNumber { get; init; }

    /// <summary>Actor que emitió el mensaje.</summary>
    public required string Sender { get; init; }

    /// <summary>Texto del mensaje.</summary>
    public required string Message { get; init; }

    /// <summary>Fecha de creación en UTC.</summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>Turno que originó este mensaje, si lo hay.</summary>
    public Guid? OriginatingTurnId { get; init; }

    /// <summary>
    /// Respuesta del interlocutor, cuando la sesión la genera automáticamente.
    /// En condición Human siempre es <c>null</c>: ahí responde una persona.
    /// </summary>
    public ConversationTurnSnapshot? InterlocutorTurn { get; init; }

    /// <summary>
    /// Origen de la respuesta: <c>ai</c>, <c>human</c> o <c>none</c> si todavía no hay.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>Metadatos de la generación, cuando la respuesta la produjo la IA.</summary>
    public AiMetadataDto? Ai { get; init; }

    /// <summary>
    /// Lectura emocional del mensaje del participante (RF-AES-01). Es el <c>emotionId</c> y
    /// la confianza que RF-EXP-04 pide en el payload enriquecido.
    /// </summary>
    public Emotions.TextEmotionDto? Emotion { get; init; }

    /// <summary>
    /// Indica que algo se resolvió en modo degradado: el proveedor falló o respondió el
    /// baseline. La operación es correcta, pero el dato no es el que el experimento espera.
    /// </summary>
    public bool Degraded { get; init; }

    /// <summary>Motivo de la degradación, en texto seguro para el cliente.</summary>
    public string? DegradedReason { get; init; }

    /// <summary>Evento de trazabilidad asociado a la degradación, si se registró.</summary>
    public Guid? EventId { get; init; }

    /// <summary>Valor de <see cref="Source"/> cuando responde la IA.</summary>
    public const string SourceAi = "ai";

    /// <summary>Valor de <see cref="Source"/> cuando responde una persona.</summary>
    public const string SourceHuman = "human";

    /// <summary>Valor de <see cref="Source"/> cuando no hay respuesta todavía.</summary>
    public const string SourceNone = "none";
}
