using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.ComponentModel.DataAnnotations;
using System.Collections.Immutable;
using System.Security.Cryptography.X509Certificates;
using Microsoft.CodeAnalysis.Emit;

namespace Bollard.Compiler;
internal class AssemblyBuilder {

    // This will generate a hidden diagnostic of "unnecessary using directive" if nothing in the application references one of these namespaces.
    // Since the diagnostic is hidden, we leave it alone.
    const string c_globalSource = @"global using System; global using System.IO; global using System.Collections.Generic;";

    static readonly DiagnosticDescriptor c_diagAssemblyNotFound = new DiagnosticDescriptor("BB1001", "Assembly not found", "Assembly '{0}' not found.{1}", "Razor", DiagnosticSeverity.Error, true);

    static readonly Regex c_rxReferenceDirective = new Regex(@"^#ref\s+""([^""]+)""\s*$", RegexOptions.CultureInvariant);

    static readonly CSharpParseOptions c_parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12); // Same version as the project in 2026. May advance this in the future.
    static readonly CSharpCompilationOptions c_compOptions = new CSharpCompilationOptions(OutputKind.ConsoleApplication, reportSuppressedDiagnostics: false, optimizationLevel: OptimizationLevel.Release);

    static string[] c_defaultRefs = [
        "System.Runtime.dll",
        "System.Console.dll",
        "System.Collections.dll",
        "System.Linq.dll",
        "System.Linq.Expressions.dll", // Includes System.Dynamic.DynamicObject
        "System.Web.HttpUtility.dll" // Includes System.Web.HttpUtility
    ];

    static string[] s_assemblySearchPath;

    string _sourceDir = string.Empty; // For shortening the path on diagnostic locations.
    List<Diagnostic> _localDiagnostics = new List<Diagnostic>();
    List<SyntaxTree> _trees = new List<SyntaxTree>();
    List<MetadataReference> _refs = new List<MetadataReference>();
    HashSet<string> _refSeen = new HashSet<string>();
    Assembly? _assembly;
    ImmutableArray<Diagnostic> _diagnostics = ImmutableArray<Diagnostic>.Empty;
    DiagnosticSeverity _successLevel = DiagnosticSeverity.Hidden;

    static AssemblyBuilder() {
        // Compose assembly search path
        var primaryPath = GetReferenceAssemblyDirectory();
        var secondaryPath = AppContext.BaseDirectory; // Never null
        s_assemblySearchPath = primaryPath is not null ? [primaryPath, secondaryPath] : [secondaryPath];
    }

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
        foreach(var name in c_defaultRefs) {
            AddReference(name);
        }

        // Load the global Usings
        _trees.Add(CSharpSyntaxTree.ParseText(c_globalSource, c_parseOptions, "_globals.cs"));
    }

    public void ParseCSharp(string sourceFileName) {
        SourceText text;
        using (var stream = File.OpenRead(sourceFileName)) {
            text = SourceText.From(stream);
        }
        _trees.Add(CSharpSyntaxTree.ParseText(text, c_parseOptions, GetDiagnosticPath(sourceFileName)));
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

                AddReference(match.Groups[1].Value, trivia.GetLocation());
                replaceTrivia.Add(trivia);
            }

            if (replaceTrivia.Count > 0) {
                var cleaned = root.ReplaceTrivia(replaceTrivia, (t0, t1) => SyntaxFactory.Whitespace(string.Empty));
                _trees[i] = CSharpSyntaxTree.Create((CompilationUnitSyntax)cleaned, c_parseOptions, tree.FilePath);
            }
        }
    }

    public DiagnosticSeverity BuildAssembly() {
        ProcessCustomizations();
        var compilation = CSharpCompilation.Create("BollardAssembly", _trees, _refs, c_compOptions);
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (result.Success) {
            _assembly = Assembly.Load(ms.ToArray());
        }
        _diagnostics = ImmutableArray<Diagnostic>.Empty.AddRange(result.Diagnostics).AddRange(_localDiagnostics);
        _successLevel = _diagnostics.MaxBy(d => d.Severity)?.Severity ?? DiagnosticSeverity.Hidden;
        return _successLevel;
    }

    public DiagnosticSeverity SuccessLevel => _successLevel;
    public ImmutableArray<Diagnostic> Diagnostics => _diagnostics;
    public Assembly? Assembly => _assembly;

    public DiagnosticSeverity ReportDiagnostics(TextWriter? writer = null, DiagnosticSeverity minSeverity = DiagnosticSeverity.Warning) {
        if (writer is null) writer = Console.Out;
        foreach (var diagnostic in _diagnostics) {
            if (diagnostic.Severity >= minSeverity) {
                writer.WriteLine(diagnostic.ToString());
            }
        }
        return _successLevel;
    }

    private string GetDiagnosticPath(string path) {
        path = Path.GetFullPath(path);
        if (path.StartsWith(_sourceDir)) {
            return path.Substring(_sourceDir.Length);
        }
        return path;
    }

