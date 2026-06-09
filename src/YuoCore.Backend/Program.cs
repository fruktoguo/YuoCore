using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using YuoCore.Backend.Game;
using YuoCore.Backend.Infrastructure;
using YuoCore.Backend.WebSockets;
using Serilog;
using StackExchange.Redis;
using YuoCore.Gateway;
using YuoCore.Persistence;
using YuoCore.Persistence.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.Configure<GameOptions>(builder.Configuration.GetSection("Game"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<GameWebSocketPushOptions>(builder.Configuration.GetSection("Gateway:WebSocketPush"));
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<GameRuntime>();
builder.Services.AddSingleton<GameCommandQueue>();
builder.Services.AddSingleton<GameCommandProcessor>();
builder.Services.AddSingleton<GameClientMessageDispatcher>();
builder.Services.AddSingleton<GameStateSyncPushDispatcher>();
builder.Services.AddSingleton<GameAoiStreamPushCoordinator>();
builder.Services.AddSingleton<YuoGatewaySessionManager>();
builder.Services.AddSingleton<IYuoGatewayDispatcher, GameGatewayProtobufDispatcher>();
builder.Services.AddSingleton<YuoGatewayDispatchPipeline>();
builder.Services.AddHostedService<GameLoopService>();
builder.Services.AddSingleton<SnapshotPersistenceService>();

var storage = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
if (storage.DurableOutboxEnabled && !storage.PostgresEnabled)
{
    throw new InvalidOperationException("Storage:DurableOutboxEnabled requires Storage:PostgresEnabled in the sample backend.");
}

if (storage.PostgresEnabled)
{
    builder.Services.AddSingleton(NpgsqlDataSource.Create(storage.PostgresConnectionString));
    builder.Services.AddSingleton<IPlayerSnapshotRepository, PostgresPlayerSnapshotRepository>();
    builder.Services.AddHostedService<PostgresSchemaMigrator>();
}
else
{
    builder.Services.AddSingleton<IPlayerSnapshotRepository, NoopPlayerSnapshotRepository>();
}

if (storage.RedisEnabled)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(storage.RedisConnectionString));
    builder.Services.AddSingleton<IPlayerSnapshotCache, RedisPlayerSnapshotCache>();
}
else
{
    builder.Services.AddSingleton<IPlayerSnapshotCache, NoopPlayerSnapshotCache>();
}

if (storage.DurableOutboxEnabled)
{
    builder.Services.AddSingleton(new YuoPersistenceQueue(storage.PersistenceQueueCapacity));
    builder.Services.AddSingleton<YuoPersistenceQueueMetrics>();
    builder.Services.AddHostedService<YuoPersistenceQueueMetricsHostedService>();
    builder.Services.AddSingleton<IYuoPersistenceOutbox>(_ =>
        new YuoFilePersistenceOutbox(storage.OutboxDirectory, "backend-player-snapshots"));
    builder.Services.AddSingleton<YuoDurablePersistenceQueue>();
    builder.Services.AddSingleton<IYuoSnapshotStore, PlayerSnapshotStoreAdapter>();
    builder.Services.AddSingleton<IYuoEventStore, NoopYuoEventStore>();
    builder.Services.AddHostedService<YuoBackendPersistenceReplayService>();
    builder.Services.AddHostedService<YuoPersistenceBackgroundService>();
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("yuocore-idle-backend"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(YuoPersistenceQueueMetrics.MeterName)
            .AddPrometheusExporter();
    });

var app = builder.Build();

app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseSerilogRequestLogging();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

app.UseAuthorization();

app.MapControllers();
app.MapPrometheusScrapingEndpoint("/metrics");
app.Map("/ws", GameWebSocketEndpoint.HandleAsync);

app.Run();

public partial class Program;
