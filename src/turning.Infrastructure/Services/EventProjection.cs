using System.Text.Json;
using Turning.Application.Features.Events;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;

namespace Turning.Infrastructure.Services;

internal static class EventProjection
{
    /// <summary>
    /// Indica si el participante puede ver el evento.
    /// </summary>
    /// <remarks>
    /// La degradación de la generación de texto (y el cambio de proveedor) solo ocurre en
    /// sesiones de IA, y sin claves externas en todos sus turnos: mostrarla, aun con un
    /// mensaje genérico, delata la condición. La degradación del análisis emocional existe en
    /// ambas condiciones y se conserva. Ante un payload que no se reconoce se oculta.
    /// </remarks>
    public static bool IsVisibleToParticipant(ExperimentEvent entry)
    {
        if (entry.Type == ExperimentEventTypes.AiProviderFailover)
            return false;

        if (entry.Type != ExperimentEventTypes.DegradedOperation)
            return true;

        using var document = JsonDocument.Parse(entry.PayloadJson);
        return document.RootElement.TryGetProperty("operation", out var operation)
            && operation.GetString() == "EmotionAnalysis";
    }

    public static EventDto ToDto(ExperimentEvent entry, bool includeInternalDetails)
    {
        if (includeInternalDetails)
            return new EventDto(entry.Id, entry.Type, entry.PayloadJson, entry.OccurredAtUtc, entry.ExpiresAtUtc, entry.SessionId);

        // El participante no debe conocer la condición ni identificar el proveedor por los eventos.
        using var document = JsonDocument.Parse(entry.PayloadJson);
        var type = entry.Type == ExperimentEventTypes.AiReplyGenerated
            ? ExperimentEventTypes.ConversationTurnAdded : entry.Type;
        object payload;
        if (entry.Type is ExperimentEventTypes.DegradedOperation or ExperimentEventTypes.AiProviderFailover)
        {
            type = ExperimentEventTypes.DegradedOperation;
            var isEmotion = document.RootElement.TryGetProperty("operation", out var operation)
                && operation.GetString() == "EmotionAnalysis";
            payload = new
            {
                code = "OPERATION_UNAVAILABLE",
                operation = isEmotion ? "EmotionAnalysis" : "Conversation",
                message = "La operación no estuvo disponible.",
                retryable = true
            };
        }
        else
        {
            var allowed = new[] { "turnId", "sequenceNumber", "sender", "status", "previousStatus", "newStatus",
                "readingId", "emotionReadingId", "emotion", "score", "expressionName", "intensity", "isFallback", "surveyDefinitionId", "sessionId" };
            payload = document.RootElement.EnumerateObject().Where(p => allowed.Contains(p.Name))
                .ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        return new EventDto(entry.Id, type, JsonSerializer.Serialize(payload), entry.OccurredAtUtc, entry.ExpiresAtUtc, entry.SessionId);
    }
}
