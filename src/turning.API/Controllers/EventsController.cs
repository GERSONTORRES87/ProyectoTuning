using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.Application.Features.Events;
using Turning.API.Filters;

namespace Turning.API.Controllers;
[ApiController]
[Authorize]
[Route("api/sessions/{sessionId:guid}/events")]
[AllowSessionInterlocutor]
public sealed class EventsController : ControllerBase
{
    private readonly IEventService _svc;
    public EventsController(IEventService svc) => _svc = svc;
    [HttpGet]
    public async Task<IActionResult> Get(Guid sessionId, [FromQuery] Guid? after, CancellationToken ct)
    {
        try { var list = await _svc.ListAsync(sessionId, after, ct, User.IsInRole("Researcher") || User.IsInRole("Administrator")); return Ok(list); }
        catch (InvalidOperationException ex) when (ex.Message=="SESSION_NOT_FOUND") { return NotFound(new { error=ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message=="EVENT_EXPIRED") { return StatusCode(410, new { error=ex.Message }); }
    }
}
