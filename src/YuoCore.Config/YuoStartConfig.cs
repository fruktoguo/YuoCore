using System.Text.Json;
using YuoCore.Abstractions;

namespace YuoCore.Config;

/// <summary>
/// 进程启动配置，描述当前进程的节点信息、服务列表和 Fiber 分配。
/// 通过 JSON 文件加载，支持注释和尾逗号。
/// </summary>
public sealed record YuoStartConfig
{
    /// <summary>节点标识，用于多节点部署中区分不同物理/虚拟节点。</summary>
    public string NodeId { get; init; } = "local";

    /// <summary>进程标识，在同一节点内应唯一。</summary>
    public string ProcessId { get; init; } = "dev";

    /// <summary>进程种类（如 Game、Login、Gateway）。</summary>
    public string ProcessKind { get; init; } = "Game";

    /// <summary>内部通信端点（如 tcp://0.0.0.0:10000）。</summary>
    public string InnerEndpoint { get; init; } = "";

    /// <summary>外部通信端点（如 tcp://0.0.0.0:20000）。</summary>
    public string OuterEndpoint { get; init; } = "";

    /// <summary>进程级别的自定义元数据。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>此进程承载的服务列表。</summary>
    public List<YuoServiceConfig> Services { get; init; } = [];

    /// <summary>此进程的 Fiber 配置列表。</summary>
    public List<YuoFiberConfig> Fibers { get; init; } = [];
}

/// <summary>
/// 单个服务的启动配置。
/// </summary>
public sealed record YuoServiceConfig
{
    /// <summary>服务名称，在同一进程内应唯一。</summary>
    public string Name { get; init; } = "";

    /// <summary>服务种类（如 "gateway"、"location"、"game"）。</summary>
    public string Kind { get; init; } = "";

    /// <summary>服务端点地址。</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>服务的自定义元数据。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// 单个 Fiber 的启动配置。
/// </summary>
public sealed record YuoFiberConfig
{
    /// <summary>Fiber ID，在进程内必须唯一且为正数。</summary>
    public long Id { get; init; }

    /// <summary>Fiber 的可读名称。</summary>
    public string Name { get; init; } = "";

    /// <summary>Fiber 种类。</summary>
    public string Kind { get; init; } = "Game";

    /// <summary>分片 ID，用于将玩家均匀分布到多个 Fiber。</summary>
    public int ShardId { get; init; }

    /// <summary>是否将此 Fiber 的 World 设为 YuoWorld.Instance 默认实例。</summary>
    public bool SetWorldAsDefault { get; init; }

    /// <summary>每次 Tick 最多处理的 mailbox 消息数。</summary>
    public int MaxMailboxMessagesPerTick { get; init; } = 10_000;

    /// <summary>Fiber 的自定义元数据。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// JSON 启动配置的加载器和校验器。
/// 支持注释和尾逗号，校验规则覆盖所有必填字段和重复检查。
/// </summary>
public static class YuoStartConfigLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 从 JSON 文件加载启动配置。
    /// </summary>
    /// <param name="path">JSON 配置文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="InvalidOperationException">文件为空或解析失败时抛出。</exception>
    public static async ValueTask<YuoStartConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<YuoStartConfig>(stream, Options, cancellationToken)
            .ConfigureAwait(false);
        return config ?? throw new InvalidOperationException($"Start config is empty: {path}");
    }

    /// <summary>
    /// 轻量校验用于测试和启动早失败；JSON Schema 负责对外契约，运行时校验负责防御错误配置。
    /// </summary>
    public static YuoResult Validate(YuoStartConfig? config)
    {
        if (config is null) return YuoResult.Fail(YuoError.Invalid("Start config is required."));
        if (string.IsNullOrWhiteSpace(config.NodeId)) return YuoResult.Fail(YuoError.Invalid("NodeId is required."));
        if (string.IsNullOrWhiteSpace(config.ProcessId)) return YuoResult.Fail(YuoError.Invalid("ProcessId is required."));
        if (string.IsNullOrWhiteSpace(config.ProcessKind)) return YuoResult.Fail(YuoError.Invalid("ProcessKind is required."));
        if (config.Metadata is null) return YuoResult.Fail(YuoError.Invalid("Metadata collection is required."));
        if (ValidateMetadata(config.Metadata, "Start config metadata") is { } metadataError)
        {
            return YuoResult.Fail(YuoError.Invalid(metadataError));
        }

        if (config.Services is null) return YuoResult.Fail(YuoError.Invalid("Services collection is required."));
        if (config.Fibers is null || config.Fibers.Count == 0)
        {
            return YuoResult.Fail(YuoError.Invalid("At least one fiber is required."));
        }

        var serviceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in config.Services)
        {
            if (service is null) return YuoResult.Fail(YuoError.Invalid("Service config item is required."));
            if (string.IsNullOrWhiteSpace(service.Name)) return YuoResult.Fail(YuoError.Invalid("Service name is required."));
            if (string.IsNullOrWhiteSpace(service.Kind)) return YuoResult.Fail(YuoError.Invalid($"Service '{service.Name}' kind is required."));
            if (service.Metadata is null) return YuoResult.Fail(YuoError.Invalid($"Service '{service.Name}' metadata collection is required."));
            if (ValidateMetadata(service.Metadata, $"Service '{service.Name}' metadata") is { } serviceMetadataError)
            {
                return YuoResult.Fail(YuoError.Invalid(serviceMetadataError));
            }

            if (!serviceNames.Add(service.Name)) return YuoResult.Fail(YuoError.Invalid($"Duplicate service name: {service.Name}."));
        }

        var fiberIds = new HashSet<long>();
        foreach (var fiber in config.Fibers)
        {
            if (fiber is null) return YuoResult.Fail(YuoError.Invalid("Fiber config item is required."));
            if (fiber.Id <= 0) return YuoResult.Fail(YuoError.Invalid($"Fiber '{fiber.Name}' id must be positive."));
            if (!fiberIds.Add(fiber.Id)) return YuoResult.Fail(YuoError.Invalid($"Duplicate fiber id: {fiber.Id}."));
            if (string.IsNullOrWhiteSpace(fiber.Name)) return YuoResult.Fail(YuoError.Invalid($"Fiber {fiber.Id} name is required."));
            if (fiber.Metadata is null) return YuoResult.Fail(YuoError.Invalid($"Fiber {fiber.Id} metadata collection is required."));
            if (ValidateMetadata(fiber.Metadata, $"Fiber {fiber.Id} metadata") is { } fiberMetadataError)
            {
                return YuoResult.Fail(YuoError.Invalid(fiberMetadataError));
            }

            if (fiber.MaxMailboxMessagesPerTick <= 0)
            {
                return YuoResult.Fail(YuoError.Invalid(
                    $"Fiber {fiber.Id} max mailbox messages per tick must be positive."));
            }
        }

        return YuoResult.Ok();
    }

    private static string? ValidateMetadata(IReadOnlyDictionary<string, string> metadata, string scope)
    {
        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return $"{scope} key cannot be empty.";
            }

            if (value is null)
            {
                return $"{scope} value cannot be null. Key: {key}.";
            }
        }

        return null;
    }
}
