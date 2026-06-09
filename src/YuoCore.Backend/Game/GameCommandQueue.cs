using System.Threading.Channels;
using Microsoft.Extensions.Options;
using YuoCore.Game;

namespace YuoCore.Backend.Game;

public sealed record YuoGameCommandResult(
    bool Success,
    string ErrorCode = "",
    string ErrorMessage = "",
    YuoPlayerSnapshotDto? Snapshot = null,
    YuoWorldSnapshotDto? World = null)
{
    public static YuoGameCommandResult Ok(
        YuoPlayerSnapshotDto? snapshot = null,
        YuoWorldSnapshotDto? world = null) =>
        new(true, Snapshot: snapshot, World: world);

    public static YuoGameCommandResult NotFound(string message) =>
        new(false, "not_found", message);
}

/// <summary>
/// 后端宿主内部的排队命令包装。
/// 纯业务 command 仍放在 YuoCore.Game；TaskCompletionSource 只属于 ASP.NET 示例宿主的异步排队边界。
/// </summary>
public sealed class YuoQueuedGameCommand
{
    private readonly TaskCompletionSource<YuoGameCommandResult>? _completion;

    internal YuoQueuedGameCommand(
        YuoGameCommand command,
        TaskCompletionSource<YuoGameCommandResult>? completion)
    {
        Command = command ?? throw new ArgumentNullException(nameof(command));
        _completion = completion;
    }

    public YuoGameCommand Command { get; }

    public bool ExpectsResult => _completion is not null;

    internal Task<YuoGameCommandResult> ResultTask =>
        _completion?.Task ?? Task.FromResult(YuoGameCommandResult.Ok());

    public bool TryComplete(YuoGameCommandResult result) =>
        _completion?.TrySetResult(result) ?? false;

    public bool TryFail(Exception exception) =>
        _completion?.TrySetException(exception) ?? false;
}

public sealed class GameCommandQueue
{
    private readonly Channel<YuoQueuedGameCommand> _channel;

    public GameCommandQueue(IOptions<GameOptions> options)
    {
        _channel = Channel.CreateBounded<YuoQueuedGameCommand>(new BoundedChannelOptions(options.Value.ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <summary>
    /// fire-and-forget 命令入口，适合 HTTP Accepted 或后台批量投递。
    /// 调用方不等待业务结果，但命令仍会在 GameLoopService 所属线程/节奏里执行。
    /// </summary>
    public ValueTask EnqueueAsync(YuoGameCommand command, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(new YuoQueuedGameCommand(command, completion: null), cancellationToken);

    /// <summary>
    /// request/response 命令入口，适合 Gateway 需要把 EnterMap/Move 的业务结果回给客户端。
    /// 如果调用方取消等待，已经入队的命令仍可能被游戏循环处理；业务层要按 inputSeq/sessionId 保持幂等。
    /// </summary>
    public async ValueTask<YuoGameCommandResult> ExecuteAsync(
        YuoGameCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var completion = new TaskCompletionSource<YuoGameCommandResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new YuoQueuedGameCommand(command, completion);

        await _channel.Writer.WriteAsync(queued, cancellationToken).ConfigureAwait(false);
        return await queued.ResultTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ChannelReader<YuoQueuedGameCommand> Reader => _channel.Reader;
}
