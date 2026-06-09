using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace YuoCore.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class YuoTaskUsageAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor YuoTaskMustBeConsumedRule = new(
        YuoDiagnosticIds.YuoTaskMustBeConsumed,
        "YuoTask 调用结果必须被消费",
        "YuoTask 调用 '{0}' 不能作为裸表达式丢弃，请使用 await、Forget/Coroutine、return、赋值或显式传递",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pooled YuoTask calls must be consumed so exceptions are observed and completion sources can return to the pool.");

    private static readonly DiagnosticDescriptor AsyncVoidForbiddenRule = new(
        YuoDiagnosticIds.AsyncVoidForbidden,
        "禁止 async void",
        "async void 方法 '{0}' 不适合 YuoCore 逻辑，请返回 Task、ValueTask、YuoTask 或仅用于事件处理器",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "async void hides exceptions from task-like error handling and should be limited to event handlers.");

    private static readonly DiagnosticDescriptor YuoTaskGetResultForbiddenRule = new(
        YuoDiagnosticIds.YuoTaskGetResultForbidden,
        "禁止同步 GetResult 消费 YuoTask",
        "YuoTask 调用 '{0}' 不应通过 GetAwaiter().GetResult() 同步消费，请使用 await、Forget/Coroutine 或测试专用抑制",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Direct GetResult bypasses the intended one-shot async consumption model of pooled YuoTask.");

    private static readonly DiagnosticDescriptor YuoTaskMustNotBeStoredRule = new(
        YuoDiagnosticIds.YuoTaskMustNotBeStored,
        "禁止长期保存 YuoTask",
        "YuoTask 调用 '{0}' 不应保存到字段或属性，请 await、return、Forget/Coroutine 或在当前调用链内显式传递",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pooled YuoTask instances are one-shot awaitables and should not be stored in long-lived fields or properties.");

    private static readonly DiagnosticDescriptor YuoTaskMustNotBeAwaitedMultipleTimesRule = new(
        YuoDiagnosticIds.YuoTaskMustNotBeAwaitedMultipleTimes,
        "禁止多次 await 同一个 YuoTask",
        "YuoTask 局部变量 '{0}' 不能被 await 多次，请为每次消费创建新的任务或改用 Task/ValueTask",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pooled YuoTask instances are one-shot awaitables and should not be awaited more than once.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            YuoTaskMustBeConsumedRule,
            AsyncVoidForbiddenRule,
            YuoTaskGetResultForbiddenRule,
            YuoTaskMustNotBeStoredRule,
            YuoTaskMustNotBeAwaitedMultipleTimesRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunctionStatement, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeAwaitExpression, SyntaxKind.AwaitExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeAnonymousFunction,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.AnonymousMethodExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (ReportsForbiddenGetResult(context, invocation))
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (!IsYuoTask(method.ReturnType))
        {
            return;
        }

        if (ReportsForbiddenStorage(context, invocation, method.Name))
        {
            return;
        }

        if (IsConsumed(invocation))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            YuoTaskMustBeConsumedRule,
            invocation.GetLocation(),
            method.Name);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool ReportsForbiddenStorage(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string methodName)
    {
        var expression = UnwrapParentheses(invocation);
        if (expression.Parent is AssignmentExpressionSyntax assignment &&
            assignment.Right.Span.Contains(invocation.Span) &&
            IsFieldOrPropertySymbol(context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol))
        {
            ReportForbiddenStorage(context, invocation, methodName);
            return true;
        }

        if (expression.Parent is not EqualsValueClauseSyntax equalsValue)
        {
            return false;
        }

        if (equalsValue.Parent is PropertyDeclarationSyntax)
        {
            ReportForbiddenStorage(context, invocation, methodName);
            return true;
        }

        if (equalsValue.Parent is VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax })
        {
            ReportForbiddenStorage(context, invocation, methodName);
            return true;
        }

        return false;
    }

    private static void ReportForbiddenStorage(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string methodName)
    {
        var diagnostic = Diagnostic.Create(
            YuoTaskMustNotBeStoredRule,
            invocation.GetLocation(),
            methodName);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsFieldOrPropertySymbol(ISymbol? symbol)
    {
        return symbol is IFieldSymbol or IPropertySymbol;
    }

    private static bool ReportsForbiddenGetResult(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax getResultAccess)
        {
            return false;
        }

        if (getResultAccess.Name.Identifier.ValueText != "GetResult")
        {
            return false;
        }

        if (getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation)
        {
            return false;
        }

        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess)
        {
            return false;
        }

        if (getAwaiterAccess.Name.Identifier.ValueText != "GetAwaiter")
        {
            return false;
        }

        if (!IsYuoTaskGetAwaiterTarget(context, getAwaiterAccess.Expression))
        {
            return false;
        }

        var diagnostic = Diagnostic.Create(
            YuoTaskGetResultForbiddenRule,
            invocation.GetLocation(),
            "GetResult");
        context.ReportDiagnostic(diagnostic);
        return true;
    }

    private static bool IsYuoTaskGetAwaiterTarget(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax getAwaiterTarget)
    {
        var awaitedType = context.SemanticModel.GetTypeInfo(getAwaiterTarget, context.CancellationToken).Type;
        if (IsYuoTask(awaitedType))
        {
            return true;
        }

        if (getAwaiterTarget is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "AsTask")
        {
            return false;
        }

        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        return IsYuoTask(receiverType);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        if (!methodDeclaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            return;
        }

        if (methodDeclaration.ReturnType is not PredefinedTypeSyntax predefinedType
            || !predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword))
        {
            return;
        }

        var method = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, context.CancellationToken);
        if (method is not null && IsEventHandlerLike(method))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            AsyncVoidForbiddenRule,
            methodDeclaration.Identifier.GetLocation(),
            methodDeclaration.Identifier.ValueText);
        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeAwaitExpression(SyntaxNodeAnalysisContext context)
    {
        var awaitExpression = (AwaitExpressionSyntax)context.Node;
        if (TryGetAwaitedYuoTaskLocal(context, awaitExpression, out var awaitedLocal) == false)
        {
            return;
        }

        var functionScope = FindFunctionLikeScope(awaitExpression);
        if (functionScope is null)
        {
            return;
        }

        // YuoTask 是一次性 awaitable。这里仅在当前函数语法范围内回看同一个局部变量，
        // 避免跨方法别名和复杂控制流推断带来的误报。
        foreach (var previousAwait in functionScope.DescendantNodes(static node => node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
                     .OfType<AwaitExpressionSyntax>())
        {
            if (previousAwait.SpanStart >= awaitExpression.SpanStart)
            {
                break;
            }

            if (TryGetAwaitedYuoTaskLocal(context, previousAwait, out var previousLocal) &&
                SymbolEqualityComparer.Default.Equals(awaitedLocal, previousLocal))
            {
                var diagnostic = Diagnostic.Create(
                    YuoTaskMustNotBeAwaitedMultipleTimesRule,
                    awaitExpression.GetLocation(),
                    awaitedLocal.Name);
                context.ReportDiagnostic(diagnostic);
                return;
            }
        }
    }

    private static void AnalyzeLocalFunctionStatement(SyntaxNodeAnalysisContext context)
    {
        var localFunction = (LocalFunctionStatementSyntax)context.Node;
        if (!localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            return;
        }

        if (localFunction.ReturnType is not PredefinedTypeSyntax predefinedType
            || !predefinedType.Keyword.IsKind(SyntaxKind.VoidKeyword))
        {
            return;
        }

        var method = context.SemanticModel.GetDeclaredSymbol(localFunction, context.CancellationToken);
        if (method is not null && IsEventHandlerLike(method))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            AsyncVoidForbiddenRule,
            localFunction.Identifier.GetLocation(),
            localFunction.Identifier.ValueText);
        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeAnonymousFunction(SyntaxNodeAnalysisContext context)
    {
        var anonymousFunction = (AnonymousFunctionExpressionSyntax)context.Node;
        if (!anonymousFunction.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
        {
            return;
        }

        if (context.SemanticModel.GetTypeInfo(anonymousFunction, context.CancellationToken).ConvertedType
            is not INamedTypeSymbol delegateType)
        {
            return;
        }

        var invokeMethod = delegateType.DelegateInvokeMethod;
        if (invokeMethod is null || !invokeMethod.ReturnsVoid)
        {
            return;
        }

        if (IsEventHandlerLike(invokeMethod))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            AsyncVoidForbiddenRule,
            anonymousFunction.AsyncKeyword.GetLocation(),
            "<lambda>");
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsYuoTask(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return namedType.ContainingNamespace.ToDisplayString() == "YuoCore.Tasks"
               && namedType.Name == "YuoTask";
    }

    private static bool IsEventHandlerLike(IMethodSymbol method)
    {
        if (method.Parameters.Length != 2)
        {
            return false;
        }

        var secondParameterType = method.Parameters[1].Type;
        while (secondParameterType is not null)
        {
            if (secondParameterType.ToDisplayString() == "System.EventArgs")
            {
                return true;
            }

            secondParameterType = secondParameterType.BaseType;
        }

        return false;
    }

    private static bool TryGetAwaitedYuoTaskLocal(
        SyntaxNodeAnalysisContext context,
        AwaitExpressionSyntax awaitExpression,
        out ILocalSymbol local)
    {
        local = null!;
        var expression = UnwrapExpressionParentheses(awaitExpression.Expression);
        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (symbol is not ILocalSymbol localSymbol || !IsYuoTask(localSymbol.Type))
        {
            return false;
        }

        local = localSymbol;
        return true;
    }

    private static SyntaxNode? FindFunctionLikeScope(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is MethodDeclarationSyntax
                or ConstructorDeclarationSyntax
                or DestructorDeclarationSyntax
                or OperatorDeclarationSyntax
                or ConversionOperatorDeclarationSyntax
                or AccessorDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax)
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsConsumed(InvocationExpressionSyntax invocation)
    {
        if (IsFollowedByFireAndForget(invocation))
        {
            return true;
        }

        return IsExpressionValueConsumed(invocation);
    }

    private static bool IsFollowedByFireAndForget(InvocationExpressionSyntax invocation)
    {
        if (invocation.Parent is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        if (memberAccess.Parent is not InvocationExpressionSyntax)
        {
            return false;
        }

        var memberName = memberAccess.Name.Identifier.ValueText;
        return memberName is "Forget" or "Coroutine";
    }

    private static bool IsExpressionValueConsumed(ExpressionSyntax expression)
    {
        SyntaxNode? current = UnwrapParentheses(expression);

        return current.Parent switch
        {
            AwaitExpressionSyntax => true,
            ReturnStatementSyntax => true,
            ArrowExpressionClauseSyntax => true,
            EqualsValueClauseSyntax => true,
            AssignmentExpressionSyntax => true,
            ArgumentSyntax => true,
            ExpressionStatementSyntax => false,
            _ => true
        };
    }

    private static SyntaxNode UnwrapParentheses(ExpressionSyntax expression)
    {
        SyntaxNode current = expression;
        while (current.Parent is ParenthesizedExpressionSyntax)
        {
            current = current.Parent;
        }

        return current;
    }

    private static ExpressionSyntax UnwrapExpressionParentheses(ExpressionSyntax expression)
    {
        var current = expression;
        while (current is ParenthesizedExpressionSyntax parenthesizedExpression)
        {
            current = parenthesizedExpression.Expression;
        }

        return current;
    }
}
