using Google.Protobuf;
using YuoCore.Game;
using YuoCore.Protocol;
using YuoCore.StateSync;

namespace YuoCore.Backend.Game;

/// <summary>
/// Backend 示例专用的游戏状态投影器。
/// 它负责把业务 DTO 转成网络同步实体，避免 StateSync 核心反向依赖 Game/Protocol。
/// </summary>
internal static class GameStateSyncProjection
{
    public static YuoEntityState ToEntityState(YuoPlayerWorldStateDto state)
    {
        var payload = ToPlayerWorldState(state).ToByteArray();
        return new YuoEntityState(
            state.UnitId,
            "player",
            ToStateVersion(state),
            payload);
    }

    public static NetworkEntityState ToNetworkEntityState(YuoPlayerWorldStateDto state)
    {
        return new NetworkEntityState
        {
            NetworkId = state.UnitId,
            Kind = "player",
            Version = ToStateVersion(state),
            Payload = ByteString.CopyFrom(ToPlayerWorldState(state).ToByteArray())
        };
    }

    public static PlayerWorldState ToPlayerWorldState(YuoPlayerWorldStateDto state)
    {
        return new PlayerWorldState
        {
            PlayerId = state.PlayerId.ToString(),
            Name = state.Name,
            MapId = state.MapId,
            UnitId = state.UnitId,
            X = state.X,
            Y = state.Y,
            Z = state.Z,
            Online = state.Online,
            LastInputSeq = state.LastInputSeq,
            UpdatedUnixMs = state.UpdatedAt.ToUnixTimeMilliseconds()
        };
    }

    private static uint ToStateVersion(YuoPlayerWorldStateDto state)
    {
        return state.LastInputSeq == 0
            ? 1U
            : checked((uint)Math.Min(uint.MaxValue, state.LastInputSeq));
    }
}
