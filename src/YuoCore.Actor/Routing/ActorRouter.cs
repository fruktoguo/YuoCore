namespace YuoCore.Actor;

public sealed class ActorRouter(
    IActorLocationRegistry locationRegistry,
    IActorTransport transport)
{
    private readonly IActorLocationRegistry _locationRegistry =
        locationRegistry ?? throw new ArgumentNullException(nameof(locationRegistry));
    private readonly IActorTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public async ValueTask SendAsync(
        IActorMessage message,
        ActorId senderActorId = default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(message);

        var address = await ResolveAddressAsync(message.TargetActorId, cancellationToken)
            .ConfigureAwait(false);

        var envelope = ActorEnvelope.ForMessage(message, senderActorId);
        try
        {
            await _transport.SendAsync(address, envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (ActorException exception) when (exception.Error.Code == 404)
        {
            // Location 是缓存/注册表，不是绝对真理。目标迁移或重启后，旧地址可能还在缓存里。
            // 这里只对 404 做一次失效重查，避免无限重试掩盖真实业务错误。
            await RetrySendAfterInvalidatingLocationAsync(message.TargetActorId, address, envelope, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask<TResponse> InvokeAsync<TResponse>(
        IActorRequest<TResponse> request,
        ActorId senderActorId = default,
        CancellationToken cancellationToken = default)
    {
        return await InvokeCoreAsync(request, senderActorId, null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TResponse> InvokeAsync<TResponse>(
        IActorRequest<TResponse> request,
        TimeSpan timeout,
        ActorId senderActorId = default,
        CancellationToken cancellationToken = default)
    {
        return await InvokeCoreAsync(request, senderActorId, timeout, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<TResponse> InvokeCoreAsync<TResponse>(
        IActorRequest<TResponse> request,
        ActorId senderActorId,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

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

        try
        {
            // Invoke 的 RpcId/RequestId 都在 envelope 中维护，业务 request 不需要携带路由细节。
            // 同进程可以直接投递对象，跨进程传输层会把 envelope/payload 编码成 bytes。
            ActorEnvelope response;
            var envelope = ActorEnvelope.ForRequest(request, senderActorId);
            ActorAddress address = default;
            var hasResolvedAddress = false;
            try
            {
                address = await ResolveAddressAsync(request.TargetActorId, cancellationToken)
                    .ConfigureAwait(false);
                hasResolvedAddress = true;

                response = await _transport.InvokeAsync(
                    address,
                    envelope,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ActorException exception) when (exception.Error.Code == 404 && hasResolvedAddress)
            {
                response = await RetryInvokeAfterInvalidatingLocationAsync(request.TargetActorId, address, envelope, cancellationToken)
                    .ConfigureAwait(false);
            }

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
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true &&
                                                !callerCancellationToken.IsCancellationRequested)
        {
            throw new ActorException(ActorError.Timeout(
                $"Actor request timed out after {timeoutValue.TotalMilliseconds:n0} ms: {request.GetType().FullName}."));
        }
    }

    private async ValueTask RetrySendAfterInvalidatingLocationAsync(
        ActorId actorId,
        ActorAddress staleAddress,
        ActorEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var refreshedAddress = await RefreshLocationAsync(actorId, staleAddress, cancellationToken)
            .ConfigureAwait(false);

        await _transport.SendAsync(refreshedAddress, envelope, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ActorEnvelope> RetryInvokeAfterInvalidatingLocationAsync(
        ActorId actorId,
        ActorAddress staleAddress,
        ActorEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var refreshedAddress = await RefreshLocationAsync(actorId, staleAddress, cancellationToken)
            .ConfigureAwait(false);

        return await _transport.InvokeAsync(refreshedAddress, envelope, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ActorAddress> RefreshLocationAsync(
        ActorId actorId,
        ActorAddress staleAddress,
        CancellationToken cancellationToken)
    {
        if (_locationRegistry is IActorLocationInvalidator invalidator)
        {
            await invalidator.InvalidateAsync(actorId, staleAddress, cancellationToken).ConfigureAwait(false);
        }

        var refreshedAddress = await ResolveAddressAsync(actorId, cancellationToken).ConfigureAwait(false);
        if (refreshedAddress == staleAddress)
        {
            throw new ActorException(ActorError.NotFound($"Actor {actorId} still points to stale address {staleAddress}."));
        }

        return refreshedAddress;
    }

    private async ValueTask<ActorAddress> ResolveAddressAsync(
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var address = await _locationRegistry.ResolveAsync(actorId, cancellationToken)
            .ConfigureAwait(false);

        if (address is null)
        {
            throw new ActorException(ActorError.NotFound($"Actor {actorId} has no registered location."));
        }

        return address.Value;
    }
}
