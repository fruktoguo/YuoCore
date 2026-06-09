using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace YuoCore.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GatewayBoundaryAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor GatewayDispatcherMustNotUseEcsRule = new(
        YuoDiagnosticIds.GatewayDispatcherMustNotUseEcs,
        "Gateway dispatcher 不能直接使用 ECS 类型",
        "Gateway dispatcher '{0}' 直接引用了 ECS 类型 '{1}'，请把客户端请求转成 command/Actor 消息，在 Game/Map/Player Fiber 内处理 ECS",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Gateway should own client connection and protocol dispatch only. ECS state changes must stay behind game or actor ownership boundaries.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(GatewayDispatcherMustNotUseEcsRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeTypeSyntax,
            SyntaxKind.IdentifierName,
            SyntaxKind.QualifiedName,
            SyntaxKind.GenericName,
            SyntaxKind.ArrayType,
            SyntaxKind.NullableType);
    }

    private static void AnalyzeTypeSyntax(SyntaxNodeAnalysisContext context)
    {
        var typeSyntax = (TypeSyntax)context.Node;
        if (typeSyntax.Ancestors().OfType<TypeSyntax>().Any())
        {
            return;
        }

        var enclosingType = context.ContainingSymbol?.ContainingType;
        if (enclosingType is null || !ImplementsGatewayDispatcher(enclosingType))
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
        if (type is null || !IsEcsBoundaryType(type))
        {
            return;
        }

        // 这里故意只看类型引用，不分析方法体里的复杂调用链。
        // 目标是先阻止最危险、最明确的边界泄漏：Gateway dispatcher 的字段/参数/局部变量直接拿到 ECS 对象。
        var diagnostic = Diagnostic.Create(
            GatewayDispatcherMustNotUseEcsRule,
            typeSyntax.GetLocation(),
            enclosingType.Name,
            type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        context.ReportDiagnostic(diagnostic);
    }

    private static bool ImplementsGatewayDispatcher(INamedTypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.Name == "IYuoGatewayDispatcher" &&
                @interface.ContainingNamespace.ToDisplayString() == "YuoCore.Gateway")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEcsBoundaryType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsEcsBoundaryType(arrayType.ElementType);
        }

        if (IsYuoCoreEcsNamespace(type))
        {
            return true;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (IsYuoCoreEcsNamespace(current))
            {
                return true;
            }
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var argument in namedType.TypeArguments)
            {
                if (IsEcsBoundaryType(argument))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsYuoCoreEcsNamespace(ITypeSymbol type)
    {
        var namespaceName = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceName == "YuoCore.Ecs" || namespaceName.StartsWith("YuoCore.Ecs.", StringComparison.Ordinal);
    }
}