#if false
    /// <summary>
    /// Add a reference from a type. Translates from the runtime path (in type.Assembly.Location) to the reference path.
    /// </summary>
    /// <param name="type">Type to be referenced</param>
    /// <remarks>
    /// Due to deduplication in AddReference using multiple types from the same assembly doesn't cost much.
    /// </remarks>
    private void AddReference(Type type) {
        // Translates from runtime path to reference path
        if (!AddReference(Path.GetFileName(type.Assembly.Location))) {
            Console.WriteLine($"Not mapped: {type.FullName} -> {type.Assembly.Location}");
        }
        else {
            Console.WriteLine($"Mapped: {type.FullName} -> {Path.GetFileName(type.Assembly.Location)}");
        }
    }
#endif

    private bool AddReference(string assemblyName, Location? location = null) {
        var fullPath = FindAssembly(assemblyName);
        if (fullPath is null) {
            var hint = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? string.Empty : " (Usually it should have a .dll extension.)";
            _localDiagnostics.Add(Diagnostic.Create(c_diagAssemblyNotFound, location, assemblyName, hint));
            return false;
        }
        var abyName = AssemblyName.GetAssemblyName(fullPath);
        if (_refSeen.Add(abyName.FullName)) {
            _refs.Add(MetadataReference.CreateFromFile(fullPath));
        }
        return true;
    }

    /// <summary>
    /// Getting the path to the reference assembly directory is surprisingly complicated.
    /// That's because the path you get easily is to the runtime assemblies which may be stripped.
    /// It's also because the reference assemblies only have two parts to their version numbers.
    /// </summary>
    /// <returns>The reference assembly path or null.</returns>
    /// <remarks>
    /// Null may be returned if the application is packaged as single-file or NativeAOT.
    /// The contents may be enumerated to get a list of all possible assemblies.
    /// </remarks>
    private static string? GetReferenceAssemblyDirectory() {
        string? deps = (string?)AppContext.GetData("FX_DEPS_FILE");
        if (deps is null) return null;

        // Extract the runtime version (e.g., 8.0.2)
        var runtimeDir = Path.GetDirectoryName(deps)!;
        var version = Path.GetFileName(runtimeDir)!;
        var versionParts = version.Split('.');

        // Go all the way to the dotNetRoot (typical runtimeDir is C:\Program Files\dotnet\shared\MicrosoftNETCore.App\8.0.25)
        var dotnetRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir)));
        if (dotnetRoot is null) return null;

        // Assemble the new dir. Typical is C:\Program Files\dotnet\Microsoft.NETCore.App.Ref\8.0.25\ref\net8.0
        var assemblyDir = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", version, "ref", $"net{versionParts[0]}.{versionParts[1]}");

        return Path.Exists(assemblyDir) ? assemblyDir : null;
    }

    private static string? FindAssembly(string assemblyName) {
        // If the assembly has a full path then try that first
        if (Path.GetDirectoryName(assemblyName) is not null) {
            if (File.Exists(assemblyName)) {
                return Path.GetFullPath(assemblyName);
            }
            assemblyName = Path.GetFileName(assemblyName);
        }

        // Try each path in the list
        foreach(var directory in s_assemblySearchPath) {
            var fullPath = Path.Combine(directory, assemblyName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

}
