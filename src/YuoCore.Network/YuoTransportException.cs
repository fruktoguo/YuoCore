namespace YuoCore.Network;

/// <summary>
/// 传输层异常，当端点未注册或传输不可达时抛出。
/// </summary>
public sealed class YuoTransportException(string message) : Exception(message);
