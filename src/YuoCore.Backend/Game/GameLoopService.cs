using Microsoft.Extensions.Options;
using YuoCore.Game;

namespace YuoCore.Backend.Game;

public sealed class GameLoopService : BackgroundService
{
    private readonly GameRuntime _runtime;
    private readonly GameCommandQueue _queue;
    private readonly GameCommandProcessor _processor;
    private readonly ILogger<GameLoopService> _logger;
    private readonly GameOptions _options;
    private long _ticksSincePersist;

    public GameLoopService(
        GameRuntime runtime,
        GameCommandQueue queue,
        GameCommandProcessor processor,
        IOptions<GameOptions> options,
        ILogger<GameLoopService> logger)
    {
        _runtime = runtime;
        _queue = queue;
        _processor = processor;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _runtime.Start();
        var period = TimeSpan.FromSeconds(1d / Math.Max(1, _options.TickRate));
        using var timer = new PeriodicTimer(period);
        _logger.LogInformation("Game loop started at {TickRate} ticks/s", _options.TickRate);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            List<YuoQueuedGameCommand>? completeAfterTick = null;
            while (_queue.Reader.TryRead(out var queued))
            {
                if (await ApplyAsync(queued, stoppingToken) is { } deferred)
                {
                    (completeAfterTick ??= []).Add(deferred);
                }
            }

            _runtime.Tick();

            if (completeAfterTick is not null)
            {
                CompleteAfterTick(completeAfterTick);
            }

            if (_options.PersistEveryTicks > 0 && ++_ticksSincePersist >= _options.PersistEveryTicks)
            {
                _ticksSincePersist = 0;
                await PersistOnlinePlayersAsync(stoppingToken);
            }
        }
    }

    private async Task PersistOnlinePlayersAsync(CancellationToken cancellationToken)
    {
        foreach (var snapshot in _runtime.SnapshotOnlinePlayers())
        {
            try
            {
                var result = await _processor.ApplyAsync(
                    new YuoPersistPlayerCommand(snapshot.PlayerId),
                    cancellationToken);
                if (!result.Success)
                {
                    _logger.LogWarning(
                        "Periodic persist skipped player {PlayerId}: {Error}",
                        snapshot.PlayerId,
                        result.ErrorMessage);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic persist failed for player {PlayerId}", snapshot.PlayerId);
            }
        }
    }

    private async Task<YuoQueuedGameCommand?> ApplyAsync(
        YuoQueuedGameCommand queued,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processor.ApplyAsync(queued.Command, cancellationToken);
            if (queued.ExpectsResult &&
                queued.Command is YuoClickCommand &&
                result.Success)
            {
                return queued;
            }

            queued.TryComplete(result);
        }
        catch (Exception ex)
        {
            queued.TryFail(ex);
            _logger.LogError(ex, "Failed to apply game command {CommandType}", queued.Command.GetType().FullName);
        }

        return null;
    }

    private void CompleteAfterTick(IReadOnlyList<YuoQueuedGameCommand> queuedCommands)
    {
        foreach (var queued in queuedCommands)
        {
            try
            {
                if (queued.Command is not YuoClickCommand click)
                {
                    continue;
                }

                var result = _runtime.TryGetSnapshot(click.PlayerId, out var snapshot)
                    ? YuoGameCommandResult.Ok(snapshot)
                    : YuoGameCommandResult.NotFound("player not found");
                queued.TryComplete(result);
            }
            catch (Exception ex)
            {
                queued.TryFail(ex);
                _logger.LogError(ex, "Failed to complete game command {CommandType} after tick", queued.Command.GetType().FullName);
            }
        }
    }
}
