using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.Application.Features.Surveys;

namespace Turning.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator,Researcher")]
[Route("api/surveys/results")]
public sealed class AdminSurveyResultsController : ControllerBase
{
    private readonly IAdminSurveyResultsService _service;
    public AdminSurveyResultsController(IAdminSurveyResultsService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(AdminSurveyResultsPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] Guid? participantId = null, CancellationToken ct = default)
    {
        try { return Ok(await _service.GetResultsAsync(page, pageSize, participantId, ct)); }
        catch (ArgumentException)
        {
            return BadRequest(new
            {
                error = "INVALID_SURVEY_RESULTS_QUERY",
                message = "La página debe ser mayor que cero y el tamaño debe estar entre 1 y 100."
            });
        }
    }
}
