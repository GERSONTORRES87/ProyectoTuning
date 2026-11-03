using System.Text.Json;
using System.Text.Json.Serialization;

namespace turning.Web.ExperimentSessions;

public sealed class CreateExperimentSessionRequestDto
{
    // Sin valor se omite del JSON y el backend asigna la condición.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreferredCondition { get; init; }
}

public sealed class ExperimentSessionSnapshotDto
{
    public Guid Id { get; init; }
    public string SessionCode { get; init; } = "";
    public string? Condition { get; init; }
    public string Status { get; init; } = "";
    public string AvatarState { get; init; } = "Neutral";
    public Guid? InterlocutorUserId { get; init; }
    public bool HasInterlocutor { get; init; }
    public bool IsAwaitingInterlocutor { get; init; }
    public string? ParticipationRole { get; init; }
    public int ConversationTurnCount { get; init; }
    public int EmotionSampleCount { get; init; }
    public string? LastDetectedEmotion { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ActivatedAtUtc { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string ConversationStage { get; init; } = "";
    public string EmotionStage { get; init; } = "";
    public string AvatarStage { get; init; } = "";
    [JsonIgnore] public bool IsTerminal => Status is "Completed" or "TimedOut" or "Cancelled";
    [JsonIgnore] public bool IsOwner => ParticipationRole == "Owner";
    [JsonIgnore] public bool CanConverse => ParticipationRole is "Owner" or "Interlocutor";
}

public sealed class PagedSessionsDto
{
    public List<ExperimentSessionSnapshotDto> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class ParticipantSessionsDto
{
    public List<ExperimentSessionSnapshotDto> Sessions { get; init; } = [];
    public int Total { get; init; }
}

public sealed class SessionListDto
{
    public List<ExperimentSessionSnapshotDto> Items { get; init; } = [];

    public int Total { get; init; }
}

public sealed class AddConversationTurnRequestDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sender { get; init; }
    public string Message { get; init; } = "";
}

public sealed class ConversationTurnSnapshotDto
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public int SequenceNumber { get; init; }
    public string Sender { get; init; } = "";
    public string Message { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class SessionResultDto
{
    public ExperimentSessionSnapshotDto Session { get; init; } = new();
    public List<ConversationTurnSnapshotDto> Conversation { get; init; } = [];
    public List<EmotionReadingDto> EmotionReadings { get; init; } = [];
    public List<AvatarDto> AvatarExpressions { get; init; } = [];
    public List<SurveyResponseDto> Survey { get; init; } = [];
    public List<JsonElement> DegradedEvents { get; init; } = [];
}

public sealed class EmotionReadingDto
{
    public Guid Id { get; init; }
    public Guid? ConversationTurnId { get; init; }
    public string Source { get; init; } = "";
    public string Emotion { get; init; } = "";
    public double Score { get; init; }
    public string Provider { get; init; } = "";
    public bool IsDegraded { get; init; }
    public DateTime CapturedAtUtc { get; init; }
}

public sealed record EmotionDto(Guid Id, string Emotion, double Score, string Source, string Provider, DateTime CapturedAtUtc);
public sealed record AvatarDto(string ExpressionName, double Intensity, bool IsFallback, string ParametersJson);
public sealed record SurveyQuestionDto(Guid Id, string Code, string Text, string Type, bool Required, int Order);
public sealed record SurveyDefinitionDto(Guid Id, string Code, string Version, IReadOnlyList<SurveyQuestionDto> Questions);
public sealed record SurveySubmitRequestDto(Guid SurveyDefinitionId, Dictionary<string, string> Answers);

public sealed record SurveyAnswerDto(Guid QuestionId, string Value);
public sealed record SurveyResponseDto(Guid Id, Guid SurveyDefinitionId, DateTime StartedAtUtc, DateTime? SubmittedAtUtc, IReadOnlyList<SurveyAnswerDto> Answers);
