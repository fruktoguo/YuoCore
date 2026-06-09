namespace YuoCore.Location;

/// <summary>
/// 稳定业务键，例如 Player:guid、Guild:id、Room:id。
/// 它和运行时 ActorId 分离，便于玩家迁移 shard 后仍然能按业务 ID 找到当前位置。
/// </summary>
public readonly record struct YuoLocationKey(string Type, string Key)
{
    public override string ToString() => $"{Type}:{Key}";
}
