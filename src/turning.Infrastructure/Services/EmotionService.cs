using Microsoft.EntityFrameworkCore;
using Turning.Application.Features.Emotions;
using Turning.Application.Features.ExperimentSessions;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;
using Turning.Infrastructure.Persistence;

namespace Turning.Infrastructure.Services;

public sealed class EmotionService : IEmotionService
{
    private readonly TurningDbContext _db;
    private readonly IEmotionAnalysisPort _port;
    private readonly IExperimentEventPublisher _events;

    public EmotionService(TurningDbContext db, IEmotionAnalysisPort port, IExperimentEventPublisher events)
    {
        _db = db; _port = port; _events = events;
    }

    public async Task<(EmotionDto emotion, AvatarDto avatar)> AddAsync(Guid sessionId, EmotionCreateRequest req, CancellationToken ct)
    {
        using var operation = await SessionOperationLock.AcquireAsync(sessionId, ct);
        var session = await _db.ExperimentSessions.FirstOrDefaultAsync(s => s.Id == sessionId && !s.IsDeleted, ct)
            ?? throw new InvalidOperationException("SESSION_NOT_FOUND");
        await _db.Entry(session).ReloadAsync(ct);
        if (session.IsDeleted) throw new InvalidOperationException("SESSION_NOT_FOUND");
        if (session.IsTerminal) throw new InvalidOperationException("SESSION_TERMINAL");
        if (!double.IsFinite(req.Score) || req.Score is < 0 or > 1) throw new ArgumentException("SCORE_RANGE");
        if (req.Emotion?.Length > 50) throw new ArgumentException("EMOTION_TOO_LONG");
        if (req.Source?.Length > 30) throw new ArgumentException("SOURCE_TOO_LONG");
        var source = string.IsNullOrWhiteSpace(req.Source) ? "video" : req.Source.Trim();
        EmotionReading reading;
        if (!string.IsNullOrWhiteSpace(req.Emotion))
        {
            reading = EmotionReading.Create(sessionId, req.Emotion.Trim(), req.Score, source, "direct");
        }
        else
        {
            try
            {
                var result = await _port.AnalyzeAsync(new EmotionAnalysisRequest { SessionId = sessionId, Source = source }, ct);
                if (string.IsNullOrWhiteSpace(result.Emotion) || result.Emotion.Length > 50
                    || string.IsNullOrWhiteSpace(result.Provider) || result.Provider.Length > 100 || !double.IsFinite(result.Score))
                    throw new InvalidOperationException("INVALID_EMOTION_RESULT");
                reading = EmotionReading.Create(sessionId, result.Emotion.Trim(), Math.Clamp(result.Score, 0, 1), source, result.Provider);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                reading = EmotionReading.Create(sessionId, EmotionLabel.Neutral, EmotionLabel.FallbackIntensity, source, "fallback");
                reading.MarkDegraded();
                _events.Publish(sessionId, ExperimentEventTypes.DegradedOperation, new
                {
                    code = "EMOTION_PROVIDER_UNAVAILABLE", operation = "EmotionAnalysis",
                    provider = _port.GetType().Name,
                    message = "El análisis emocional no estuvo disponible; se utilizó una expresión neutral.", retryable = true
                });
            }
        }
        var avatar = AvatarExpression.FromReading(reading);
        _db.EmotionReadings.Add(reading);
        _db.AvatarExpressions.Add(avatar);
        session.IncrementEmotionSample(reading.Emotion, avatar.ExpressionName);
        _events.Publish(sessionId, ExperimentEventTypes.EmotionReadingAdded, new { readingId = reading.Id, emotion = reading.Emotion, score = reading.Score });
        _events.Publish(sessionId, ExperimentEventTypes.AvatarExpressionChanged, new { emotionReadingId = reading.Id, expressionName = avatar.ExpressionName, intensity = avatar.Intensity, isFallback = avatar.IsFallback });
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("SESSION_CONFLICT"); }
        return (ToDto(reading), new AvatarDto(avatar.ExpressionName, avatar.Intensity, avatar.IsFallback, avatar.ParametersJson));
    }

    public async Task<IReadOnlyList<EmotionDto>> ListAsync(Guid sessionId, CancellationToken ct)
    {
        if (!await _db.ExperimentSessions.AnyAsync(s => s.Id == sessionId && !s.IsDeleted, ct))
            throw new InvalidOperationException("SESSION_NOT_FOUND");
        var readings = await _db.EmotionReadings.AsNoTracking().Where(e => e.SessionId == sessionId && !e.IsDeleted)
            .OrderBy(e => e.CapturedAtUtc).ThenBy(e => e.Id).ToListAsync(ct);
        return readings.Select(ToDto).ToArray();
    }

    private static EmotionDto ToDto(EmotionReading reading) =>
        new(reading.Id, reading.Emotion, reading.Score, reading.Source, reading.Provider, reading.CapturedAtUtc);
}

public sealed class AvatarService : IAvatarService
{
    private readonly TurningDbContext _db;
    public AvatarService(TurningDbContext db) => _db = db;

    public async Task<AvatarDto> GetCurrentAsync(Guid sessionId, CancellationToken ct)
    {
        if (!await _db.ExperimentSessions.AnyAsync(s => s.Id == sessionId && !s.IsDeleted, ct))
            throw new InvalidOperationException("SESSION_NOT_FOUND");
        var current = await _db.AvatarExpressions.AsNoTracking().Where(a => a.SessionId == sessionId && !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        return current is null ? new AvatarDto("Neutral", 0.3, true, "{}")
            : new AvatarDto(current.ExpressionName, current.Intensity, current.IsFallback, current.ParametersJson);
    }
}
