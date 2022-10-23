﻿using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Analyzer.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JSInteropMustNotBeUsedInOnInitializedAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor s_rule = new(
        RuleIdentifiers.JSRuntimeMustNotBeUsedInOnInitialized,
        title: "JSRuntime must not be used in OnInitialized or OnInitializedAsync",
        messageFormat: "JSRuntime must not be used in OnInitialized or OnInitializedAsync",
        RuleCategories.Design,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "",
        helpLinkUri: RuleIdentifiers.GetHelpUri(RuleIdentifiers.JSRuntimeMustNotBeUsedInOnInitialized));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(s_rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(ctx =>
        {
            var analyzerContext = new AnalyzerContext(ctx.Compilation);
            if (analyzerContext.IsValid)
            {
                ctx.RegisterOperationBlockStartAction(analyzerContext.OperationBlockStart);
            }
        });
    }

    private sealed class AnalyzerContext
    {
        public AnalyzerContext(Compilation compilation)
        {
            IJSRuntimeSymbol = compilation.GetBestTypeByMetadataName("Microsoft.JSInterop.IJSRuntime");
            JSRuntimeSymbol = compilation.GetBestTypeByMetadataName("Microsoft.JSInterop.JSRuntime");
            WebAssemblyJSRuntimeSymbol = compilation.GetBestTypeByMetadataName("Microsoft.JSInterop.WebAssembly.WebAssemblyJSRuntime");
            var componentBase = compilation.GetBestTypeByMetadataName("Microsoft.AspNetCore.Components.ComponentBase");
            if (componentBase != null)
            {
                OnInitializedMethodSymbol = componentBase.GetMembers("OnInitialized").SingleOrDefaultIfMultiple();
                OnInitializedAsyncMethodSymbol = componentBase.GetMembers("OnInitializedAsync").SingleOrDefaultIfMultiple();
            }
        }

        public INamedTypeSymbol? IJSRuntimeSymbol { get; }
        public INamedTypeSymbol? JSRuntimeSymbol { get; }
        public INamedTypeSymbol? WebAssemblyJSRuntimeSymbol { get; }
        public ISymbol? OnInitializedMethodSymbol { get; }
        public ISymbol? OnInitializedAsyncMethodSymbol { get; }

        public bool IsValid
        {
            get
            {
                if (WebAssemblyJSRuntimeSymbol != null)
                    return false; // There is no issue in WebAssembly

                return (IJSRuntimeSymbol != null || JSRuntimeSymbol != null) && (OnInitializedMethodSymbol != null || OnInitializedAsyncMethodSymbol != null);
            }
        }

        internal void OperationBlockStart(OperationBlockStartAnalysisContext context)
        {
            if (context.OwningSymbol is not IMethodSymbol methodSymbol)
                return;

            if (methodSymbol.Override(OnInitializedMethodSymbol) || methodSymbol.Override(OnInitializedAsyncMethodSymbol))
            {
                context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
            }
        }

        private void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var operation = (IInvocationOperation)context.Operation;
            var instance = operation.Instance;
            if (instance == null)
            {
                if (operation.TargetMethod.IsExtensionMethod && operation.Arguments.Length > 0)
                {
                    instance = operation.Arguments[0].Value;
                }

                if (instance == null)
                    return;
            }

            var type = instance.GetActualType();
            if (type == null)
                return;

            if (type.IsEqualTo(IJSRuntimeSymbol) || type.IsEqualTo(JSRuntimeSymbol))
            {
                context.ReportDiagnostic(s_rule, operation);
            }
        }
    }
}
