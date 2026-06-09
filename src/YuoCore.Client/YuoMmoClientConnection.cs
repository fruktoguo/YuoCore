using YuoCore.Protocol;

namespace YuoCore.Client;

/// <summary>
/// Web/Unity 共用的 MMO 客户端连接会话。
/// 它只管理当前 transport、YuoMmoClient 和 playerId，不知道 WebSocket、TCP、KCP、Actor 或服务端 Entity。
/// </summary>
public sealed class YuoMmoClientConnection
{
    private readonly Func<CancellationToken, ValueTask<IYuoClientTransport>> _transportFactory;
    private readonly Func<IYuoClientTransport, CancellationToken, ValueTask>? _closeTransportAsync;
    private readonly YuoClientSessionOptions? _sessionOptions;
    private IYuoClientTransport? _transport;
    private YuoMmoClient? _client;

    public YuoMmoClientConnection(
        Func<CancellationToken, ValueTask<IYuoClientTransport>> transportFactory,
        Func<IYuoClientTransport, CancellationToken, ValueTask>? closeTransportAsync = null,
        YuoClientSessionOptions? sessionOptions = null)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _closeTransportAsync = closeTransportAsync;
        _sessionOptions = sessionOptions;
    }

    public string PlayerId { get; private set; } = string.Empty;

    public YuoMmoClient Client =>
        _client ?? throw new InvalidOperationException("YuoMmoClientConnection is not connected.");

    public async ValueTask<Welcome> ConnectAsync(
        string? playerId = null,
        CancellationToken cancellationToken = default)
    {
        await CloseAsync(cancellationToken).ConfigureAwait(false);

        _transport = await _transportFactory(cancellationToken).ConfigureAwait(false);
        _client = new YuoMmoClient(_transport, new YuoClientSession(_sessionOptions ?? YuoClientSessionOptions.Default));

        var welcome = await _client.LoginAsync(playerId ?? PlayerId, cancellationToken).ConfigureAwait(false);
        PlayerId = welcome.PlayerId;
        return welcome;
    }

    public ValueTask<Welcome> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(PlayerId))
        {
            throw new InvalidOperationException("Cannot reconnect before first login.");
        }

        return ConnectAsync(PlayerId, cancellationToken);
    }

    public async ValueTask<PlayerSnapshot> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(PlayerId))
        {
            throw new InvalidOperationException("Cannot disconnect before first login.");
        }

        var snapshot = await Client.DisconnectAsync(PlayerId, cancellationToken: cancellationToken).ConfigureAwait(false);
        await CloseAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        var transport = _transport;
        _transport = null;
        _client = null;

        if (transport is null)
        {
            return;
        }

        if (_closeTransportAsync is not null)
        {
            await _closeTransportAsync(transport, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (transport is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (transport is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
