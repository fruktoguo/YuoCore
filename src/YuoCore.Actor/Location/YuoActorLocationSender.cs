using System.Collections.Concurrent;
using YuoCore.Location;

namespace YuoCore.Actor;

/// <summary>
/// 按稳定业务 LocationKey 向 Actor 发送消息。
/// 它吸收 ET MessageLocationSender 的核心思想：业务侧不缓存运行时地址，Sender 缓存地址并在 stale endpoint 时失效重查。
/// 当前实现只依赖 Location 抽象和 Actor transport，因此仍属于同进程/同机器可用能力；跨机器 transport 后续作为可选模块接入。
/// </summary>
public sealed class YuoActorLocationSender(
    IYuoLocationRegistry locations,
    IActorTransport transport,
    YuoActorLocationSenderOptions? options = null)
{
    private readonly IYuoLocationRegistry _locations = locations ?? throw new ArgumentNullException(nameof(locations));
    private readonly IActorTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly ConcurrentDictionary<YuoLocationKey, YuoActorAddress> _addressCache = new();
    private readonly YuoActorLocationSenderOptions _options = ValidateOptions(options ?? new YuoActorLocationSenderOptions());
    private readonly IActorDelayProvider _delayProvider =
        (options ?? new YuoActorLocationSenderOptions()).DelayProvider ?? TaskActorDelayProvider.Instance;

    public async ValueTask SendAsync(
        YuoLocationKey key,
        Func<ActorId, IActorMessage> messageFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageFactory);

        await ExecuteWithLocationRetryAsync(
            key,
            async address =>
            {
                var envelope = ActorEnvelope.ForMessage(CreateMessage(address, messageFactory));
                await _transport.SendAsync(ToActorAddress(address), envelope, cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TResponse> InvokeAsync<TResponse>(
        YuoLocationKey key,
        Func<ActorId, IActorRequest<TResponse>> requestFactory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);

        var callerCancellationToken = cancellationToken;
        var timeoutValue = timeout.GetValueOrDefault();
        using var timeoutCts = timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;

        if (timeoutCts is not null)
        {
            timeoutCts.CancelAfter(timeoutValue);
            cancellationToken = timeoutCts.Token;
        }

        ActorEnvelope response;
        try
        {
            response = await ExecuteWithLocationRetryAsync(
                key,
                address =>
                {
                    var envelope = ActorEnvelope.ForRequest(CreateRequest(address, requestFactory));
                    return _transport.InvokeAsync(ToActorAddress(address), envelope, cancellationToken);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
        {
            if (callerCancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new ActorException(ActorError.Timeout(
                $"Actor location request timed out after {timeoutValue.TotalMilliseconds:n0} ms: {key}."));
        }

        return UnwrapResponse<TResponse>(response);
    }

    public void Invalidate(YuoLocationKey key, YuoActorAddress? staleAddress = null)
    {
        if (staleAddress is null)
        {
            _addressCache.TryRemove(key, out _);
        }
        else if (_addressCache.TryGetValue(key, out var cached) && cached == staleAddress.Value)
        {
            _addressCache.TryRemove(key, out _);
        }

        if (_locations is IYuoLocationCacheInvalidator invalidator)
        {
            invalidator.Invalidate(key, staleAddress);
        }
    }

    private async ValueTask<TResult> ExecuteWithLocationRetryAsync<TResult>(
        YuoLocationKey key,
        Func<YuoActorAddress, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var address = await ResolveAsync(key, cancellationToken).ConfigureAwait(false);
            try
            {
                return await operation(address).ConfigureAwait(false);
            }
            catch (ActorException exception) when (exception.Error.Code == 404 && attempt < _options.MaxStaleRetries)
            {
                Invalidate(key, address);
                await DelayBeforeRetryAsync(
                    key,
                    ActorRetryDelayReason.StaleAddress,
                    attempt + 1,
                    _options.RetryDelay,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<YuoActorAddress> ResolveAsync(
        YuoLocationKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_addressCache.TryGetValue(key, out var cached))
        {
            if (!_options.ValidateCachedAddressOnUse)
            {
                return cached;
            }

            var cachedEntry = await _locations.ResolveAsync(key, cancellationToken).ConfigureAwait(false);
            if (cachedEntry is null)
            {
                _addressCache.TryRemove(key, out _);
                throw new ActorException(ActorError.NotFound($"Location {key} has no registered actor address."));
            }

            if (!cachedEntry.IsLocked)
            {
                _addressCache[key] = cachedEntry.Address;
                return cachedEntry.Address;
            }

            // 本地 sender 缓存不能绕过迁移锁。命中 locked entry 后丢弃旧地址，
            // 交给下面的 locked retry 逻辑等待迁移完成或返回冲突。
            _addressCache.TryRemove(key, out _);
        }

        for (var attempt = 0; ; attempt++)
        {
            var entry = await _locations.ResolveAsync(key, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                throw new ActorException(ActorError.NotFound($"Location {key} has no registered actor address."));
            }

            if (!entry.IsLocked)
            {
                _addressCache[key] = entry.Address;
                return entry.Address;
            }

            if (attempt >= _options.MaxLockedResolveRetries)
            {
                throw new ActorException(ActorError.Conflict($"Location {key} is locked by {entry.LockOwner}."));
            }

            await DelayBeforeRetryAsync(
                key,
                ActorRetryDelayReason.LocationLocked,
                attempt + 1,
                _options.LockedResolveRetryDelay,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask DelayBeforeRetryAsync(
        YuoLocationKey key,
        ActorRetryDelayReason reason,
        int attempt,
        TimeSpan baseDelay,
        CancellationToken cancellationToken)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        var delay = _options.RetryDelayPolicy?.GetDelay(new ActorRetryDelayContext(
            key,
            reason,
            attempt,
            baseDelay)) ?? baseDelay;

        return delay <= TimeSpan.Zero
            ? ValueTask.CompletedTask
            : _delayProvider.DelayAsync(delay, cancellationToken);
    }

    private static IActorMessage CreateMessage(
        YuoActorAddress address,
        Func<ActorId, IActorMessage> factory)
    {
        var actorId = new ActorId(address.ActorId);
        var message = factory(actorId);
        if (message is null)
        {
            throw new ActorException(ActorError.BadRequest("Message factory returned null."));
        }

        if (message.TargetActorId != actorId)
        {
            throw new ActorException(ActorError.BadRequest($"Message target {message.TargetActorId} does not match location actor {actorId}."));
        }

        return message;
    }

    private static IActorRequest<TResponse> CreateRequest<TResponse>(
        YuoActorAddress address,
        Func<ActorId, IActorRequest<TResponse>> factory)
    {
        var actorId = new ActorId(address.ActorId);
        var request = factory(actorId);
        if (request is null)
        {
            throw new ActorException(ActorError.BadRequest("Request factory returned null."));
        }

        if (request.TargetActorId != actorId)
        {
            throw new ActorException(ActorError.BadRequest($"Request target {request.TargetActorId} does not match location actor {actorId}."));
        }

        return request;
    }

    private static TResponse UnwrapResponse<TResponse>(ActorEnvelope response)
    {
        if (response.Error is not null)
        {
            throw new ActorException(response.Error);
        }

        if (response.Message is TResponse typed)
        {
            return typed;
        }

        if (response.Message is null && default(TResponse) is null)
        {
            return default!;
        }

        throw new ActorException(ActorError.Internal(
            $"Actor response type mismatch. Expected {typeof(TResponse).FullName}, got {response.Message?.GetType().FullName ?? "null"}."));
    }

    private static ActorAddress ToActorAddress(YuoActorAddress address)
    {
        return new ActorAddress(address.NodeId, checked((int)address.FiberId), new ActorId(address.ActorId), address.Endpoint);
    }

    private static YuoActorLocationSenderOptions ValidateOptions(YuoActorLocationSenderOptions options)
    {
        if (options.MaxStaleRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxStaleRetries), "max stale retries cannot be negative.");
        }

        if (options.RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.RetryDelay), "retry delay cannot be negative.");
        }

        if (options.MaxLockedResolveRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxLockedResolveRetries), "max locked resolve retries cannot be negative.");
        }

        if (options.LockedResolveRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.LockedResolveRetryDelay), "locked resolve retry delay cannot be negative.");
        }

        return options;
    }
}

public sealed record YuoActorLocationSenderOptions(
    int MaxStaleRetries = 1,
    TimeSpan RetryDelay = default,
    int MaxLockedResolveRetries = 0,
    TimeSpan LockedResolveRetryDelay = default,
    IActorDelayProvider? DelayProvider = null,
    IActorRetryDelayPolicy? RetryDelayPolicy = null,
    bool ValidateCachedAddressOnUse = true);
