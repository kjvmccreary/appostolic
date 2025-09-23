using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Appostolic.DevHeadersAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DevHeaderArtifactsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "RDH0001";

    private static readonly LocalizableString Title = "Forbidden legacy dev header artifact";
    private static readonly LocalizableString MessageFormat = "Legacy dev header auth artifact '{0}' must not be reintroduced";
    private static readonly LocalizableString Description = "Prevents reintroduction of removed development header authentication pathway artifacts.";
    private const string Category = "Security";

    private static readonly string[] ForbiddenTokens = new[]
    {
        "x-dev-user",
        "x-tenant",
        "DevHeaderAuthHandler",
        "BearerOrDev",
        "AUTH__ALLOW_DEV_HEADERS"
    };

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/kjvmccreary/appostolic"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var filePath = context.Tree.FilePath ?? string.Empty;

        // Allowlist: negative-path regression or historical story docs inside tests may intentionally mention strings.
        // We only enforce within C# source under /apps/ and /packages/ excluding test files named *DevHeadersRemoved* or story log migrations.
    if (filePath.IndexOf("storyLog", System.StringComparison.OrdinalIgnoreCase) >= 0) return;
        if (filePath.EndsWith("DevHeadersRemovedTests.cs")) return; // explicit negative-path test

        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var node in root.DescendantNodesAndTokens())
        {
            if (!node.IsToken) continue;
            var token = node.AsToken();
            if (token.IsKind(SyntaxKind.StringLiteralToken))
            {
                var text = token.Text; // includes quotes
                foreach (var forbidden in ForbiddenTokens)
                {
                    if (text.IndexOf(forbidden, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Report(token.GetLocation(), forbidden, context);
                    }
                }
            }
            else if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                var ident = token.ValueText;
                foreach (var forbidden in ForbiddenTokens)
                {
                    if (string.Equals(ident, forbidden, System.StringComparison.Ordinal))
                    {
                        Report(token.GetLocation(), forbidden, context);
                    }
                }
            }
        }
    }

    private static void Report(Location location, string artifact, SyntaxTreeAnalysisContext context)
    {
        var diagnostic = Diagnostic.Create(Rule, location, artifact);
        context.ReportDiagnostic(diagnostic);
    }
}
