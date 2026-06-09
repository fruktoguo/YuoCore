using YuoCore.Client;
using YuoCore.Protocol;

namespace YuoCore.Client.StateSync;

/// <summary>
/// 客户端 StateSync ACK 辅助工具。
/// 它只在 Snapshot/Delta 已成功应用到本地 Mirror 后确认 tick；
/// 如果出现缺口、旧包或无效包，必须先走恢复流程，不能提前 ACK。
/// </summary>
public static class YuoClientStateSyncAcks
{
    public static bool ShouldAck(YuoClientStateSyncReceiveResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        return result.IsStateSyncMessage &&
               result.Tick is > 0 &&
               result.ApplyResult?.IsApplied == true;
    }

    public static async ValueTask<StateSyncAckResponse?> AckIfAppliedAsync(
        YuoMmoClient client,
        string playerId,
        YuoClientStateSyncReceiveResult result,
        CancellationToken cancellationToken = default)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(playerId));
        }

        if (!ShouldAck(result))
        {
            return null;
        }

        return await client.AckStateSyncAsync(
            playerId,
            result.Tick!.Value,
            result.StreamId,
            cancellationToken).ConfigureAwait(false);
    }
}
