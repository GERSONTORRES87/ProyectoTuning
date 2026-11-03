using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Turning.Application.Features.Emotions;

namespace Turning.API.Controllers;

[ApiController]
[Authorize]
[Route("api/sessions/{sessionId:guid}/emotions")]
public sealed class EmotionsController : ControllerBase
{
    private readonly IEmotionService _svc;
    public EmotionsController(IEmotionService svc) => _svc = svc;
    [HttpPost]
    public async Task<IActionResult> Post(Guid sessionId, [FromBody] EmotionPostRequest req, CancellationToken ct)
    {
        try { var (emo, ava) = await _svc.AddAsync(sessionId, new EmotionCreateRequest(req.Emotion, req.Score, req.Source), ct); return CreatedAtAction(nameof(Get), new { sessionId }, new { emo, ava }); }
        catch (InvalidOperationException ex) when (ex.Message=="SESSION_NOT_FOUND") { return NotFound(new { error=ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message is "SESSION_TERMINAL" or "SESSION_CONFLICT") { return Conflict(new { error=ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error=ex.Message }); }
    }
    [HttpGet]
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken ct)
    {
        try { return Ok(await _svc.ListAsync(sessionId, ct)); }
        catch (InvalidOperationException ex) when (ex.Message=="SESSION_NOT_FOUND") { return NotFound(new { error=ex.Message }); }
    }
}
public sealed class EmotionPostRequest { public string? Emotion { get; set; } public double Score { get; set; } = 0.5; public string? Source { get; set; } }
