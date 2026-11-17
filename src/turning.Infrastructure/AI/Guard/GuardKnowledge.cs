using System.Globalization;
using System.Text;
using System.Text.Json;
using Turning.Domain.Entities;

namespace Turning.Infrastructure.AI.Guard;

/// <summary>Regla que responde con un texto aprobado en lugar del modelo.</summary>
public sealed record BlockRule(string Id, IReadOnlyList<string> Examples, IReadOnlyList<string> Keywords, string Response);

/// <summary>Pauta que se añade al prompt cuando el mensaje trata un tema.</summary>
public sealed record TopicGuide(string Id, IReadOnlyList<string> Examples, string Guidance);

/// <summary>Pauta que se añade al prompt según la emoción detectada.</summary>
public sealed record EmotionGuide(string Emotion, string Guidance);

/// <summary>
/// Base de conocimiento del filtro previo al modelo (<c>conversation-guard-es.json</c>, recurso embebido).
/// </summary>
public sealed class GuardKnowledge
{
    private const string ResourceName = "turning.Infrastructure.AI.Guard.conversation-guard-es.json";

    private GuardKnowledge(string version, IReadOnlyList<BlockRule> blockRules, IReadOnlyList<TopicGuide> topicGuides, IReadOnlyList<EmotionGuide> emotionGuides)
    {
        Version = version;
        BlockRules = blockRules;
        TopicGuides = topicGuides;
        EmotionGuides = emotionGuides;
    }

    /// <summary>Versión de la base, para trazabilidad.</summary>
    public string Version { get; }

    /// <summary>Reglas de bloqueo.</summary>
    public IReadOnlyList<BlockRule> BlockRules { get; }

    /// <summary>Pautas por tema.</summary>
    public IReadOnlyList<TopicGuide> TopicGuides { get; }

    /// <summary>Pautas por emoción.</summary>
    public IReadOnlyList<EmotionGuide> EmotionGuides { get; }

    /// <summary>Carga la base incluida en el ensamblado.</summary>
    public static GuardKnowledge LoadEmbedded()
    {
        using var stream = typeof(GuardKnowledge).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"No se encontró el recurso {ResourceName}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    /// Lee y valida una base. Lanza <see cref="InvalidOperationException"/> nombrando la entrada inválida:
    /// una regla de crisis sin respuesta no puede descubrirse en mitad de un ensayo.
    /// </summary>
    public static GuardKnowledge Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<RawKnowledge>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("La base del filtro está vacía.");

        if (string.IsNullOrWhiteSpace(raw.Version))
            throw new InvalidOperationException("La base del filtro no tiene 'version'.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string RequireId(string? id, string kind) =>
            !string.IsNullOrWhiteSpace(id) && ids.Add(id)
                ? id
                : throw new InvalidOperationException($"Hay una entrada de '{kind}' sin id o con id repetido ({id}).");

        var rules = (raw.BlockRules ?? []).Select(rule =>
        {
            var id = RequireId(rule.Id, "blockRules");
            var examples = Clean(rule.Examples);
            var keywords = Clean(rule.Keywords).Select(NormalizeText).ToArray();
            if (string.IsNullOrWhiteSpace(rule.Response))
                throw new InvalidOperationException($"La regla de bloqueo '{id}' no tiene respuesta aprobada.");
            if (examples.Length == 0 && keywords.Length == 0)
                throw new InvalidOperationException($"La regla de bloqueo '{id}' no tiene ejemplos ni palabras clave.");
            return new BlockRule(id, examples, keywords, rule.Response.Trim());
        }).ToArray();

        var topics = (raw.TopicGuides ?? []).Select(guide =>
        {
            var id = RequireId(guide.Id, "topicGuides");
            var examples = Clean(guide.Examples);
            if (examples.Length == 0 || string.IsNullOrWhiteSpace(guide.Guidance))
                throw new InvalidOperationException($"La pauta '{id}' necesita ejemplos y texto de pauta.");
            return new TopicGuide(id, examples, guide.Guidance.Trim());
        }).ToArray();

        var emotions = (raw.EmotionGuides ?? []).Select(guide =>
        {
            if (!EmotionLabel.IsKnown(guide.Emotion) || string.IsNullOrWhiteSpace(guide.Guidance))
                throw new InvalidOperationException($"La pauta de emoción '{guide.Emotion}' no es válida.");
            return new EmotionGuide(EmotionLabel.Normalize(guide.Emotion), guide.Guidance.Trim());
        }).ToArray();

        return new GuardKnowledge(raw.Version, rules, topics, emotions);
    }

    /// <summary>
    /// Minúsculas, sin tildes y con los signos convertidos en espacios, rodeado de espacios para que las
    /// palabras clave coincidan por palabra completa.
    /// </summary>
    public static string NormalizeText(string text)
    {
        var builder = new StringBuilder(text.Length + 2).Append(' ');
        foreach (var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var next = char.IsLetterOrDigit(c) ? c : ' ';
            if (next != ' ' || builder[^1] != ' ') builder.Append(next);
        }
        if (builder[^1] != ' ') builder.Append(' ');
        return builder.ToString();
    }

    private static string[] Clean(IEnumerable<string>? values) =>
        (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();

    private sealed record RawKnowledge(string? Version, List<RawRule>? BlockRules, List<RawTopic>? TopicGuides, List<RawEmotion>? EmotionGuides);
    private sealed record RawRule(string? Id, List<string>? Examples, List<string>? Keywords, string? Response);
    private sealed record RawTopic(string? Id, List<string>? Examples, string? Guidance);
    private sealed record RawEmotion(string? Emotion, string? Guidance);
}
