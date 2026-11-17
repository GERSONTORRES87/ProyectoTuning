using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;

namespace Turning.Application.Features.ConversationTurns;

/// <summary>
/// Implementa el enrutamiento por condición experimental (RF-EXP-01).
/// </summary>
public sealed class ConversationOrchestrator : IConversationOrchestrator
{
    /// <summary>Proveedor que figura cuando responde una regla del filtro en lugar del modelo.</summary>
    public const string GuardProvider = "guard";

    private readonly ITextGenerationPort _textGenerationPort;
    private readonly IConversationGuard _guard;
    private readonly ILogger<ConversationOrchestrator> _logger;

    /// <summary>
    /// Constructor del orquestador de conversación.
    /// </summary>
    public ConversationOrchestrator(
        ITextGenerationPort textGenerationPort,
        IConversationGuard guard,
        ILogger<ConversationOrchestrator> logger)
    {
        _textGenerationPort = textGenerationPort;
        _guard = guard;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InterlocutorReplyOutcome?> ResolveReplyAsync(
        ExperimentSession session,
        ConversationTurn participantTurn,
        IReadOnlyList<ConversationTurn> previousTurns,
        EmotionContext? emotionContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(participantTurn);
        ArgumentNullException.ThrowIfNull(previousTurns);

        // Condición Human: responde una persona. Que no se genere nada aquí es el
        // comportamiento correcto, no una carencia.
        if (session.Condition != ExperimentalCondition.AI)
            return null;

        // Solo el mensaje del participante dispara una respuesta. Si el turno ya viene del
        // interlocutor, generar otra respuesta encadenaría la conversación consigo misma.
        if (participantTurn.Sender != ConversationActor.Participant)
            return null;

        var stopwatch = Stopwatch.StartNew();
        var guard = await EvaluateGuardAsync(participantTurn, emotionContext, cancellationToken);

        if (guard.Blocked)
        {
            // Respuesta aprobada: el mensaje no llega al modelo. No es una degradación, es la política.
            return new InterlocutorReplyOutcome(
                Text: guard.Response,
                Provider: GuardProvider,
                Model: guard.RuleId,
                LatencyMs: stopwatch.ElapsedMilliseconds,
                Degraded: false,
                FailureReason: null,
                Attempts: 0,
                ProvidersTried: null,
                Guard: guard.Trace);
        }

        var request = new TextGenerationRequest
        {
            SessionId = session.Id,
            OriginatingTurnId = participantTurn.Id,
            UserInput = participantTurn.Message,
            ConversationHistory = ConversationHistoryBuilder.Build(previousTurns),
            EmotionContext = emotionContext,
            Guidance = guard.Guidance
        };

        try
        {
            var result = await _textGenerationPort.GenerateAsync(request, cancellationToken);
            stopwatch.Stop();

            if (result.Degraded)
            {
                _logger.LogWarning(
                    "Generación degradada en la sesión {SessionId}: proveedor {Provider}, motivo {FailureReason}",
                    session.Id, result.Provider, result.FailureReason);
            }

            return new InterlocutorReplyOutcome(
                Text: result.Text,
                Provider: result.Provider,
                Model: result.Model,
                LatencyMs: result.LatencyMs,
                Degraded: result.Degraded,
                FailureReason: result.FailureReason,
                Attempts: result.Attempts,
                ProvidersTried: result.ProvidersTried,
                Guard: guard.Trace);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // El cliente abandonó la petición: no es una degradación del proveedor.
            throw;
        }
        catch (Exception ex)
        {
            // Un fallo de la IA no puede costarle el mensaje al participante ni tumbar la
            // sesión, pero tampoco se silencia: antes esto era un catch vacío y la
            // degradación quedaba invisible para todos.
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Falló la generación de respuesta en la sesión {SessionId} para el turno {TurnId}",
                session.Id, participantTurn.Id);

            return new InterlocutorReplyOutcome(
                Text: null,
                Provider: "none",
                Model: null,
                LatencyMs: stopwatch.ElapsedMilliseconds,
                Degraded: true,
                FailureReason: ex.GetType().Name,
                Attempts: 1,
                ProvidersTried: null,
                Guard: guard.Trace);
        }
    }

    private async Task<GuardDecision> EvaluateGuardAsync(
        ConversationTurn participantTurn, EmotionContext? emotionContext, CancellationToken cancellationToken)
    {
        try
        {
            return await _guard.EvaluateAsync(participantTurn.Message, emotionContext?.EmotionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // El filtro ya degrada por su cuenta; esto cubre un fallo inesperado para no perder el turno.
            _logger.LogError(ex, "Falló el filtro previo al modelo para el turno {TurnId}", participantTurn.Id);
            return GuardDecision.PassThrough;
        }
    }
}
