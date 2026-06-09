using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using YuoCore.Analyzers;

namespace YuoCore.Analyzers.Tests;

public sealed class YuoTaskUsageAnalyzerTests
{
    [Fact]
    public void Analyzer_release_notes_cover_all_yuo_diagnostic_ids()
    {
        var root = FindRepositoryRoot();
        var shipped = File.ReadAllText(Path.Combine(root, "src", "YuoCore.Analyzers", "AnalyzerReleases.Shipped.md"));
        var unshipped = File.ReadAllText(Path.Combine(root, "src", "YuoCore.Analyzers", "AnalyzerReleases.Unshipped.md"));
        var releaseNotes = shipped + Environment.NewLine + unshipped;
        var ids = typeof(YuoDiagnosticIds)
            .GetFields()
            .Where(field => field is { IsLiteral: true, IsStatic: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var id in ids)
        {
            Assert.Contains(id, releaseNotes, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Reports_bare_yuotask_invocation()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public void Run()
                {
                    DoAsync();
                }

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.YuoTaskMustBeConsumed, diagnostic.Id);
    }

    [Theory]
    [InlineData("await DoAsync();")]
    [InlineData("DoAsync().Forget();")]
    [InlineData("DoAsync().Coroutine();")]
    [InlineData("var task = DoAsync();")]
    [InlineData("_ = DoAsync();")]
    [InlineData("return DoAsync();")]
    public async Task Allows_consumed_yuotask_invocation(string statement)
    {
        var diagnostics = await GetDiagnosticsAsync(
            $$"""
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public async YuoTask Run()
                {
                    {{statement}}
                }

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_async_void_logic_method()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            public sealed class Sample
            {
                public async void Run()
                {
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.AsyncVoidForbidden, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_async_void_local_function()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            public sealed class Sample
            {
                public void Run()
                {
                    async void Local()
                    {
                        await System.Threading.Tasks.Task.CompletedTask;
                    }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.AsyncVoidForbidden, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_async_void_lambda_converted_to_action()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System;

            public sealed class Sample
            {
                public void Run()
                {
                    Action action = async () =>
                    {
                        await System.Threading.Tasks.Task.CompletedTask;
                    };
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.AsyncVoidForbidden, diagnostic.Id);
    }

    [Fact]
    public async Task Allows_async_void_event_handler_shape()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System;

            public sealed class Sample
            {
                public async void OnClick(object? sender, EventArgs e)
                {
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Allows_async_lambda_converted_to_event_handler_shape()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System;

            public sealed class Sample
            {
                public void Run()
                {
                    EventHandler handler = async (sender, e) =>
                    {
                        await System.Threading.Tasks.Task.CompletedTask;
                    };
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_yuotask_getawaiter_getresult()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public void Run()
                {
                    DoAsync().GetAwaiter().GetResult();
                }

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.YuoTaskGetResultForbidden, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_yuotask_astask_getawaiter_getresult()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public void Run()
                {
                    DoAsync().AsTask().GetAwaiter().GetResult();
                }

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.YuoTaskGetResultForbidden, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_yuotask_invocation_stored_in_field()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                private YuoTask _pending;

                public void Run()
                {
                    _pending = DoAsync();
                }

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.YuoTaskMustNotBeStored, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_yuotask_invocation_stored_in_property_initializer()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public YuoTask Pending { get; } = DoAsync();

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.YuoTaskMustNotBeStored, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_yuotask_local_awaited_twice()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public async YuoTask Run()
                {
                    var task = DoAsync();
                    await task;
                    await task;
                }

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.YuoTaskMustNotBeAwaitedMultipleTimes, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_generic_yuotask_local_awaited_twice()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public async YuoTask Run()
                {
                    var task = DoAsync();
                    _ = await task;
                    _ = await task;
                }

                private static YuoTask<int> DoAsync() => YuoTask.FromResult(1);
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.YuoTaskMustNotBeAwaitedMultipleTimes, diagnostic.Id);
    }

    [Fact]
    public async Task Allows_distinct_yuotask_locals()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public async YuoTask Run()
                {
                    var first = DoAsync();
                    var second = DoAsync();
                    await first;
                    await second;
                }

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Allows_repeated_yuotask_calls_without_reusing_local()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using YuoCore.Tasks;

            public sealed class Sample
            {
                public async YuoTask Run()
                {
                    await DoAsync();
                    await DoAsync();
                }

                private static YuoTask DoAsync() => YuoTask.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_gateway_dispatcher_primary_constructor_ecs_dependency()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using YuoCore.Ecs;
            using YuoCore.Gateway;

            public sealed class BadGatewayDispatcher(YuoEntity entity) : IYuoGatewayDispatcher
            {
                public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
                    YuoGatewaySession session,
                    YuoGatewayInboundMessage message,
                    CancellationToken cancellationToken = default) =>
                    throw new System.NotImplementedException();
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.GatewayDispatcherMustNotUseEcs, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_gateway_dispatcher_component_dependency_through_inheritance()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using YuoCore.Ecs;
            using YuoCore.Gateway;

            public sealed class PlayerStateComponent : YuoComponent
            {
            }

            public sealed class BadGatewayDispatcher : IYuoGatewayDispatcher
            {
                private PlayerStateComponent _component = null!;

                public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
                    YuoGatewaySession session,
                    YuoGatewayInboundMessage message,
                    CancellationToken cancellationToken = default) =>
                    throw new System.NotImplementedException();
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.GatewayDispatcherMustNotUseEcs, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_gateway_dispatcher_ecs_dependency_inside_collection_type()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using YuoCore.Ecs;
            using YuoCore.Gateway;

            public sealed class BadGatewayDispatcher : IYuoGatewayDispatcher
            {
                public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
                    YuoGatewaySession session,
                    YuoGatewayInboundMessage message,
                    CancellationToken cancellationToken = default)
                {
                    List<YuoEntity> entities = [];
                    throw new System.NotImplementedException();
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.GatewayDispatcherMustNotUseEcs, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_gateway_dispatcher_ecs_dependency_inside_array_type()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using YuoCore.Ecs;
            using YuoCore.Gateway;

            public sealed class BadGatewayDispatcher : IYuoGatewayDispatcher
            {
                private YuoEntity[] _entities = [];

                public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
                    YuoGatewaySession session,
                    YuoGatewayInboundMessage message,
                    CancellationToken cancellationToken = default) =>
                    throw new System.NotImplementedException();
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.GatewayDispatcherMustNotUseEcs, diagnostic.Id);
    }

    [Fact]
    public async Task Allows_gateway_dispatcher_to_reference_plain_array_type()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using YuoCore.Gateway;

            public sealed class GoodGatewayDispatcher : IYuoGatewayDispatcher
            {
                private byte[] _buffer = [];

                public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
                    YuoGatewaySession session,
                    YuoGatewayInboundMessage message,
                    CancellationToken cancellationToken = default) =>
                    throw new System.NotImplementedException();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Allows_gateway_dispatcher_to_depend_on_command_dispatcher()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using YuoCore.Gateway;

            public sealed class GameCommandDispatcher
            {
                public ValueTask DispatchAsync(byte[] payload, CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }

            public sealed class GoodGatewayDispatcher(GameCommandDispatcher commands) : IYuoGatewayDispatcher
            {
                public async ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
                    YuoGatewaySession session,
                    YuoGatewayInboundMessage message,
                    CancellationToken cancellationToken = default)
                {
                    await commands.DispatchAsync(message.Payload.ToArray(), cancellationToken);
                    return new YuoGatewayOutboundMessage(message.Version, message.OpCode, message.RequestId, 0, []);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Reports_protocol_contract_actor_id_dependency()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            namespace YuoCore.Actor
            {
                public readonly struct ActorId
                {
                }
            }

            namespace Game.Protocol
            {
                using YuoCore.Actor;

                public sealed record MoveRequest(ActorId TargetActorId, int X, int Y);
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.ProtocolContractMustNotUseRuntimeTypes, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_protocol_contract_ecs_dependency_inside_collection_type()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            namespace YuoCore.Ecs
            {
                public class YuoEntity
                {
                }
            }

            namespace Game.Contracts.World
            {
                using System.Collections.Generic;
                using YuoCore.Ecs;

                public sealed class SnapshotDto
                {
                    public List<YuoEntity> Entities { get; } = [];
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.ProtocolContractMustNotUseRuntimeTypes, diagnostic.Id);
    }

    [Fact]
    public async Task Reports_protocol_contract_location_key_dependency()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            namespace YuoCore.Location
            {
                public readonly struct YuoLocationKey
                {
                }
            }

            namespace YuoCore.Protocol.Messages
            {
                using YuoCore.Location;

                public sealed class EnterMapRequest
                {
                    public YuoLocationKey MapKey { get; init; }
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(YuoDiagnosticIds.ProtocolContractMustNotUseRuntimeTypes, diagnostic.Id);
    }

    [Fact]
    public async Task Allows_protocol_contract_plain_data()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            namespace Game.Protocol
            {
                public sealed record MoveRequest(string PlayerId, int X, int Y);

                public sealed class SnapshotDto
                {
                    public string[] EntityIds { get; init; } = [];
                    public byte[] Payload { get; init; } = [];
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(EventArgs).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValueTask).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(YuoCore.Abstractions.YuoError).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(YuoCore.Ecs.YuoEntity).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(YuoCore.Gateway.IYuoGatewayDispatcher).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(YuoCore.Tasks.YuoTask).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "YuoAnalyzerTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(
            new YuoTaskUsageAnalyzer(),
            new GatewayBoundaryAnalyzer(),
            new ProtocolBoundaryAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "YuoCore.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find YuoCore repository root from test output directory.");
    }
}
