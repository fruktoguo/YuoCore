using YuoCore.Location;

namespace YuoCore.Actor.Tests;

/// <summary>
/// 回归：Actor→Location 适配器不再硬编码 ProcessId，宿主可配置进程标识。
/// </summary>
public sealed class YuoLocationActorRegistryTests
{
    [Fact]
    public async Task Register_writes_configured_process_id()
    {
        var inner = new InMemoryYuoLocationRegistry();
        var registry = new YuoLocationActorRegistry(inner, "proc-a");
        var actorId = new ActorId(5001);

        await registry.RegisterAsync(actorId, new ActorAddress("node-1", 3, actorId));

        var entry = await inner.ResolveAsync(new YuoLocationKey("actor", "5001"));
        Assert.NotNull(entry);
        Assert.Equal("proc-a", entry.Address.ProcessId);
        Assert.Equal("node-1", entry.Address.NodeId);
    }

    [Fact]
    public async Task Resolve_roundtrips_actor_address()
    {
        var inner = new InMemoryYuoLocationRegistry();
        var registry = new YuoLocationActorRegistry(inner, "proc-b");
        var actorId = new ActorId(5002);
        var address = new ActorAddress("node-2", 7, actorId, "tcp://127.0.0.1:3000");

        await registry.RegisterAsync(actorId, address);
        var resolved = await registry.ResolveAsync(actorId);

        Assert.Equal(address, resolved);
    }
}
