using YuoCore.Ecs;
using YuoCore.Runtime;

namespace YuoCore.Runtime.Tests;

public sealed class YuoRuntimeTests
{
    [Fact]
    public void ExplicitWorldEntitiesStayInTheirOwningWorld()
    {
        var worldA = new YuoWorld();
        var worldB = new YuoWorld();
        worldA.OnInit();
        worldB.OnInit(false);

        try
        {
            var entityA = new YuoEntity(worldA);
            var entityB = new YuoEntity(worldB);

            Assert.Same(worldA, entityA.World);
            Assert.Same(worldB, entityB.World);
            Assert.True(worldA.Entities.ContainsKey(entityA.EntityData.Id));
            Assert.False(worldA.Entities.ContainsKey(entityB.EntityData.Id));
            Assert.True(worldB.Entities.ContainsKey(entityB.EntityData.Id));
            Assert.False(worldB.Entities.ContainsKey(entityA.EntityData.Id));
        }
        finally
        {
            worldB.OnDestroy();
            worldA.OnDestroy();
        }
    }

    [Fact]
    public void DefaultWorldConstructorStillSupportsSinglePlayerStyle()
    {
        var world = new YuoWorld();
        world.OnInit();

        try
        {
            var entity = new YuoEntity();

            Assert.Same(world, YuoWorld.Instance);
            Assert.Same(world, entity.World);
            Assert.True(world.Entities.ContainsKey(entity.EntityData.Id));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public async Task FiberTickDrivesOwnedWorldUpdateSystems()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 1,
            Name = "test-game-shard",
            SetWorldAsDefault = false
        });

        var entity = new YuoEntity(fiber.World);
        var counter = entity.AddComponent<TestCounterComponent>();

        await fiber.TickAsync();
        await fiber.TickAsync();

        Assert.Equal(2, counter.Updates);
    }

    [Fact]
    public async Task FiberMailboxRunsBeforeWorldUpdate()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 2,
            Name = "mailbox-before-update",
            SetWorldAsDefault = false
        });

        var entity = new YuoEntity(fiber.World);
        var counter = entity.AddComponent<TestCounterComponent>();

        await fiber.PostAsync(f => counter.Pending += 10);
        await fiber.TickAsync();

        Assert.Equal(10, counter.Applied);
        Assert.Equal(1, counter.Updates);
    }

    [Fact]
    public async Task FiberMailboxBudgetSplitsBurstAcrossTicks()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 3,
            Name = "mailbox-budget",
            SetWorldAsDefault = false,
            MaxMailboxMessagesPerTick = 1
        });

        var entity = new YuoEntity(fiber.World);
        var counter = entity.AddComponent<TestCounterComponent>();

        await fiber.PostAsync(_ => counter.Pending += 1);
        await fiber.PostAsync(_ => counter.Pending += 10);
        await fiber.PostAsync(_ => counter.Pending += 100);

        await fiber.TickAsync();
        Assert.Equal(1, counter.Applied);
        Assert.Equal(1, counter.Updates);

        await fiber.TickAsync();
        Assert.Equal(11, counter.Applied);
        Assert.Equal(2, counter.Updates);

        await fiber.TickAsync();
        Assert.Equal(111, counter.Applied);
        Assert.Equal(3, counter.Updates);
    }

    [Fact]
    public async Task FiberCallCanceledBeforeDispatchDoesNotRunQueuedAction()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 4,
            Name = "call-cancel-before-dispatch",
            SetWorldAsDefault = false
        });
        var executed = 0;
        using var cts = new CancellationTokenSource();

        var callTask = fiber.CallAsync<int>((_, _) =>
        {
            executed++;
            return ValueTask.FromResult(1);
        }, cts.Token).AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await callTask);
        await fiber.TickAsync();

        Assert.Equal(0, executed);
    }

    [Fact]
    public async Task FiberDrainCancellationBeforeReadDoesNotDropQueuedPost()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 5,
            Name = "drain-cancel-before-read",
            SetWorldAsDefault = false
        });
        var executed = 0;
        await fiber.PostAsync(_ => executed++);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fiber.DrainMailboxAsync(cts.Token));
        await fiber.DrainMailboxAsync();

        Assert.Equal(1, executed);
    }

    [Fact]
    public async Task RuntimeCanTickMultipleFibersInParallel()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiberA = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 11, Name = "parallel-a" });
        var fiberB = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 12, Name = "parallel-b" });
        var counterA = new YuoEntity(fiberA.World).AddComponent<TestCounterComponent>();
        var counterB = new YuoEntity(fiberB.World).AddComponent<TestCounterComponent>();

        await runtime.TickParallelAsync(maxConcurrency: 2);

        Assert.Equal(1, counterA.Updates);
        Assert.Equal(1, counterB.Updates);
        Assert.NotSame(fiberA.World, fiberB.World);
    }

    [Fact]
    public async Task FiberManagerTickAllThrowsAfterDispose()
    {
        var manager = new YuoFiberManager();
        manager.CreateFiber(new YuoFiberOptions { Id = 21, Name = "disposed-manager" });
        manager.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await manager.TickAllAsync());
    }

    [Fact]
    public void FiberManagerRejectsNullOptions()
    {
        using var manager = new YuoFiberManager();

        Assert.Throws<ArgumentNullException>(() => manager.CreateFiber((YuoFiberOptions)null!));
    }

    [Fact]
    public void FiberManagerDoesNotReuseRemovedExplicitFiberId()
    {
        using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 31, Name = "temporary" });

        Assert.True(runtime.Fibers.RemoveFiber(fiber.Id));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 31, Name = "reuse" }));

        Assert.Contains("used before", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoFiberIdsSkipIdsThatWereReservedExplicitly()
    {
        using var runtime = new YuoCoreRuntime();
        var explicitFiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 1, Name = "explicit" });

        var autoFiber = runtime.Fibers.CreateFiber("auto");

        Assert.NotEqual(explicitFiber.Id, autoFiber.Id);
        Assert.True(autoFiber.Id > 1);
    }

    [Fact]
    public void ChildEntitiesInheritParentWorld()
    {
        var world = new YuoWorld();
        world.OnInit(false);

        try
        {
            var parent = new YuoEntity(world);
            var child = parent.AddChild();

            Assert.Same(world, child.World);
            Assert.True(world.Entities.ContainsKey(child.EntityData.Id));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void ComponentLocatorsAreScopedPerWorld()
    {
        var worldA = new YuoWorld();
        var worldB = new YuoWorld();
        worldA.OnInit();
        worldB.OnInit(false);

        try
        {
            var componentA = YuoComponentCachedLocator<TestLocatorComponent>.GetFrom(worldA);
            var componentB = YuoComponentCachedLocator<TestLocatorComponent>.GetFrom(worldB);

            Assert.NotSame(componentA, componentB);
            Assert.Same(worldA, componentA.Entity.World);
            Assert.Same(worldB, componentB.Entity.World);
        }
        finally
        {
            worldB.OnDestroy();
            worldA.OnDestroy();
        }
    }

    public sealed class TestCounterComponent : YuoComponent
    {
        public int Pending;
        public int Applied;
        public int Updates;
    }

    public sealed class TestLocatorComponent : YuoComponent
    {
    }

    public sealed class TestCounterSystem : YuoSystem<TestCounterComponent>, IUpdate
    {
        public override void Run(TestCounterComponent component)
        {
            component.Updates++;
            component.Applied += component.Pending;
            component.Pending = 0;
        }
    }

}
