using System.Globalization;
using Turning.Domain.Common;

namespace Turning.Domain.Entities;

public sealed class SurveyDefinition : BaseEntity
{
    private SurveyDefinition() { }
    public string Code { get; private set; } = string.Empty;
    public string Version { get; private set; } = "1.0";
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public List<SurveyQuestion> Questions { get; private set; } = [];

    public static SurveyDefinition Create(string code, string name) => new()
    {
        Code = code,
        Name = name,
        IsActive = true
    };

    public void AddQuestion(string code, string text, string type, bool required, int order)
    {
        if (Questions.Any(q => q.Code == code || q.Order == order))
            throw new ArgumentException("SURVEY_DUPLICATE_QUESTION");
        Questions.Add(SurveyQuestion.Create(Id, code, text, type, required, order));
    }

    public void Activate() => IsActive = true;
}

public sealed class SurveyQuestion : BaseEntity
{
    private SurveyQuestion() { }
    public Guid SurveyDefinitionId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string Type { get; private set; } = "text";
    public bool Required { get; private set; }
    public int Order { get; private set; }

    public static SurveyQuestion Create(Guid definitionId, string code, string text, string type, bool required, int order)
    {
        if (definitionId == Guid.Empty || string.IsNullOrWhiteSpace(code) || code.Length > 50
            || string.IsNullOrWhiteSpace(text) || text.Length > 1000 || order < 1
            || type is not ("text" or "scale" or "choice"))
            throw new ArgumentException("SURVEY_INVALID_QUESTION");
        return new SurveyQuestion
        {
            SurveyDefinitionId = definitionId,
            Code = code,
            Text = text,
            Type = type,
            Required = required,
            Order = order
        };
    }

    internal string ValidateAnswer(string value)
    {
        var answer = value.Trim();
        if (answer.Length > 4000)
            throw new ArgumentException("SURVEY_ANSWER_TOO_LONG");
        if (Required && answer.Length == 0)
            throw new ArgumentException("SURVEY_REQUIRED_ANSWER");
        if (answer.Length == 0) return answer;
        if (Type == "scale" && (!int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var score) || score is < 1 or > 5))
            throw new ArgumentException("SURVEY_INVALID_SCALE");
        if (Type == "choice" && answer is not ("Human" or "AI" or "Unsure"))
            throw new ArgumentException("SURVEY_INVALID_CHOICE");
        return answer;
    }
}

public sealed class SurveyResponse : BaseEntity
{
    private SurveyResponse() { }
    public Guid SessionId { get; private set; }
    public Guid SurveyDefinitionId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public List<SurveyAnswer> Answers { get; private set; } = [];

    public static SurveyResponse Create(Guid sessionId, Guid ownerUserId, Guid definitionId)
    {
        if (sessionId == Guid.Empty || ownerUserId == Guid.Empty || definitionId == Guid.Empty)
            throw new ArgumentException("SURVEY_INVALID_RESPONSE");
        return new SurveyResponse
        {
            SessionId = sessionId,
            OwnerUserId = ownerUserId,
            SurveyDefinitionId = definitionId,
            StartedAtUtc = DateTime.UtcNow
        };
    }

    public void Submit(SurveyDefinition definition, IReadOnlyDictionary<string, string>? answers)
    {
        if (SubmittedAtUtc.HasValue) throw new InvalidOperationException("ALREADY_SUBMITTED");
        if (definition.Id != SurveyDefinitionId) throw new ArgumentException("SURVEY_NOT_FOUND");
        if (definition.Questions.Count == 0) throw new ArgumentException("SURVEY_NO_QUESTIONS");

        var validated = new Dictionary<Guid, SurveyAnswer>();
        foreach (var pair in answers ?? new Dictionary<string, string>())
        {
            var hasQuestionId = Guid.TryParse(pair.Key, out var questionId);
            var question = definition.Questions.FirstOrDefault(q => q.Code == pair.Key || (hasQuestionId && q.Id == questionId));
            if (question is null) throw new ArgumentException("SURVEY_UNKNOWN_QUESTION");
            if (validated.ContainsKey(question.Id)) throw new ArgumentException("SURVEY_DUPLICATE_ANSWER");
            var value = question.ValidateAnswer(pair.Value ?? string.Empty);
            validated.Add(question.Id, SurveyAnswer.Create(Id, question.Id, value));
        }
        if (definition.Questions.Any(q => q.Required && (!validated.TryGetValue(q.Id, out var answer) || string.IsNullOrWhiteSpace(answer.Value))))
            throw new ArgumentException("SURVEY_REQUIRED_ANSWER");

        Answers.AddRange(validated.Values);
        SubmittedAtUtc = DateTime.UtcNow;
    }
}

public sealed class SurveyAnswer : BaseEntity
{
    private SurveyAnswer() { }
    public Guid SurveyResponseId { get; private set; }
    public Guid SurveyQuestionId { get; private set; }
    public string Value { get; private set; } = string.Empty;

    internal static SurveyAnswer Create(Guid responseId, Guid questionId, string value) => new()
    {
        SurveyResponseId = responseId,
        SurveyQuestionId = questionId,
        Value = value
    };
}
