using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace YuoCore.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProtocolBoundaryAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor ProtocolContractMustNotUseRuntimeTypesRule = new(
        YuoDiagnosticIds.ProtocolContractMustNotUseRuntimeTypes,
        "协议契约不能引用运行时内部类型",
        "Protocol contract '{0}' 引用了运行时内部类型 '{1}'，请只暴露纯数据字段，不要泄漏 Entity、Actor、Fiber、Location 或 Gateway 对象",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Protocol contracts shared by Web, Unity and server should stay as plain data and must not expose runtime ownership or routing objects.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(ProtocolContractMustNotUseRuntimeTypesRule);

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
        if (enclosingType is null || !IsProtocolContractScope(enclosingType))
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
        if (type is null || !IsRuntimeBoundaryType(type))
        {
            return;
        }

        // 协议层是 Web/Unity/服务端共享契约，只允许纯数据。
        // ActorId、Fiber、LocationKey、YuoEntity 等内部运行时对象应该在 Gateway/Actor 层解析，不进入外部 DTO。
        var diagnostic = Diagnostic.Create(
            ProtocolContractMustNotUseRuntimeTypesRule,
            typeSyntax.GetLocation(),
            enclosingType.Name,
            type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsProtocolContractScope(INamedTypeSymbol type)
    {
        var namespaceName = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceName == "YuoCore.Protocol" ||
               namespaceName.StartsWith("YuoCore.Protocol.", StringComparison.Ordinal) ||
               namespaceName.EndsWith(".Protocol", StringComparison.Ordinal) ||
               namespaceName.Contains(".Protocol.", StringComparison.Ordinal) ||
               namespaceName.EndsWith(".Contracts", StringComparison.Ordinal) ||
               namespaceName.Contains(".Contracts.", StringComparison.Ordinal);
    }

    private static bool IsRuntimeBoundaryType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return IsRuntimeBoundaryType(arrayType.ElementType);
        }

        if (IsRuntimeNamespace(type))
        {
            return true;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (IsRuntimeNamespace(current))
            {
                return true;
            }
        }

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var argument in namedType.TypeArguments)
            {
                if (IsRuntimeBoundaryType(argument))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsRuntimeNamespace(ITypeSymbol type)
    {
        var namespaceName = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return IsNamespaceOrChild(namespaceName, "YuoCore.Ecs") ||
               IsNamespaceOrChild(namespaceName, "YuoCore.Actor") ||
               IsNamespaceOrChild(namespaceName, "YuoCore.Runtime") ||
               IsNamespaceOrChild(namespaceName, "YuoCore.Location") ||
               IsNamespaceOrChild(namespaceName, "YuoCore.Gateway");
    }

    private static bool IsNamespaceOrChild(string namespaceName, string expected)
    {
        return namespaceName == expected ||
               namespaceName.StartsWith(expected + ".", StringComparison.Ordinal);
    }
}
