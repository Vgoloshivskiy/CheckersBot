using Checkers.Application;
using Microsoft.AspNetCore.Mvc;

namespace Checkers.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    private readonly IEngineGateway _engine;

    public HealthController(IEngineGateway engine)
    {
        _engine = engine;
    }

    /// <summary>200 once every engine worker has started and warmed up, 503 until then.</summary>
    [HttpGet("/healthz")]
    public IActionResult Get()
    {
        if (!_engine.IsReady)
        {
            return StatusCode(503, new { ok = false, workers = 0 });
        }

        return Ok(new
        {
            ok = true,
            workers = _engine.WorkerCount,
            engine = _engine.Info.Name,
            databasePieces = _engine.Info.DatabasePieces
        });
    }
}
