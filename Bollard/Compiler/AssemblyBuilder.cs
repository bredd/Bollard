global using DiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

using System;
using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Bollard;

internal class AssemblyBuilder {

    static readonly DiagnosticDescriptor c_diagAssemblyNotFound = new DiagnosticDescriptor("BB1001", "Assembly not found", "Assembly '{0}' not found.{1}", "Razor", DiagnosticSeverity.Error, true);

    static readonly Regex c_rxReferenceDirective = new Regex(@"^#ref\s+""([^""]+)""\s*$", RegexOptions.CultureInvariant);

    static readonly CSharpParseOptions c_parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12); // Same version as the project in 2026. May advance this in the future.
    static readonly CSharpCompilationOptions c_compOptions = new CSharpCompilationOptions(
        OutputKind.ConsoleApplication,
        reportSuppressedDiagnostics: false,
        optimizationLevel: OptimizationLevel.Release
        );

    static string[] c_defaultRefs = [
        "System.Runtime.dll",
        "System.Console.dll",
        "System.Collections.dll",
        "System.Linq.dll",
        "System.Linq.Expressions.dll", // Includes System.Dynamic.DynamicObject
        "System.Web.HttpUtility.dll" // Includes System.Web.HttpUtility
    ];

    string _sourceDir = string.Empty; // For shortening the path on diagnostic locations.
    List<Diagnostic> _localDiagnostics = new List<Diagnostic>();
    List<SyntaxTree> _trees = new List<SyntaxTree>();
    ImmutableArray<Diagnostic> _diagnostics = ImmutableArray<Diagnostic>.Empty;
    DiagnosticSeverity _successLevel = DiagnosticSeverity.Hidden;
    Assembly? _assembly;

    public string SourceDir {
        get => _sourceDir;
        set {
            _sourceDir = Path.GetFullPath(value);
            if (_sourceDir[_sourceDir.Length-1] != Path.DirectorySeparatorChar) {
                _sourceDir += Path.DirectorySeparatorChar;
            }
        }
    }

    public AssemblyBuilder() {
        // Add the core library and the default references
        var arm = AssemblyReferenceManager.Instance;
        foreach(var name in c_defaultRefs) {
            if (!arm.Add(name)) {
                throw new InvalidOperationException("Failed to load default assembly: " + name);
            }
        }
    }

    public void ParseCSharp(Stream stream, string name) {
        var text = SourceText.From(stream);
        _trees.Add(CSharpSyntaxTree.ParseText(text, c_parseOptions, name));
    }

    public void ParseCSharp(string sourceFileName) {
        SourceText text;
        using (var stream = File.OpenRead(sourceFileName)) {
            text = SourceText.From(stream);
        }
        _trees.Add(CSharpSyntaxTree.ParseText(text, c_parseOptions, GetDiagnosticPath(sourceFileName)));
    }

    public void ParseCSharpString(string text, string sourceName) {
        _trees.Add(CSharpSyntaxTree.ParseText(text, c_parseOptions, sourceName));
    }

    private void ProcessCustomizations() {

        // Find references to additional assemblies following the #r syntax precedent
        for (int i=0; i<_trees.Count; ++i) {
            var tree = _trees[i];
            var replaceTrivia = new List<SyntaxTrivia>();

            var root = tree.GetRoot();
            foreach (var trivia in root.DescendantTrivia()) {
                if (!trivia.IsKind(SyntaxKind.BadDirectiveTrivia))
                    continue;

                var match = c_rxReferenceDirective.Match(trivia.ToString());
                if (!match.Success)
                    continue;

                var location = trivia.GetLocation();
                var sourcePath = Path.Combine(_sourceDir, location.SourceTree?.FilePath ?? string.Empty);
                var assemblyName = match.Groups[1].Value;
                if (!AssemblyReferenceManager.Instance.Add(assemblyName, sourcePath, trivia.GetLocation())) {
                    var hint = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? string.Empty : " (Usually it should have a .dll extension.)";
                    _localDiagnostics.Add(Diagnostic.Create(c_diagAssemblyNotFound, location, assemblyName, hint));
                }
                replaceTrivia.Add(trivia);
            }

            if (replaceTrivia.Count > 0) {
                var cleaned = root.ReplaceTrivia(replaceTrivia, (t0, t1) => SyntaxFactory.Whitespace(string.Empty));
                _trees[i] = CSharpSyntaxTree.Create((CompilationUnitSyntax)cleaned, c_parseOptions, tree.FilePath);
            }
        }
    }

    public bool HasEntryPoint() {

        // First check for top-level statements
        foreach (var tree in _trees) {
            if (tree.GetRoot().ChildNodes().Any(n => n is GlobalStatementSyntax))
                return true;
        }

        // If no top-level statements, look for a static Main() method
        foreach (var tree in _trees) {
            var root = tree.GetRoot();
            if (root.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(m => {
                if (m.Identifier.Text != "Main") return false;
                if (!m.Modifiers.Any(SyntaxKind.StaticKeyword)) return false;
                var returnType = m.ReturnType.ToString();
                if (returnType != "void" && returnType != "int") return false;
                var parameters = m.ParameterList.Parameters;
                if (parameters.Count == 0) return true;
                if (parameters.Count == 1
                    && parameters[0].Type is ArrayTypeSyntax arr
                    && arr.ElementType.ToString() == "string")
                    return true;
                return false;
            })) return true;
        }

        return false;
    }

    public DiagnosticSeverity BuildAssembly() {
        ProcessCustomizations();

        var compilation = CSharpCompilation.Create("BollardAssembly", _trees, AssemblyReferenceManager.Instance.MetadataReferences, c_compOptions);
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (result.Success) {
            ms.Position = 0;
            _assembly = AssemblyLoadContext.Default.LoadFromStream(ms);
        }
        _diagnostics = ImmutableArray<Diagnostic>.Empty.AddRange(result.Diagnostics).AddRange(_localDiagnostics);
        _successLevel = _diagnostics.MaxBy(d => d.Severity)?.Severity ?? DiagnosticSeverity.Hidden;
        return _successLevel;
    }

    public DiagnosticSeverity SuccessLevel => _successLevel;
    public ImmutableArray<Diagnostic> Diagnostics => _diagnostics;
    public Assembly? Assembly => _assembly;

    public string GetDiagnosticPath(string path) {
        path = Path.GetFullPath(path);
        if (path.StartsWith(_sourceDir)) {
            return path.Substring(_sourceDir.Length);
        }
        return path;
    }

    public Diagnostic ToCompilerDiagnostic(RazorDiagnostic diag) {
        string filePath = GetDiagnosticPath(diag.Span.FilePath);
        // The integer values between RazorDiagnosticSeverity and Microsoft.CodeAnalysis.DiagnosticSeverity are equivalent
        var descriptor = new DiagnosticDescriptor(diag.Id, "Razor " + diag.Id, "{0}", "Razor", (DiagnosticSeverity)(int)diag.Severity, true);
        var textSpan = new TextSpan(diag.Span.AbsoluteIndex, 0);
        var linePositionSpan = new LinePositionSpan(new LinePosition(diag.Span.LineIndex, diag.Span.CharacterIndex),
            new LinePosition(diag.Span.LineIndex, Math.Max(diag.Span.CharacterIndex, diag.Span.EndCharacterIndex)));
        var location = Location.Create(filePath, textSpan, linePositionSpan);
        return Diagnostic.Create(descriptor, location, diag.GetMessage());
    }

}
