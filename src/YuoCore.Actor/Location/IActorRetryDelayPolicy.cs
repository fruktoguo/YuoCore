using YuoCore.Location;

namespace YuoCore.Actor;

public enum ActorRetryDelayReason
{
    StaleAddress = 1,
    LocationLocked = 2
}

public sealed record ActorRetryDelayContext(
    YuoLocationKey Key,
    ActorRetryDelayReason Reason,
    int Attempt,
    TimeSpan BaseDelay);

/// <summary>
/// Actor 内部重试的等待时长策略。
/// DelayProvider 负责“如何等待”，DelayPolicy 只负责“这次应该等多久”，因此可以在不改变 Actor.Core 依赖的前提下接入退避和 jitter。
/// </summary>
public interface IActorRetryDelayPolicy
{
    TimeSpan GetDelay(ActorRetryDelayContext context);
}

public sealed class ExponentialActorRetryDelayPolicy : IActorRetryDelayPolicy
{
    private readonly object _gate = new();
    private readonly Random _random;

    public ExponentialActorRetryDelayPolicy(
        TimeSpan? maxDelay = null,
        double jitterRatio = 0,
        Random? random = null)
    {
        if (maxDelay is { } delay && delay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "max delay must be positive.");
        }

        if (jitterRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterRatio), "jitter ratio must be between 0 and 1.");
        }

        MaxDelay = maxDelay;
        JitterRatio = jitterRatio;
        _random = random ?? Random.Shared;
    }

    public TimeSpan? MaxDelay { get; }

    public double JitterRatio { get; }

    public TimeSpan GetDelay(ActorRetryDelayContext context)
    {
        if (context.Attempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context.Attempt), "retry attempt must be positive.");
        }

        if (context.BaseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var exponent = Math.Min(context.Attempt - 1, 30);
        var multiplier = 1L << exponent;
        var ticks = context.BaseDelay.Ticks > long.MaxValue / multiplier
            ? long.MaxValue
            : context.BaseDelay.Ticks * multiplier;

        if (MaxDelay is { } maxDelay)
        {
            ticks = Math.Min(ticks, maxDelay.Ticks);
        }

        if (JitterRatio <= 0)
        {
            return TimeSpan.FromTicks(ticks);
        }

        double randomValue;
        lock (_gate)
        {
            randomValue = _random.NextDouble();
        }

        var factor = 1 + ((randomValue * 2) - 1) * JitterRatio;
        return TimeSpan.FromTicks(Math.Max(0, (long)Math.Round(ticks * factor)));
    }
}
