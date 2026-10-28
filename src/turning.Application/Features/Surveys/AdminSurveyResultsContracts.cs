using Turning.Application.Features.Events;

namespace Turning.Application.Features.Surveys;

/// <summary>Participante propietario de las respuestas de una encuesta.</summary>
public sealed record AdminSurveyParticipantDto(Guid Id, string FullName, string Email);

/// <summary>Respuesta enviada y su definición original, sin alterar ni completar sus datos.</summary>
public sealed record AdminSurveyResultDto(
    Guid ResponseId,
    Guid SessionId,
    string SessionCode,
    string Condition,
    string CompletionStatus,
    DateTime SubmittedAtUtc,
    AdminSurveyParticipantDto Participant,
    SurveyDefinitionDto Definition,
    IReadOnlyList<SurveyAnswerDto> Answers);

/// <summary>Página de encuestas enviadas, ordenadas por fecha de envío descendente.</summary>
public sealed record AdminSurveyResultsPageDto(
    IReadOnlyList<AdminSurveyResultDto> Items,
    int Total,
    int Page,
    int PageSize);
