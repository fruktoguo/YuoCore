using BenchmarkDotNet.Attributes;
using YuoCore.Timers;

namespace YuoCore.Benchmarks;

[MemoryDiagnoser]
public class TimerSchedulerBenchmarks
{
    [Params(1_000, 100_000)]
    public int Timers { get; set; }

    [Benchmark]
    public async ValueTask ScheduleAndFireOnce()
    {
        var scheduler = new YuoTimerScheduler();
        var fired = 0;

        for (var i = 0; i < Timers; i++)
        {
            scheduler.ScheduleOnce(
                TimeSpan.FromMilliseconds(1),
                _ =>
                {
                    fired++;
                    return ValueTask.CompletedTask;
                });
        }

        await scheduler.UpdateAsync(scheduler.CurrentTime.AddMilliseconds(1)).ConfigureAwait(false);
        if (fired != Timers)
        {
            throw new InvalidOperationException($"Expected {Timers} fired timers, got {fired}.");
        }
    }
}

public static class TimerPerfSmokeRunner
{
    public static void Run(string[] args)
    {
        var options = TimerPerfSmokeOptions.Parse(args);
        var scheduler = new YuoTimerScheduler();
        var fired = 0;
        var dueAt = scheduler.CurrentTime.AddMilliseconds(1);
        var sw = new System.Diagnostics.Stopwatch();

        sw.Start();
        for (var i = 0; i < options.Timers; i++)
        {
            scheduler.ScheduleOnce(
                TimeSpan.FromMilliseconds(1),
                _ =>
                {
                    fired++;
                    return ValueTask.CompletedTask;
                });
        }

        sw.Stop();
        var scheduleMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        scheduler.UpdateAsync(dueAt).AsTask().GetAwaiter().GetResult();
        sw.Stop();
        var fireMs = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"timer-smoke: timers={options.Timers:n0}");
        Console.WriteLine($"schedule-ms: {scheduleMs:n3}");
        Console.WriteLine($"fire-ms: {fireMs:n3}");
        Console.WriteLine($"callbacks: {fired:n0}");
        Console.WriteLine($"fire-timer/s: {options.Timers / (fireMs / 1000.0):n0}");

        if (fired != options.Timers)
        {
            throw new InvalidOperationException($"Expected {options.Timers} fired timers, got {fired}.");
        }
    }
}

public sealed record TimerPerfSmokeOptions(int Timers)
{
    public static TimerPerfSmokeOptions Parse(string[] args)
    {
        var timers = 100_000;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--timers")
            {
                timers = int.Parse(args[++i]);
            }
        }

        if (timers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timers), "timers must be greater than zero.");
        }

        return new TimerPerfSmokeOptions(timers);
    }
}
