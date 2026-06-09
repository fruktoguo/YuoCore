using Microsoft.AspNetCore.Mvc;
using YuoCore.Backend.Game;
using YuoCore.Backend.Infrastructure;
using YuoCore.Game;

namespace YuoCore.Backend.Controllers;

[ApiController]
[Route("api/game")]
public sealed class GameController : ControllerBase
{
    private readonly GameRuntime _runtime;
    private readonly GameCommandQueue _queue;
    private readonly SnapshotPersistenceService _persistence;

    public GameController(GameRuntime runtime, GameCommandQueue queue, SnapshotPersistenceService persistence)
    {
        _runtime = runtime;
        _queue = queue;
        _persistence = persistence;
    }

    [HttpPost("players")]
    public async Task<ActionResult<YuoPlayerSnapshotDto>> Create([FromBody] YuoCreatePlayerRequest request, CancellationToken cancellationToken)
    {
        var snapshot = _runtime.CreatePlayer(request.Name);
        await _persistence.SaveAsync(snapshot, cancellationToken);
        return CreatedAtAction(nameof(Get), new { playerId = snapshot.PlayerId }, snapshot);
    }

    [HttpGet("players/{playerId:guid}")]
    public async Task<ActionResult<YuoPlayerSnapshotDto>> Get(Guid playerId, CancellationToken cancellationToken)
    {
        if (_runtime.TryGetSnapshot(playerId, out var live))
        {
            await _persistence.SaveAsync(live, cancellationToken);
            return live;
        }

        var stored = await _persistence.GetAsync(playerId, cancellationToken);
        return stored is null ? NotFound() : stored;
    }

    [HttpPost("players/{playerId:guid}/click")]
    public async Task<IActionResult> Click(Guid playerId, [FromQuery] long amount = 1, CancellationToken cancellationToken = default)
    {
        await _queue.EnqueueAsync(new YuoClickCommand(playerId, amount), cancellationToken);
        return Accepted();
    }

    [HttpPost("seed/{count:int}")]
    public ActionResult<IReadOnlyList<YuoPlayerSnapshotDto>> Seed(int count)
    {
        return Ok(_runtime.SeedPlayers(count));
    }

    [HttpGet("stats")]
    public ActionResult<YuoGameStatsDto> Stats()
    {
        return _runtime.Stats();
    }
}
