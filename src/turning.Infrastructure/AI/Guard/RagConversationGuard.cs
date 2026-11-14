using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;

namespace Turning.Infrastructure.AI.Guard;

/// <summary>
/// Filtro RAG previo al modelo: compara el mensaje por embeddings con los ejemplos de la base de
/// conocimiento, bloquea con respuesta aprobada o recupera pautas para el prompt.
/// </summary>
/// <remarks>
/// El índice (un vector por ejemplo) se construye en segundo plano la primera vez que se usa: ningún
/// turno espera a que se embeba toda la base. Hasta tenerlo, y siempre que Ollama falle, el filtro
/// decide solo por palabras clave. Las palabras clave bloquean también con embeddings, para que una
/// señal de crisis explícita nunca dependa de un umbral de similitud.
/// shortcut: índice en memoria por instancia; con varias réplicas cada una construye el suyo.
/// </remarks>
public sealed class RagConversationGuard : IConversationGuard
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly GuardKnowledge _knowledge;
    private readonly IEmbeddingClient _embeddings;
    private readonly IOptions<GuardOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RagConversationGuard> _logger;
    private readonly object _gate = new();

    private Index? _index;
    private Task _building = Task.CompletedTask;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Constructor del filtro.
    /// </summary>
    public RagConversationGuard(
        GuardKnowledge knowledge, IEmbeddingClient embeddings, IOptions<GuardOptions> options,
        TimeProvider time, ILogger<RagConversationGuard> logger)
    {
        _knowledge = knowledge;
        _embeddings = embeddings;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Arranca la construcción del índice si hace falta y devuelve la tarea en curso. Sirve para
    /// calentarlo al arrancar y para las pruebas.
    /// </summary>
    public Task WarmUpAsync()
    {
        if (_index is null && _embeddings.IsConfigured)
        {
            lock (_gate)
            {
                if (_index is null && _building.IsCompleted && _time.GetUtcNow() >= _retryAt)
                    _building = Task.Run(BuildIndexAsync);
            }
        }

        return _building;
    }

    /// <inheritdoc />
    public async Task<GuardDecision> EvaluateAsync(string message, string? emotionId, CancellationToken cancellationToken = default)
    {
        var emotionGuide = emotionId is null
            ? null
            : _knowledge.EmotionGuides.FirstOrDefault(guide => guide.Emotion == EmotionLabel.Normalize(emotionId));

        var keywordRule = MatchKeywords(message);
        if (keywordRule is not null)
            return Blocked(keywordRule);

        _ = WarmUpAsync();
        var index = _index;
        float[]? vector = null;
        if (index is not null)
        {
            try
            {
                vector = Normalize((await _embeddings.EmbedAsync([message], _options.Value.TimeoutMs, cancellationToken))[0]);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sin embeddings para este turno ({Error}); el filtro usa solo palabras clave", ex.GetType().Name);
            }
        }

        if (index is null || vector is null)
            return WithEmotion(GuardMode.Keywords, [], emotionGuide);

        var options = _options.Value;
        var rule = index.Rules
            .Select(entry => (entry.Item, Score: entry.Score(vector)))
            .MaxBy(match => match.Score);
        if (rule.Item is not null && rule.Score >= options.BlockThreshold)
            return Blocked(rule.Item);

        var topics = index.Topics
            .Select(entry => (entry.Item, Score: entry.Score(vector)))
            .Where(match => match.Score >= options.GuideThreshold)
            .OrderByDescending(match => match.Score)
            .Take(options.MaxGuides)
            .Select(match => match.Item)
            .ToArray();

        return WithEmotion(GuardMode.Context, topics, emotionGuide);
    }

    private BlockRule? MatchKeywords(string message)
    {
        var text = GuardKnowledge.NormalizeText(message);
        return _knowledge.BlockRules.FirstOrDefault(rule => rule.Keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal)));
    }

    private static GuardDecision Blocked(BlockRule rule) => new(GuardMode.Blocked, rule.Id, rule.Response, [], []);

    private static GuardDecision WithEmotion(GuardMode mode, IReadOnlyList<TopicGuide> topics, EmotionGuide? emotion)
    {
        // La emoción va primero: con la pauta del tema delante, el modelo respondía al tema y no a lo que la persona siente.
        var guidance = new List<string>();
        var ids = new List<string>();
        if (emotion is not null)
        {
            guidance.Add(emotion.Guidance);
            ids.Add($"emotion:{emotion.Emotion}");
        }
        guidance.AddRange(topics.Select(topic => topic.Guidance));
        ids.AddRange(topics.Select(topic => topic.Id));
        return new GuardDecision(mode, null, null, guidance, ids);
    }

    private async Task BuildIndexAsync()
    {
        try
        {
            var texts = _knowledge.BlockRules.SelectMany(rule => rule.Examples)
                .Concat(_knowledge.TopicGuides.SelectMany(guide => guide.Examples))
                .ToArray();
            var vectors = (await _embeddings.EmbedAsync(texts, _options.Value.IndexTimeoutMs, CancellationToken.None))
                .Select(Normalize)
                .ToArray();

            // Los vectores vienen en el orden de 'texts': cada entrada toma los suyos en ese mismo orden.
            var offset = 0;
            float[][] Next(int count)
            {
                var slice = vectors[offset..(offset + count)];
                offset += count;
                return slice;
            }

            var rules = _knowledge.BlockRules.Select(rule => new Entry<BlockRule>(rule, Next(rule.Examples.Count))).ToArray();
            var topics = _knowledge.TopicGuides.Select(guide => new Entry<TopicGuide>(guide, Next(guide.Examples.Count))).ToArray();

            _index = new Index(rules, topics);
            _logger.LogInformation("Índice del filtro listo: {Examples} ejemplos de la base {Version}", texts.Length, _knowledge.Version);
        }
        catch (Exception ex)
        {
            _retryAt = _time.GetUtcNow() + RetryAfterFailure;
            _logger.LogWarning("No se pudo construir el índice del filtro ({Error}); se reintenta en un minuto", ex.GetType().Name);
        }
    }

    private static float[] Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        return norm == 0 ? vector : vector.Select(value => value / norm).ToArray();
    }

    private sealed record Index(IReadOnlyList<Entry<BlockRule>> Rules, IReadOnlyList<Entry<TopicGuide>> Topics);

    private sealed record Entry<T>(T Item, float[][] Vectors)
    {
        /// <summary>Similitud coseno con el ejemplo más parecido; los vectores ya están normalizados.</summary>
        public double Score(float[] vector) =>
            Vectors.Length == 0 ? 0 : Vectors.Max(example => example.Zip(vector, (a, b) => (double)a * b).Sum());
    }
}
