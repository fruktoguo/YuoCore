using System.Xml.Linq;

namespace YuoCore.Architecture.Tests;

public sealed class ProjectDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["YuoCore.Abstractions"] = [],
            ["YuoCore.Actor"] = ["YuoCore.Location"],
            ["YuoCore.Actor.Ecs"] = ["YuoCore.Actor", "YuoCore.Ecs"],
            ["YuoCore.Actor.Timers"] = ["YuoCore.Actor", "YuoCore.Timers"],
            ["YuoCore.Analyzers"] = [],
            ["YuoCore.Backend"] = ["YuoCore.Actor.Ecs", "YuoCore.Ecs", "YuoCore.Game", "YuoCore.Gateway", "YuoCore.Persistence", "YuoCore.Persistence.Hosting", "YuoCore.Protocol", "YuoCore.Runtime", "YuoCore.Runtime.Actor", "YuoCore.Server", "YuoCore.StateSync.Protocol"],
            ["YuoCore.Client"] = ["YuoCore.Abstractions", "YuoCore.Protocol"],
            ["YuoCore.Client.StateSync"] = ["YuoCore.Client", "YuoCore.StateSync.Protocol"],
            ["YuoCore.Config"] = ["YuoCore.Abstractions"],
            ["YuoCore.Ecs"] = [],
            ["YuoCore.Game"] = ["YuoCore.Actor", "YuoCore.Ecs"],
            ["YuoCore.Gateway"] = ["YuoCore.Abstractions"],
            ["YuoCore.Location"] = ["YuoCore.Abstractions"],
            ["YuoCore.Network"] = ["YuoCore.Location", "YuoCore.Protocol"],
            ["YuoCore.Persistence"] = ["YuoCore.Abstractions"],
            ["YuoCore.Persistence.Hosting"] = ["YuoCore.Persistence"],
            ["YuoCore.Protocol"] = ["YuoCore.Abstractions"],
            ["YuoCore.Runtime"] = ["YuoCore.Abstractions", "YuoCore.Ecs"],
            ["YuoCore.Runtime.Actor"] = ["YuoCore.Actor", "YuoCore.Runtime"],
            ["YuoCore.Runtime.Timers"] = ["YuoCore.Runtime", "YuoCore.Timers"],
            ["YuoCore.Server"] = ["YuoCore.Actor", "YuoCore.Actor.Ecs", "YuoCore.Config", "YuoCore.Ecs", "YuoCore.Location", "YuoCore.Runtime", "YuoCore.Runtime.Actor"],
            ["YuoCore.Server.ServiceDiscovery"] = ["YuoCore.Config", "YuoCore.ServiceDiscovery"],
            ["YuoCore.ServiceDiscovery"] = ["YuoCore.Abstractions"],
            ["YuoCore.StateSync.Abstractions"] = [],
            ["YuoCore.StateSync"] = ["YuoCore.Ecs", "YuoCore.StateSync.Abstractions"],
            ["YuoCore.StateSync.Protocol"] = ["YuoCore.Protocol", "YuoCore.StateSync.Abstractions"],
            ["YuoCore.Tasks"] = [],
            ["YuoCore.Timers"] = ["YuoCore.Abstractions"]
        };

    private static readonly IReadOnlyDictionary<string, string[]> AllowedPackageReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["YuoCore.Abstractions"] = [],
            ["YuoCore.Actor"] = [],
            ["YuoCore.Actor.Ecs"] = [],
            ["YuoCore.Actor.Timers"] = [],
            ["YuoCore.Analyzers"] = ["Microsoft.CodeAnalysis.Analyzers", "Microsoft.CodeAnalysis.CSharp"],
            ["YuoCore.Backend"] =
            [
                "Dapper",
                "Google.Protobuf",
                "Microsoft.AspNetCore.OpenApi",
                "Npgsql",
                "OpenTelemetry.Exporter.OpenTelemetryProtocol",
                "OpenTelemetry.Exporter.Prometheus.AspNetCore",
                "OpenTelemetry.Extensions.Hosting",
                "OpenTelemetry.Instrumentation.AspNetCore",
                "OpenTelemetry.Instrumentation.Http",
                "OpenTelemetry.Instrumentation.Runtime",
                "Serilog.AspNetCore",
                "Serilog.Sinks.Console",
                "StackExchange.Redis"
            ],
            ["YuoCore.Client"] = [],
            ["YuoCore.Client.StateSync"] = [],
            ["YuoCore.Config"] = [],
            ["YuoCore.Ecs"] = [],
            ["YuoCore.Game"] = [],
            ["YuoCore.Gateway"] = [],
            ["YuoCore.Location"] = [],
            ["YuoCore.Network"] = [],
            ["YuoCore.Persistence"] = [],
            ["YuoCore.Persistence.Hosting"] = ["Microsoft.Extensions.Hosting.Abstractions", "Microsoft.Extensions.Options"],
            ["YuoCore.Protocol"] = ["Google.Protobuf", "Grpc.Tools", "System.Text.Json"],
            ["YuoCore.Runtime"] = [],
            ["YuoCore.Runtime.Actor"] = [],
            ["YuoCore.Runtime.Timers"] = [],
            ["YuoCore.Server"] = [],
            ["YuoCore.Server.ServiceDiscovery"] = [],
            ["YuoCore.ServiceDiscovery"] = [],
            ["YuoCore.StateSync.Abstractions"] = [],
            ["YuoCore.StateSync"] = [],
            ["YuoCore.StateSync.Protocol"] = [],
            ["YuoCore.Tasks"] = [],
            ["YuoCore.Timers"] = []
        };

    [Fact]
    public void Source_project_references_follow_the_documented_matrix()
    {
        var projects = LoadSourceProjects();

        Assert.Empty(projects.Keys.Except(AllowedProjectReferences.Keys, StringComparer.Ordinal));
        Assert.Empty(AllowedProjectReferences.Keys.Except(projects.Keys, StringComparer.Ordinal));

        foreach (var project in projects.Values)
        {
            var allowed = AllowedProjectReferences[project.Name].Order(StringComparer.Ordinal);
            var actual = project.ProjectReferences.Order(StringComparer.Ordinal);

            // 依赖矩阵是 YuoCore 可裁剪性的硬护栏。
            // 新增项目引用时必须先确认它不会污染单机、Unity 或纯本机多线程使用路径。
            Assert.Equal(allowed, actual);
        }
    }

    [Fact]
    public void Source_package_references_follow_the_documented_matrix()
    {
        var projects = LoadSourceProjects();

        Assert.Empty(projects.Keys.Except(AllowedPackageReferences.Keys, StringComparer.Ordinal));
        Assert.Empty(AllowedPackageReferences.Keys.Except(projects.Keys, StringComparer.Ordinal));

        foreach (var project in projects.Values)
        {
            var allowed = AllowedPackageReferences[project.Name].Order(StringComparer.Ordinal);
            var actual = project.PackageReferences.Order(StringComparer.Ordinal);

            // NuGet 依赖同样会影响 Unity/单机包体，必须像项目引用一样显式白名单化。
            Assert.Equal(allowed, actual);
        }
    }

    [Fact]
    public void Core_projects_stay_free_of_runtime_and_server_dependencies()
    {
        var projects = LoadSourceProjects();
        var coreProjects = new[] { "YuoCore.Abstractions", "YuoCore.Ecs", "YuoCore.Tasks" };

        foreach (var projectName in coreProjects)
        {
            var project = projects[projectName];

            Assert.Empty(project.ProjectReferences);
            Assert.Empty(project.PackageReferences);
        }
    }

    [Fact]
    public void Core_runtime_projects_do_not_reference_protocol_network_or_server_layers()
    {
        var projects = LoadSourceProjects();
        var coreRuntimeProjects = new[]
        {
            "YuoCore.Abstractions",
            "YuoCore.Actor",
            "YuoCore.Ecs",
            "YuoCore.Location",
            "YuoCore.Runtime",
            "YuoCore.Tasks",
            "YuoCore.Timers"
        };
        var forbiddenReferences = new HashSet<string>(StringComparer.Ordinal)
        {
            "YuoCore.Backend",
            "YuoCore.Config",
            "YuoCore.Gateway",
            "YuoCore.Network",
            "YuoCore.Persistence",
            "YuoCore.Persistence.Hosting",
            "YuoCore.Protocol",
            "YuoCore.Server",
            "YuoCore.Server.ServiceDiscovery",
            "YuoCore.ServiceDiscovery"
        };

        var illegalReferences = coreRuntimeProjects
            .SelectMany(projectName => projects[projectName].ProjectReferences
                .Where(forbiddenReferences.Contains)
                .Select(reference => $"{projectName} -> {reference}"))
            .ToArray();

        // 单机、Unity 和同机多线程核心路径不能被外部协议、网络、数据库、宿主装配层污染。
        // 需要桥接时新增显式适配包，例如 Runtime.Actor、Runtime.Timers、Actor.Timers。
        Assert.Empty(illegalReferences);
    }

    [Fact]
    public void Unity_client_adapter_stays_on_client_transport_boundary()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "clients", "unity", "YuoCore.UnityClient", "YuoCore.UnityClient.csproj");
        var project = LoadProject(projectPath);

        Assert.Equal(["YuoCore.Client"], project.ProjectReferences.Order(StringComparer.Ordinal));
        Assert.Empty(project.PackageReferences);

        var forbiddenReferences = new[]
        {
            "UnityEngine",
            "YuoCore.Actor",
            "YuoCore.Backend",
            "YuoCore.Ecs",
            "YuoCore.Game",
            "YuoCore.Gateway",
            "YuoCore.Location",
            "YuoCore.Persistence",
            "YuoCore.Runtime",
            "YuoCore.Server"
        };
        var sourceFiles = Directory.EnumerateFiles(Path.GetDirectoryName(projectPath)!, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        var violations = sourceFiles
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                return forbiddenReferences
                    .Where(reference => text.Contains(reference, StringComparison.Ordinal))
                    .Select(reference => $"{Path.GetRelativePath(root, path)} contains {reference}");
            })
            .ToArray();

        // Unity 适配层只负责把客户端协议字节接到具体网络库。
        // 它不能成为服务端架构、ECS 权威状态或 UnityEngine 表现层的反向入口。
        Assert.Empty(violations);
    }

    [Fact]
    public void Optional_hosting_and_discovery_adapters_do_not_become_core_dependencies()
    {
        var projects = LoadSourceProjects();
        var optionalAdapters = new HashSet<string>(StringComparer.Ordinal)
        {
            "YuoCore.Actor.Timers",
            "YuoCore.Persistence.Hosting",
            "YuoCore.Runtime.Actor",
            "YuoCore.Runtime.Timers",
            "YuoCore.Server",
            "YuoCore.Server.ServiceDiscovery",
            "YuoCore.Backend"
        };

        var illegalReferences = projects.Values
            .Where(project => !optionalAdapters.Contains(project.Name))
            .SelectMany(project => project.ProjectReferences
                .Where(optionalAdapters.Contains)
                .Select(reference => $"{project.Name} -> {reference}"))
            .ToArray();

        Assert.Empty(illegalReferences);
    }

    [Fact]
    public void Source_directory_build_props_installs_analyzer_without_runtime_reference()
    {
        var root = FindRepositoryRoot();
        var propsPath = Path.Combine(root, "src", "Directory.Build.props");
        var document = XDocument.Load(propsPath);

        var analyzerReference = Assert.Single(document.Descendants("ProjectReference"));
        var include = analyzerReference.Attribute("Include")?.Value ?? string.Empty;
        Assert.EndsWith(
            Path.Combine("YuoCore.Analyzers", "YuoCore.Analyzers.csproj"),
            include.Replace('\\', Path.DirectorySeparatorChar),
            StringComparison.Ordinal);
        Assert.Equal("Analyzer", analyzerReference.Attribute("OutputItemType")?.Value);
        Assert.Equal("false", analyzerReference.Attribute("ReferenceOutputAssembly")?.Value);
        Assert.Equal("all", analyzerReference.Attribute("PrivateAssets")?.Value);
    }

    [Fact]
    public void Source_directory_build_props_defines_common_nuget_metadata()
    {
        var root = FindRepositoryRoot();
        var propsPath = Path.Combine(root, "src", "Directory.Build.props");
        var document = XDocument.Load(propsPath);
        var propertyNames = document.Descendants("PropertyGroup")
            .Elements()
            .Select(element => element.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal);

        var requiredProperties = new[]
        {
            "PackageId",
            "VersionPrefix",
            "RepositoryUrl",
            "PackageOutputPath",
            "IncludeSymbols",
            "SymbolPackageFormat",
            "PackageReadmeFile"
        };

        foreach (var propertyName in requiredProperties)
        {
            // NuGet 元数据必须集中在 src/Directory.Build.props，避免每个包各自漂移。
            Assert.Contains(propertyName, propertyNames);
        }
    }

    [Fact]
    public void Backend_sample_host_is_not_packable()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", "YuoCore.Backend", "YuoCore.Backend.csproj");
        var document = XDocument.Load(projectPath);

        Assert.Equal("false", document.Descendants("IsPackable").Single().Value);
    }

    [Fact]
    public void Backend_sample_host_exports_optional_persistence_queue_meter()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "YuoCore.Backend", "Program.cs"));

        // Backend 是可选宿主层，可以接 OpenTelemetry/Prometheus；
        // Persistence.Hosting 只暴露 .NET Meter，不能让 Persistence 核心直接依赖遥测包。
        Assert.Contains("AddSingleton<YuoPersistenceQueueMetrics>()", program, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<YuoPersistenceQueueMetricsHostedService>()", program, StringComparison.Ordinal);
        Assert.Contains("AddMeter(YuoPersistenceQueueMetrics.MeterName)", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_dependency_matrix_mentions_every_source_project()
    {
        var root = FindRepositoryRoot();
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "module-dependency-matrix.md"));
        var projects = LoadSourceProjects();

        foreach (var projectName in projects.Keys.Order(StringComparer.Ordinal))
        {
            // 模块矩阵是 AI 和维护者判断导入组合/可裁剪性的入口。
            // 新增 src 项目时，必须同步写清楚职责、依赖方向和适用场景。
            Assert.Contains($"`{projectName}`", matrix, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nuget_packaging_document_mentions_every_source_project()
    {
        var root = FindRepositoryRoot();
        var packaging = File.ReadAllText(Path.Combine(root, "docs", "nuget-packaging.md"));
        var projects = LoadSourceProjects();

        foreach (var projectName in projects.Keys.Order(StringComparer.Ordinal))
        {
            // 打包规范必须覆盖每个 src 项目：可打包库说明导入层级，示例宿主说明为什么不发布。
            Assert.Contains(projectName, packaging, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ai_development_guide_routes_every_source_project()
    {
        var root = FindRepositoryRoot();
        var guide = File.ReadAllText(Path.Combine(root, "docs", "ai-development-guide.md"));
        var projects = LoadSourceProjects();

        foreach (var projectName in projects.Keys.Order(StringComparer.Ordinal))
        {
            // AI 入口必须能把每个 src 项目路由到正确职责。
            // 新增模块时只更新依赖矩阵还不够，也要更新 AI 任务入口，避免后续自动化把文件放错包。
            Assert.Contains($"`src/{projectName}`", guide, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ai_development_guide_mentions_architecture_analyzer_diagnostics()
    {
        var root = FindRepositoryRoot();
        var guide = File.ReadAllText(Path.Combine(root, "docs", "ai-development-guide.md"));
        var architectureDiagnosticIds = LoadAnalyzerReleaseDiagnosticIds("Architecture");

        foreach (var diagnosticId in architectureDiagnosticIds)
        {
            // 架构类 Analyzer 是 AI 高频错误的编译期护栏。
            // 新增这类诊断时，AI 任务入口也必须解释它，否则自动化只会看到报错但不知道边界原因。
            Assert.Contains(diagnosticId, guide, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Root_agents_entry_points_to_required_ai_rule_documents()
    {
        var root = FindRepositoryRoot();
        var agents = File.ReadAllText(Path.Combine(root, "AGENTS.md"));

        var requiredDocuments = new[]
        {
            "docs/ai-development-guide.md",
            "docs/yuocore-modular-et10-extraction-rules.md",
            "docs/module-dependency-matrix.md"
        };

        foreach (var document in requiredDocuments)
        {
            // 根 AGENTS.md 是自动化开发的最小入口。
            // 它必须继续指向任务路由、ET10 吸收规则和模块依赖矩阵这三类硬规则。
            Assert.Contains(document, agents, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Core_markdown_documents_have_balanced_fenced_code_blocks()
    {
        var root = FindRepositoryRoot();
        var markdownFiles = Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md")
            .Append(Path.Combine(root, "AGENTS.md"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var markdownFile in markdownFiles)
        {
            var fenceCount = File.ReadLines(markdownFile)
                .Count(line => line.TrimStart().StartsWith("```", StringComparison.Ordinal));

            // 长期 goal 会持续追加中文设计记录。代码块不闭合会让后续 AI/人类误读整段规则。
            Assert.Equal(0, fenceCount % 2);
        }
    }

    private static IReadOnlyDictionary<string, ProjectInfo> LoadSourceProjects()
    {
        var root = FindRepositoryRoot();
        var sourceDirectory = Path.Combine(root, "src");

        return Directory.EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Select(LoadProject)
            .ToDictionary(project => project.Name, StringComparer.Ordinal);
    }

    private static ProjectInfo LoadProject(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var projectReferences = document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => GetProjectNameFromReference(value!))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var packageReferences = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new ProjectInfo(
            Path.GetFileNameWithoutExtension(projectPath),
            projectReferences,
            packageReferences);
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

    private static string[] LoadAnalyzerReleaseDiagnosticIds(string category)
    {
        var root = FindRepositoryRoot();
        var releaseFiles = new[]
        {
            Path.Combine(root, "src", "YuoCore.Analyzers", "AnalyzerReleases.Shipped.md"),
            Path.Combine(root, "src", "YuoCore.Analyzers", "AnalyzerReleases.Unshipped.md")
        };

        return releaseFiles
            .Where(File.Exists)
            .SelectMany(File.ReadLines)
            .Select(line => line.Split('|').Select(part => part.Trim()).ToArray())
            .Where(parts => parts.Length >= 3 &&
                            parts[0].StartsWith("YUO", StringComparison.Ordinal) &&
                            string.Equals(parts[1], category, StringComparison.Ordinal))
            .Select(parts => parts[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetProjectNameFromReference(string reference)
    {
        var normalized = reference.Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFileNameWithoutExtension(normalized);
    }

    private sealed record ProjectInfo(
        string Name,
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> PackageReferences);
}
