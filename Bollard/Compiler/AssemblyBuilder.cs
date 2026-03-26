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

    static readonly char[] c_slashes = { '/', '\\' };

    static string[] c_defaultRefs = [
        "System.Runtime.dll",
        "System.Console.dll",
        "System.Collections.dll",
        "System.Linq.dll",
        "System.Linq.Expressions.dll", // Includes System.Dynamic.DynamicObject
        "System.Web.HttpUtility.dll" // Includes System.Web.HttpUtility
    ];

    static readonly string? c_referenceAssemblyDirectory = GetReferenceAssemblyDirectory();
    static readonly string c_localAssemblyDirectory = AppContext.BaseDirectory;

    string _sourceDir = string.Empty; // For shortening the path on diagnostic locations.
    List<Diagnostic> _localDiagnostics = new List<Diagnostic>();
    List<SyntaxTree> _trees = new List<SyntaxTree>();
    List<MetadataReference> _assemblyRefs = new List<MetadataReference>();
    HashSet<string> _assemblyRefSeen = new HashSet<string>();
    Dictionary<string, string> _assemblyRefMap = new Dictionary<string, string>();
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
        foreach(var name in c_defaultRefs) {
            AddAssemblyReference(name);
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
                AddAssemblyReference(match.Groups[1].Value, sourcePath, trivia.GetLocation());
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

        var compilation = CSharpCompilation.Create("BollardAssembly", _trees, _assemblyRefs, c_compOptions);
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (result.Success) {
            ms.Position = 0;
            var loadContext = new LoadContext(_assemblyRefMap);
            _assembly = loadContext.LoadFromStream(ms);
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

    private bool AddAssemblyReference(string assemblyName, string? referencingPath = null, Location? location = null) {
        string? assemblyPath = null;
        bool isReferenceAssembly = false;

        // If a path to the assembly is given, it must be located as specified relative to the referencing file
        if (assemblyName.IndexOfAny(c_slashes) >= 0) {
            if (referencingPath is null) {
                throw new InvalidOperationException("Internal error: referencingPath should be specified when an assembly with a path is referenced.");
            }
            assemblyPath = Path.Combine(Path.GetFileName(referencingPath), assemblyName);
        }

        // Try the reference assembly directory
        if (assemblyPath is null && c_referenceAssemblyDirectory is not null) {
            assemblyPath = Path.Combine(c_referenceAssemblyDirectory, assemblyName);
            if (File.Exists(assemblyPath)) {
                isReferenceAssembly = true;
            }
            else {
                assemblyPath = null;
            }
        }

        // Try the local assembly directory (where the base executable is located)
        if (assemblyPath is null) {
            assemblyPath = Path.Combine(c_localAssemblyDirectory, assemblyName);
            if (!File.Exists(assemblyPath)) {
                assemblyPath = null;
            }
        }

        // Report if not found
        if (assemblyPath is null) {
            var hint = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? string.Empty : " (Usually it should have a .dll extension.)";
            _localDiagnostics.Add(Diagnostic.Create(c_diagAssemblyNotFound, location, assemblyName, hint));
            return false;
        }

        var abyName = AssemblyName.GetAssemblyName(assemblyPath);
        if (_assemblyRefSeen.Add(abyName.FullName)) {
            _assemblyRefs.Add(MetadataReference.CreateFromFile(assemblyPath));
            if (!isReferenceAssembly) {
                _assemblyRefMap[abyName.FullName] = assemblyPath;
            }
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

    class LoadContext: System.Runtime.Loader.AssemblyLoadContext {
        Dictionary<string, string> _assemblyRefMap;

        public LoadContext(Dictionary<string, string> assemblyRefMap) {
            _assemblyRefMap = assemblyRefMap;
        }

        protected override Assembly? Load(AssemblyName assemblyName) {
            Console.WriteLine("Attempting to load: " + assemblyName);
            if (_assemblyRefMap.TryGetValue(assemblyName.FullName, out var path)) {
                return LoadFromAssemblyPath(path);
            }
            return base.Load(assemblyName);
        }
    }

}
