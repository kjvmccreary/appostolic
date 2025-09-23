using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Appostolic.DevHeadersAnalyzer.Tests;

internal static class AnalyzerTestHelper
{
    // Compiles the supplied C# source with the provided analyzer and returns produced diagnostics.
    public static ImmutableArray<Diagnostic> GetDiagnostics(string source, DiagnosticAnalyzer analyzer, string? filePath = null)
    {
        filePath ??= "/repo/apps/api/TestFile.cs"; // default path within enforcement scope
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).GetTypeInfo().Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Diagnostics.Debug).GetTypeInfo().Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create(analyzer);
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var diags = compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        return diags.OrderBy(d => d.Location.SourceSpan.Start).ToImmutableArray();
    }
}
