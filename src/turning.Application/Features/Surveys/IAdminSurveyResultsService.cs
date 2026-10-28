namespace Turning.Application.Features.Surveys;

/// <summary>Consulta administrativa de encuestas enviadas por participantes.</summary>
public interface IAdminSurveyResultsService
{
    Task<AdminSurveyResultsPageDto> GetResultsAsync(int page = 1, int pageSize = 50,
        Guid? participantId = null, CancellationToken ct = default);
}
