using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using YuoCore.Backend.Game;
using YuoCore.Game;

namespace YuoCore.Backend.Infrastructure;

public interface IPlayerSnapshotRepository
{
    Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default);
    Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default);
}

public sealed class NoopPlayerSnapshotRepository : IPlayerSnapshotRepository
{
    public Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default) => Task.FromResult<YuoPlayerSnapshotDto?>(null);
}

public sealed class PostgresPlayerSnapshotRepository : IPlayerSnapshotRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresPlayerSnapshotRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        const string sql = """
            insert into player_snapshots(
              player_id, name, gold, exp, level, power, clicks, updated_at,
              map_id, unit_id, x, y, z, online, last_input_seq)
            values (
              @PlayerId, @Name, @Gold, @Exp, @Level, @Power, @Clicks, @UpdatedAt,
              @MapId, @UnitId, @X, @Y, @Z, @Online, @LastInputSeq)
            on conflict (player_id) do update set
              name = excluded.name,
              gold = excluded.gold,
              exp = excluded.exp,
              level = excluded.level,
              power = excluded.power,
              clicks = excluded.clicks,
              updated_at = excluded.updated_at,
              map_id = excluded.map_id,
              unit_id = excluded.unit_id,
              x = excluded.x,
              y = excluded.y,
              z = excluded.z,
              online = excluded.online,
              last_input_seq = excluded.last_input_seq;
            """;

        var parameters = new
        {
            snapshot.PlayerId,
            snapshot.Name,
            snapshot.Gold,
            snapshot.Exp,
            snapshot.Level,
            snapshot.Power,
            snapshot.Clicks,
            snapshot.UpdatedAt,
            snapshot.MapId,
            snapshot.UnitId,
            snapshot.X,
            snapshot.Y,
            snapshot.Z,
            snapshot.Online,
            LastInputSeq = (decimal)snapshot.LastInputSeq
        };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
    }

    public async Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            select player_id as "PlayerId", name as "Name", gold as "Gold", exp as "Exp", level as "Level",
                   power as "Power", clicks as "Clicks", updated_at as "UpdatedAt",
                   map_id as "MapId", unit_id as "UnitId", x as "X", y as "Y", z as "Z",
                   online as "Online", last_input_seq as "LastInputSeq"
            from player_snapshots
            where player_id = @playerId;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<PlayerSnapshotRow>(
            new CommandDefinition(sql, new { playerId }, cancellationToken: cancellationToken));
        return row?.ToDto();
    }

    private sealed record PlayerSnapshotRow(
        Guid PlayerId,
        string Name,
        long Gold,
        long Exp,
        int Level,
        long Power,
        long Clicks,
        DateTime UpdatedAt,
        long MapId,
        long UnitId,
        float X,
        float Y,
        float Z,
        bool Online,
        decimal LastInputSeq)
    {
        public YuoPlayerSnapshotDto ToDto()
        {
            var inputSeq = LastInputSeq <= 0 ? 0UL : decimal.ToUInt64(decimal.Truncate(LastInputSeq));
            var updatedAt = UpdatedAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)
                : UpdatedAt.ToUniversalTime();

            return new YuoPlayerSnapshotDto(
                PlayerId,
                Name,
                Gold,
                Exp,
                Level,
                Power,
                Clicks,
                new DateTimeOffset(updatedAt),
                MapId,
                UnitId,
                X,
                Y,
                Z,
                Online,
                inputSeq);
        }
    }
}

public sealed class PostgresSchemaMigrator : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly StorageOptions _options;
    private readonly ILogger<PostgresSchemaMigrator> _logger;

    public PostgresSchemaMigrator(IServiceProvider services, IOptions<StorageOptions> options, ILogger<PostgresSchemaMigrator> logger)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.PostgresEnabled) return;
        try
        {
            var dataSource = _services.GetRequiredService<NpgsqlDataSource>();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition("""
                create table if not exists player_snapshots (
                  player_id uuid primary key,
                  name text not null,
                  gold bigint not null,
                  exp bigint not null,
                  level integer not null,
                  power bigint not null,
                  clicks bigint not null,
                  updated_at timestamptz not null,
                  map_id bigint not null default 0,
                  unit_id bigint not null default 0,
                  x real not null default 0,
                  y real not null default 0,
                  z real not null default 0,
                  online boolean not null default false,
                  last_input_seq numeric(20,0) not null default 0
                );
                alter table player_snapshots add column if not exists map_id bigint not null default 0;
                alter table player_snapshots add column if not exists unit_id bigint not null default 0;
                alter table player_snapshots add column if not exists x real not null default 0;
                alter table player_snapshots add column if not exists y real not null default 0;
                alter table player_snapshots add column if not exists z real not null default 0;
                alter table player_snapshots add column if not exists online boolean not null default false;
                alter table player_snapshots add column if not exists last_input_seq numeric(20,0) not null default 0;
                """, cancellationToken: cancellationToken));
            _logger.LogInformation("PostgreSQL schema is ready");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL migration failed");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
