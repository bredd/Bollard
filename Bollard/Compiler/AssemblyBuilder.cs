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

namespace Bollard.Compiler;
internal class AssemblyBuilder {

    const string c_globalSource = @"global using System; global using System.IO; global using System.Collections.Generic;";

    static readonly Regex c_rxReferenceDirective = new Regex(@"^#ref\s+""([^""]+)""\s*$", RegexOptions.CultureInvariant);

    static readonly CSharpParseOptions c_parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12); // Same version as the project in 2026. May advance this in the future.
    static readonly CSharpCompilationOptions c_compOptions = new CSharpCompilationOptions(OutputKind.ConsoleApplication, reportSuppressedDiagnostics: false, optimizationLevel: OptimizationLevel.Release);


    static readonly MetadataReference[] c_refs = new MetadataReference[] {
        MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("netstandard")).Location),
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(DynamicObject).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(HttpUtility).Assembly.Location),


        MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime")).Location),
        MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Collections")).Location),

        /* Likely will need more. See RazorBit.cs. */
    };   

    List<SyntaxTree> _trees = new List<SyntaxTree>();

    public AssemblyBuilder() {

        // Load the global Usings
        _trees.Add(CSharpSyntaxTree.ParseText(c_globalSource, c_parseOptions, "_globals.cs"));
    }

    public void ParseCSharp(string sourceFileName) {
        SourceText text;
        using (var stream = File.OpenRead(sourceFileName)) {
            text = SourceText.From(stream);
        }
        _trees.Add(CSharpSyntaxTree.ParseText(text, c_parseOptions, sourceFileName));
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

                Console.WriteLine(match.Groups[1].Value);
                replaceTrivia.Add(trivia);
            }

            var cleaned = root.ReplaceTrivia(replaceTrivia, (t0, t1) => SyntaxFactory.Whitespace(string.Empty));
            _trees[i] = CSharpSyntaxTree.Create((CompilationUnitSyntax)cleaned);
        }
    }

    public Assembly? GetAssembly() {

        ProcessCustomizations();

        var metadataReferences = new List<MetadataReference>(c_refs);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            metadataReferences.Add(MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("netstandard")).Location));
        }


        var compilation = CSharpCompilation.Create("BollardAssembly", _trees, metadataReferences, c_compOptions);

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success) {
            foreach (var diagnostic in result.Diagnostics) {
                Console.WriteLine(diagnostic.ToString());
            }
            throw new ApplicationException("Failed to compile.");
        }
        ms.Position = 0;
        return Assembly.Load(ms.ToArray());
    }
}
