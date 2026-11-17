using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Turning.Application.Interfaces;
using Turning.Domain.Emotions;
using Turning.Domain.Entities;

namespace Turning.Application.Features.Emotions;

/// <summary>
/// Aplica la política de RF-AES-01: prueba el clasificador externo, cae al analizador léxico
/// del dominio si no responde, mide la latencia, garantiza el fallback y nunca propaga excepciones.
/// </summary>
public sealed class TextEmotionAnalysisService : ITextEmotionAnalysisService
{
    /// <summary>
    /// Presupuesto de RF-AES-01 para el léxico local: el análisis no puede notarse en la
    /// conversación. Superarlo no invalida el resultado, pero sí deja constancia.
    /// </summary>
    public const int LatencyBudgetMs = 100;

    private readonly ITextEmotionClassifierPort _classifier;
    private readonly TextEmotionAnalyzer _analyzer;
    private readonly ILogger<TextEmotionAnalysisService> _logger;

    /// <summary>
    /// Constructor del servicio de análisis emocional de texto.
    /// </summary>
    public TextEmotionAnalysisService(
        ITextEmotionClassifierPort classifier,
        TextEmotionAnalyzer analyzer,
        ILogger<TextEmotionAnalysisService> logger)
    {
        _classifier = classifier;
        _analyzer = analyzer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TextEmotionDto> AnalyzeAsync(string? text, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                var classified = await _classifier.ClassifyAsync(text, cancellationToken);
                if (classified is not null)
                    return FromClassifier(classified, (int)stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falló el clasificador de emociones externo; se usa el léxico local");
            }
        }

        stopwatch.Restart();

        try
        {
            var analysis = _analyzer.Analyze(text);
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > LatencyBudgetMs)
            {
                _logger.LogWarning(
                    "El análisis emocional tardó {ElapsedMs} ms, por encima del presupuesto de {BudgetMs} ms",
                    stopwatch.ElapsedMilliseconds, LatencyBudgetMs);
            }

            return Map(analysis, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // RF-AES-01 exige fallback a neutral: la conversación no se detiene porque no
            // hayamos sabido leer una emoción, pero el fallo queda registrado.
            stopwatch.Stop();
            _logger.LogError(ex, "Falló el análisis emocional del texto; se aplica el resultado neutral");

            return Map(TextEmotionAnalysis.Fallback("unavailable"), (int)stopwatch.ElapsedMilliseconds);
        }
    }

    private static TextEmotionDto FromClassifier(TextEmotionClassification classified, int latencyMs)
    {
        double Score(string label) => classified.Scores.TryGetValue(label, out var value) ? value : 0;

        // La API no da intensidad ni polaridad: la intensidad es la probabilidad de que el texto no
        // sea neutral, y la polaridad sigue la valencia de la API (surprise cuenta como neutra).
        var intensity = Math.Clamp(1 - Score(EmotionLabel.Neutral), 0, 1);
        var polarity = Math.Clamp(
            Score(EmotionLabel.Joy)
            - (Score(EmotionLabel.Anger) + Score(EmotionLabel.Sadness) + Score(EmotionLabel.Fear) + Score(EmotionLabel.Disgust)),
            -1, 1);

        return new TextEmotionDto(
            EmotionId: classified.EmotionId,
            Confidence: Math.Clamp(classified.Confidence, 0, 1),
            Intensity: intensity,
            Polarity: polarity,
            Source: TextEmotionDto.SourceTextAnalysis,
            FallbackUsed: false,
            ModelVersion: classified.ModelVersion,
            LatencyMs: latencyMs,
            Scores: classified.Scores);
    }

    private static TextEmotionDto Map(TextEmotionAnalysis analysis, int latencyMs) =>
        new(
            EmotionId: analysis.EmotionId,
            Confidence: analysis.Confidence,
            Intensity: analysis.Intensity,
            Polarity: analysis.Polarity,
            Source: analysis.FallbackUsed ? TextEmotionDto.SourceFallback : TextEmotionDto.SourceTextAnalysis,
            FallbackUsed: analysis.FallbackUsed,
            ModelVersion: analysis.ModelVersion,
            LatencyMs: latencyMs,
            Scores: analysis.Scores);
}
