using YuoCore.Backend.Infrastructure;
using YuoCore.Game;

namespace YuoCore.Backend.Game;

/// <summary>
/// 示例后端的游戏命令处理器。
/// Gateway、HTTP、后台任务只产生 YuoGameCommand；真正如何调用 GameRuntime 集中在这里，避免协议层散落业务分支。
/// </summary>
public sealed class GameCommandProcessor(GameRuntime runtime, SnapshotPersistenceService? persistence = null)
{
    private readonly GameRuntime _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public async ValueTask<YuoGameCommandResult> ApplyAsync(
        YuoGameCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            YuoClickCommand click => ApplyClick(click),
            YuoEnterMapCommand enter => ApplyEnterMap(enter),
            YuoMoveCommand move => ApplyMove(move),
            YuoDisconnectCommand disconnect => ApplyDisconnect(disconnect),
            YuoPersistPlayerCommand persist => await ApplyPersistAsync(persist, cancellationToken).ConfigureAwait(false),
            _ => new YuoGameCommandResult(false, "unsupported_command", $"Unsupported game command: {command.GetType().FullName}.")
        };
    }

    private async ValueTask<YuoGameCommandResult> ApplyPersistAsync(
        YuoPersistPlayerCommand command,
        CancellationToken cancellationToken)
    {
        if (persistence is null)
        {
            return new YuoGameCommandResult(false, "unsupported_command", "persistence service is not configured.");
        }

        if (!_runtime.TryGetSnapshot(command.PlayerId, out var snapshot))
        {
            return YuoGameCommandResult.NotFound("player not found");
        }

        await persistence.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return YuoGameCommandResult.Ok(snapshot);
    }

    private YuoGameCommandResult ApplyClick(YuoClickCommand command)
    {
        if (!_runtime.TryEnqueueClick(command.PlayerId, command.Amount))
        {
            return YuoGameCommandResult.NotFound("player not found");
        }

        // Click 的真实组件修改发生在后续 runtime.Tick 中。
        // 如果调用方等待结果，GameLoopService 会在 tick 后读取 snapshot 再完成响应。
        return YuoGameCommandResult.Ok();
    }

    private YuoGameCommandResult ApplyEnterMap(YuoEnterMapCommand command)
    {
        return _runtime.TryEnterMap(command.PlayerId, command.MapId, out var world)
            ? YuoGameCommandResult.Ok(world: world)
            : YuoGameCommandResult.NotFound("player not found");
    }

    private YuoGameCommandResult ApplyMove(YuoMoveCommand command)
    {
        return _runtime.TryMove(command.PlayerId, command.X, command.Y, command.Z, command.InputSeq, out var world)
            ? YuoGameCommandResult.Ok(world: world)
            : YuoGameCommandResult.NotFound("player not found");
    }

    private YuoGameCommandResult ApplyDisconnect(YuoDisconnectCommand command)
    {
        if (!_runtime.TryDisconnect(command.PlayerId, command.SessionId))
        {
            return YuoGameCommandResult.NotFound("player not found or session mismatch");
        }

        return _runtime.TryGetSnapshot(command.PlayerId, out var snapshot)
            ? YuoGameCommandResult.Ok(snapshot)
            : YuoGameCommandResult.Ok();
    }
}
