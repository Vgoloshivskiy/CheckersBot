using System.Diagnostics;
using Checkers.Api.Contracts;
using Checkers.Application;
using Microsoft.AspNetCore.Mvc;

namespace Checkers.Api.Controllers;

[ApiController]
[Route("v1/move")]
public sealed class MoveController : ControllerBase
{
    private readonly MoveSuggestService _suggest;
    private readonly PositionService _positions;
    private readonly ILogger<MoveController> _logger;

    public MoveController(MoveSuggestService suggest, PositionService positions, ILogger<MoveController> logger)
    {
        _suggest = suggest;
        _positions = positions;
        _logger = logger;
    }

    /// <summary>
    /// Best move for a position. 422 for an invalid request, 503 while the engine is starting,
    /// 504 when the hard time limit is hit, 500 if the engine fails.
    /// </summary>
    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest([FromBody] SuggestRequest request, CancellationToken requestAborted)
    {
        long started = Stopwatch.GetTimestamp();

        PreparedSuggest prepared;
        try
        {
            var limits = request.Limits is null
                ? null
                : new LimitsRequest(request.Limits.MaxDepth, request.Limits.SoftTimeMs, request.Limits.HardTimeMs);
            prepared = _suggest.Prepare(
                request.GameId, request.State?.Notation, request.State?.Position, request.Level, limits);
        }
        catch (RequestValidationException ex)
        {
            _logger.LogInformation(
                "suggest requestId={RequestId} status=422 code={Code}", HttpContext.TraceIdentifier, ex.Code);
            return ProblemFor(422, "Invalid request", ex.Code, ex.Message);
        }

        // The hard limit is enforced here, on the whole request including any wait for a free worker.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        timeout.CancelAfter(prepared.Limits.HardTime);

        try
        {
            var result = await _suggest.SuggestAsync(prepared, timeout.Token);

            _logger.LogInformation(
                "suggest requestId={RequestId} timeMs={TimeMs} depth={Depth} nodes={Nodes} tablebaseHit={TablebaseHit} level={Level} cached={Cached}",
                HttpContext.TraceIdentifier,
                result.TimeMs,
                result.Depth,
                result.Nodes,
                result.TablebaseHit,
                prepared.Limits.Level,
                result.Cached);

            return Ok(new SuggestResponse(
                "kingsrow",
                result.BestMove,
                result.Pv,
                result.ScoreOrWdl,
                result.Depth,
                result.Nodes,
                result.PositionKey,
                new SuggestInfo(result.TablebaseHit, result.TimeMs, result.Cached)));
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(
                "suggest requestId={RequestId} timeMs={TimeMs} status=504",
                HttpContext.TraceIdentifier,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            return ProblemFor(
                504,
                "Engine timeout",
                "engine_timeout",
                $"The engine did not answer within {(int)prepared.Limits.HardTime.TotalMilliseconds} ms.");
        }
        catch (EngineUnavailableException ex)
        {
            _logger.LogWarning(ex, "suggest requestId={RequestId} status=503", HttpContext.TraceIdentifier);
            return ProblemFor(503, "Engine unavailable", "engine_unavailable", ex.Message);
        }
        catch (EngineFailureException ex)
        {
            _logger.LogError(ex, "suggest requestId={RequestId} status=500", HttpContext.TraceIdentifier);
            return ProblemFor(500, "Engine failure", "engine_failure", ex.Message);
        }
    }

    /// <summary>Is this move legal in this position? Invalid PDN gives 422; an illegal move gives legal=false.</summary>
    [HttpPost("validate")]
    public IActionResult Validate([FromBody] ValidateRequest request)
    {
        try
        {
            var result = _positions.Validate(request.Position, request.Move);
            return Ok(new ValidateResponse(result.Legal, result.Reason));
        }
        catch (RequestValidationException ex)
        {
            return ProblemFor(422, "Invalid position", ex.Code, ex.Message);
        }
    }

    /// <summary>Extra endpoint for the test board: every legal move with the position it leads to.</summary>
    [HttpPost("legal")]
    public IActionResult Legal([FromBody] LegalMovesRequest request)
    {
        try
        {
            var result = _positions.GetLegalMoves(request.Position);
            return Ok(new LegalMovesResponse(
                result.Position,
                result.SideToMove.ToPdnLetter(),
                result.Moves.Select(m => new LegalMoveDto(m.Move, m.Path, m.Captured, m.ResultPosition)).ToList(),
                result.GameOver,
                result.Winner?.ToPdnLetter()));
        }
        catch (RequestValidationException ex)
        {
            return ProblemFor(422, "Invalid position", ex.Code, ex.Message);
        }
    }

    private ObjectResult ProblemFor(int status, string title, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"urn:checkers-api:{code}"
        };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
