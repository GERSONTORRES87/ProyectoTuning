namespace turning.Web.Surveys;

public sealed class SurveyDefinitionDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public List<SurveyQuestionDto> Questions { get; init; } = [];
}

public sealed class SurveyQuestionDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string Type { get; init; } = "text";
    public bool Required { get; init; }
    public int Order { get; init; }
}

public sealed class SurveyAnswerDto
{
    public Guid QuestionId { get; init; }
    public string Value { get; init; } = string.Empty;
}

public sealed class SurveyParticipantDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}

public sealed class AdminSurveyResultDto
{
    public Guid ResponseId { get; init; }
    public Guid SessionId { get; init; }
    public string SessionCode { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public string CompletionStatus { get; init; } = string.Empty;
    public DateTime SubmittedAtUtc { get; init; }
    public SurveyParticipantDto Participant { get; init; } = new();
    public SurveyDefinitionDto Definition { get; init; } = new();
    public List<SurveyAnswerDto> Answers { get; init; } = [];
}
