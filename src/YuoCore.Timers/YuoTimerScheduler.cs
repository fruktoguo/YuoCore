using YuoCore.Abstractions;

namespace YuoCore.Timers;

/// <summary>
/// 手动驱动的轻量计时器调度器。
/// 它不创建后台线程，宿主需要在 Unity Update、单机主循环或服务器 Fiber Tick 中主动调用 UpdateAsync。
/// 这样可以保留 ET TimerComponent 的“归属于当前执行上下文”思想，同时避免单机/Unity 导入隐藏的服务端依赖。
/// </summary>
public sealed class YuoTimerScheduler(
    IYuoClock? clock = null,
    YuoTimerSchedulerOptions? options = null)
{
    private static readonly AsyncLocal<YuoTimerScheduler?> UpdatingScheduler = new();

    private readonly object _gate = new();
    private readonly IYuoClock _clock = clock ?? new SystemYuoClock();
    private readonly YuoTimerSchedulerOptions _options = ValidateOptions(options ?? new YuoTimerSchedulerOptions());
    private readonly Dictionary<long, TimerEntry> _entries = [];
    private readonly PriorityQueue<TimerEntry, TimerPriority> _queue = new();
    private DateTimeOffset _currentTime = (clock ?? new SystemYuoClock()).UtcNow;
    private long _nextId;

    public event EventHandler<YuoTimerFailureEventArgs>? CallbackFailed;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public DateTimeOffset CurrentTime
    {
        get
        {
            lock (_gate)
            {
                return _currentTime;
            }
        }
    }

    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (delay <= TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        return WaitUntilAsync(CurrentTime.Add(delay), cancellationToken);
    }

    public ValueTask WaitUntilAsync(DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentTime = CurrentTime;
        if (dueAt <= currentTime)
        {
            return ValueTask.CompletedTask;
        }

        ThrowIfWaitingOnSelfDuringCallback();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = CreateEntry(dueAt, null, null, completion, cancellationToken);
        return new ValueTask(completion.Task);
    }

    public YuoTimerId ScheduleOnce(
        TimeSpan delay,
        Func<CancellationToken, ValueTask> callback,
        CancellationToken cancellationToken = default)
    {
        return ScheduleAt(CurrentTime.Add(delay <= TimeSpan.Zero ? TimeSpan.Zero : delay), callback, cancellationToken);
    }

    public YuoTimerId ScheduleAt(
        DateTimeOffset dueAt,
        Func<CancellationToken, ValueTask> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        return CreateEntry(dueAt, null, callback, null, cancellationToken).TimerId;
    }

    public YuoTimerId ScheduleRepeated(
        TimeSpan interval,
        Func<CancellationToken, ValueTask> callback,
        bool fireImmediately = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "repeated timer interval must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var now = CurrentTime;
        var dueAt = fireImmediately ? now : now.Add(interval);
        return CreateEntry(dueAt, interval, callback, null, cancellationToken).TimerId;
    }

    public bool Remove(YuoTimerId timerId)
    {
        if (timerId.IsEmpty)
        {
            return false;
        }

        TimerEntry? entry;
        lock (_gate)
        {
            if (!_entries.Remove(timerId.Value, out entry))
            {
                return false;
            }

            entry.Removed = true;
            entry.DisposeCancellation();
        }

        entry.CompleteCanceled();
        return true;
    }

    public ValueTask UpdateAsync(CancellationToken cancellationToken = default)
    {
        return UpdateAsync(_clock.UtcNow, cancellationToken);
    }

    public async ValueTask UpdateAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dueEntries = DequeueDueEntries(now, cancellationToken);

        foreach (var entry in dueEntries)
        {
            if (entry.Completion is not null)
            {
                entry.CompleteWait();
                continue;
            }

            var previousScheduler = UpdatingScheduler.Value;
            UpdatingScheduler.Value = this;
            try
            {
                await InvokeCallbackAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (entry.Interval is not null)
                {
                    Remove(entry.TimerId);
                }

                throw;
            }
            finally
            {
                UpdatingScheduler.Value = previousScheduler;
            }

            RescheduleRepeatedEntry(entry, now);
        }
    }

    private TimerEntry CreateEntry(
        DateTimeOffset dueAt,
        TimeSpan? interval,
        Func<CancellationToken, ValueTask>? callback,
        TaskCompletionSource? completion,
        CancellationToken cancellationToken)
    {
        var id = new YuoTimerId(Interlocked.Increment(ref _nextId));
        var entry = new TimerEntry(id, dueAt, interval, callback, completion, cancellationToken);

        lock (_gate)
        {
            _entries.Add(id.Value, entry);
            Enqueue(entry);
        }

        entry.RegisterCancellation(this);
        return entry;
    }

    private List<TimerEntry> DequeueDueEntries(DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<TimerEntry>? dueEntries = null;

        lock (_gate)
        {
            _currentTime = now;

            if (_options.MaxDueTimersPerUpdate is { } maxDueTimers)
            {
                while ((dueEntries?.Count ?? 0) < maxDueTimers && TryDequeueNextDueEntry(out var entry))
                {
                    dueEntries ??= [];
                    dueEntries.Add(entry);
                }

                return dueEntries ?? [];
            }

            while (TryDequeueNextDueEntry(out var entry))
            {
                dueEntries ??= [];
                dueEntries.Add(entry);
            }

            return dueEntries ?? [];

            bool TryDequeueNextDueEntry(out TimerEntry dueEntry)
            {
                dueEntry = null!;
                while (_queue.TryPeek(out var entry, out var priority) && priority.DueAt <= now)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _queue.Dequeue();

                    if (entry.Removed || !_entries.TryGetValue(entry.TimerId.Value, out var current) || current != entry)
                    {
                        continue;
                    }

                    if (priority.Version != entry.Version)
                    {
                        continue;
                    }

                    if (entry.Interval is null)
                    {
                        _entries.Remove(entry.TimerId.Value);
                        entry.Removed = true;
                        entry.DisposeCancellation();
                    }

                    dueEntry = entry;
                    return true;
                }

                return false;
            }
        }
    }

    private async ValueTask InvokeCallbackAsync(TimerEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Callback is null)
        {
            return;
        }

        try
        {
            await entry.Callback(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!_options.StopOnCallbackError)
        {
            PublishCallbackFailure(entry.TimerId, exception);
            if (entry.Interval is not null && !_options.ContinueRepeatedTimerAfterError)
            {
                Remove(entry.TimerId);
            }
        }
    }

    private void RescheduleRepeatedEntry(TimerEntry entry, DateTimeOffset now)
    {
        if (entry.Interval is null)
        {
            return;
        }

        lock (_gate)
        {
            if (entry.Removed || !_entries.ContainsKey(entry.TimerId.Value))
            {
                return;
            }

            entry.DueAt = now.Add(entry.Interval.Value);
            entry.Version++;
            Enqueue(entry);
        }
    }

    private void Enqueue(TimerEntry entry)
    {
        _queue.Enqueue(entry, new TimerPriority(entry.DueAt, entry.Version, entry.TimerId.Value));
    }

    private void ThrowIfWaitingOnSelfDuringCallback()
    {
        if (!ReferenceEquals(UpdatingScheduler.Value, this))
        {
            return;
        }

        throw new InvalidOperationException(
            "A timer callback cannot await a future delay on the same YuoTimerScheduler. Post work to the owning Fiber or schedule a separate timer instead.");
    }

    private static YuoTimerSchedulerOptions ValidateOptions(YuoTimerSchedulerOptions options)
    {
        if (options.MaxDueTimersPerUpdate is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaxDueTimersPerUpdate),
                "max due timers per update must be positive.");
        }

        return options;
    }

    private void CancelFromToken(TimerEntry entry, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_entries.Remove(entry.TimerId.Value))
            {
                return;
            }

            entry.Removed = true;
        }

        entry.CompleteCanceled(cancellationToken);
    }

    private void PublishCallbackFailure(YuoTimerId timerId, Exception exception)
    {
        var handlers = CallbackFailed;
        if (handlers is null)
        {
            return;
        }

        try
        {
            handlers(this, new YuoTimerFailureEventArgs(timerId, exception));
        }
        catch
        {
            // 失败事件是观测出口，不能让观测代码反过来打断计时器主循环。
        }
    }

    private readonly record struct TimerPriority(DateTimeOffset DueAt, long Version, long Id) : IComparable<TimerPriority>
    {
        public int CompareTo(TimerPriority other)
        {
            var dueAtCompare = DueAt.CompareTo(other.DueAt);
            if (dueAtCompare != 0)
            {
                return dueAtCompare;
            }

            var versionCompare = Version.CompareTo(other.Version);
            return versionCompare != 0 ? versionCompare : Id.CompareTo(other.Id);
        }
    }

    private sealed class TimerEntry(
        YuoTimerId timerId,
        DateTimeOffset dueAt,
        TimeSpan? interval,
        Func<CancellationToken, ValueTask>? callback,
        TaskCompletionSource? completion,
        CancellationToken cancellationToken)
    {
        private CancellationTokenRegistration _cancellationRegistration;

        public YuoTimerId TimerId { get; } = timerId;

        public DateTimeOffset DueAt { get; set; } = dueAt;

        public TimeSpan? Interval { get; } = interval;

        public Func<CancellationToken, ValueTask>? Callback { get; } = callback;

        public TaskCompletionSource? Completion { get; } = completion;

        public bool Removed { get; set; }

        public long Version { get; set; }

        public void RegisterCancellation(YuoTimerScheduler scheduler)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return;
            }

            _cancellationRegistration = cancellationToken.Register(
                static state =>
                {
                    var (owner, entry, token) = ((YuoTimerScheduler, TimerEntry, CancellationToken))state!;
                    owner.CancelFromToken(entry, token);
                },
                (scheduler, this, cancellationToken));
        }

        public void DisposeCancellation()
        {
            _cancellationRegistration.Dispose();
        }

        public void CompleteWait()
        {
            Completion?.TrySetResult();
        }

        public void CompleteCanceled(CancellationToken cancellationToken = default)
        {
            Completion?.TrySetCanceled(cancellationToken);
        }
    }
}
