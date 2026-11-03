using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.Application.Features.Surveys;
namespace Turning.API.Controllers;
[ApiController]
[Authorize]
[Route("api/sessions/{sessionId:guid}/survey")]
public sealed class SurveyController : ControllerBase
{
    private readonly ISurveyService _svc;
    public SurveyController(ISurveyService svc) => _svc = svc;
    [HttpGet]
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken ct)
    {
        try { var d = await _svc.GetForSessionAsync(sessionId, ct); return Ok(d); }
        catch (InvalidOperationException ex) when (ex.Message=="SESSION_NOT_FOUND") { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message=="SURVEY_NOT_AVAILABLE") { return BadRequest(new { error=ex.Message }); }
    }
    [HttpPost("responses")]
    public async Task<IActionResult> PostResponse(Guid sessionId, [FromBody] SurveySubmitRequest req, CancellationToken ct)
    {
        try { var id = await _svc.SubmitAsync(sessionId, req, ct); return CreatedAtAction(nameof(Get), new { sessionId }, new { responseId = id }); }
        catch (InvalidOperationException ex) when (ex.Message=="SESSION_NOT_FOUND") { return NotFound(new { error=ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message=="SURVEY_NOT_FOUND"||ex.Message=="SURVEY_NOT_AVAILABLE") { return BadRequest(new { error=ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message=="ALREADY_SUBMITTED") { return Conflict(new { error=ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error=ex.Message }); }
    }
}
