# YuoCore Idle Backend

ASP.NET Core + Raw WebSocket + Protobuf backend using the ported Yuo ECS runtime.

## Architecture

- `YuoCore.Abstractions`: small shared contracts such as result, error, clock, and runtime id.
- `YuoCore.Ecs`: ECS core, including `YuoWorld`, entities, components, systems, tags, and scene components.
- `YuoCore.Runtime`: Fiber runtime. `YuoFiber` owns and drives one `YuoWorld`; multiple fibers can tick in parallel.
- `YuoCore.Actor`: Actor-style routing, mailbox dispatch, and compatibility adapters.
- `YuoCore.Location`: stable business key to actor address registry with version and lock support.
- `YuoCore.Network`: transport/session abstractions and in-memory transport for tests.
- `YuoCore.Protocol`: shared client/internal protocol envelope, protobuf contracts, and serializer contracts.
- `YuoCore.Client`: Unity/C# client protocol session, request/response, opcode, and transport abstractions.
- `YuoCore.Client.StateSync`: optional C# client bridge for applying StateSnapshot/StateDelta pushes to a local mirror.
- `clients/unity/YuoCore.UnityClient`: optional Unity Editor/Standalone WebSocket transport adapter.
- `clients/web/yuocore-client-ts`: browser TypeScript SDK for V1 envelope request/response over WebSocket.
- `YuoCore.Gateway`: client session, connection, and gateway dispatch abstractions.
- `YuoCore.Game`: MMO domain components and internal actor messages.
- `YuoCore.StateSync.Abstractions`: pure snapshot, delta, mirror, AOI, and visibility-delta primitives for Web/Unity/server sharing.
- `YuoCore.StateSync`: ECS network identity/state components and Entity-based snapshot/AOI adapters.
- `YuoCore.Persistence`: snapshot/event store abstractions and async persistence queue.
- `YuoCore.ServiceDiscovery`: optional service registration, query, and change notification abstraction.
- `YuoCore.Config`: start-process/fiber/service config models and validation.
- `YuoCore.Server`: generic host that starts configured fibers and owns process-level services.
- `YuoCore.Backend`: Idle-game backend sample built on top of ECS and Runtime.

Detailed architecture notes are in `docs/et-inspired-architecture.md`.

The final ET-inspired YuoCore design is in `docs/yuocore-et-essence-final-design.md`.

Implementation and performance notes are in `docs/implementation-and-performance-notes.md`.

Web + Unity MMO responsibility separation is in `docs/web-unity-mmo-architecture.md`.

Modular runtime and ET10 extraction rules are in `docs/yuocore-modular-et10-extraction-rules.md`.

Module dependency and import profiles are in `docs/module-dependency-matrix.md` and `docs/import-profiles.md`.

NuGet package split and version rules are in `docs/nuget-packaging.md`.

ET10 extraction history and risk notes are in `docs/et10-extraction-log.md` and `docs/risk-and-decisions.md`.

## Run locally

```bash
dotnet run --project src/YuoCore.Backend/YuoCore.Backend.csproj
```

Open `http://localhost:5000` or the URL printed by Kestrel.

## Docker stack

```bash
docker compose up --build
```

- App direct: `http://localhost:8080`
- Nginx: `http://localhost:8088`
- OpenAPI: `/openapi/v1.json`
- Prometheus metrics: `/metrics`

## Benchmarks

```bash
dotnet run -c Release --project benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj
```

100 components + 100 systems + 10k entities + multi-fiber parallel tick:

```bash
dotnet run -c Release --project benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -- \
  --filter '*ParallelShape*' \
  --job Dry \
  --warmupCount 1 \
  --iterationCount 1
```

## Raw WebSocket protobuf load test

```bash
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- \
  --url ws://localhost:8080/ws \
  --players 10000 \
  --duration 120 \
  --interval 1000 \
  --connect-concurrency 200
```

## Local composite soak test

```bash
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- \
  --mode local-composite \
  --players 10000 \
  --duration 120 \
  --duration-mode \
  --shards 4 \
  --state-entities 10000 \
  --aoi-observers 256 \
  --persistence-per-tick 10000 \
  --aoi-result-mode count \
  --json-output artifacts/load/local-composite-soak.json \
  --csv-output artifacts/load/local-composite-soak.csv \
  --fail-p95-ms 250 \
  --fail-max-ms 500
```

## Testcontainers integration tests

The normal test run skips container startup. Enable it explicitly:

```bash
RUN_TESTCONTAINERS=1 dotnet test
```
