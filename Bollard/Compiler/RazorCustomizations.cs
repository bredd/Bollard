using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using static System.Net.Mime.MediaTypeNames;
using static Bollard.RazorCustomizations;
using System.Diagnostics.Tracing;

/* RazorProjectEngine phases and passes determined to date
 *   DefaultRazorParsingPhase
 *      Outputs RazorSyntaxTree which is attached to the code document.
 *   DefaultRazorSyntaxTreePhase
 *      May modify RazorSyntaxTree
 *   DefaultRazorTagHelperBinderPhase
 *      May annotate the syntax tree.
 *   DefaultRazorIntermediateNodeLoweringPhase
 *      Outputs the DocumentIntermediateNode Tree (the parse tree operated on by the following phases)
 *   DefaultRazorDocumentClassifierPhase
 *      IRazorCSharpDocumentClassifierPass (presumably)
 *   DefaultRazorDirectiveClassifierPhase
 *      IRazorDirectiveClassifierPass
 *   DefaultRazorOptimizationPhase
 *      IRazorOptimizationPass (most useful place to insert work)
 *      (Trace up the stack one slot to see all of the passes being run and figure out where to position)
 *   DefaultRazorCSharpLoweringPhase
 *      (Actual CSharp document generation)
 */


namespace Bollard;
internal class RazorCustomizations {

    const string c_pageDirectiveName = "page";
    const string c_assetDirectiveName = "asset";
    const string c_layoutDirectiveName = "layout";
    const string c_baseClassHtml = "Bollard.HtmlTemplate";
    const string c_baseClassGeneric = "Bollard.RazorTemplate";
    const string c_docTypeHtml = "mvc";        // Equivalent to FileKinds.Legacy
    const string c_docTypeGeneric = "generic"; // No HTML Processing

    static readonly char[] c_directorySeparatorChars = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    // The descriptor tells the parser what this is.
    private static readonly DirectiveDescriptor c_pageDirective =
        DirectiveDescriptor.CreateSingleLineDirective(c_pageDirectiveName,
            builder => {
                // Name and description arguments are just for diagnostic feedback to the user. They don't affect operation
                builder.AddOptionalStringToken("path", "Site path to output destination.");
                builder.Usage = DirectiveUsage.FileScopedSinglyOccurring; // Modifies the prior setting.
            });

    // The descriptor tells the parser what this is.
    private static readonly DirectiveDescriptor c_assetDirective =
        DirectiveDescriptor.CreateSingleLineDirective(c_assetDirectiveName,
            builder => {
                // Name and description arguments are just for diagnostic feedback to the user. They don't affect operation
                builder.AddOptionalStringToken("path", "Site path to output destination.");
                builder.Usage = DirectiveUsage.FileScopedSinglyOccurring; // Modifies the prior setting.
            });

    private static readonly DirectiveDescriptor c_layoutDirective =
    DirectiveDescriptor.CreateSingleLineDirective(c_layoutDirectiveName,
        builder => {
            // Name and description arguments are just for diagnostic feedback to the user. They don't affect operation
            builder.AddOptionalStringToken("path", "Site path to output destination.");
            builder.Usage = DirectiveUsage.FileScopedSinglyOccurring; // Modifies the prior setting.
        });

    private class CustomDataNode : IntermediateNode {
        public CustomDataNode(string name, string value) {
            Name = name;
            Value = value;
        }

        public string Name { get; private set; }

        public string Value { get; private set; }

        public override IntermediateNodeCollection Children => IntermediateNodeCollection.ReadOnly; // Badly named but returns empty which is what we want.
        public override void Accept(IntermediateNodeVisitor visitor)
            => visitor.VisitDefault(this);
    }

    private class PreProcessPhase : IRazorEnginePhase {

        public static void Attach(RazorProjectEngineBuilder builder) {
            builder.Phases.Insert(1, new PreProcessPhase());
        }

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument) {
            Console.WriteLine($"*** PreProcessPhase: {codeDocument?.Source?.RelativePath} ***");
            var source = codeDocument?.Source;
            if (source is null) {
#if DEBUG
                Console.WriteLine("Unexpected null RazorCodeDocument.Source.");
#endif
                return;
            }
            var reader = new RazorDirectiveExtractor(source);

            // At this stage, all we are looking for is either an @page directive indicating HTML parsing or an @asset directive indicating plain parsing
            bool htmlParser = false;
            var filePath = source.RelativePath ?? source.FilePath;

            if (string.Equals(Path.GetExtension(filePath), ".cshtml", StringComparison.OrdinalIgnoreCase))
                htmlParser = true;

            // If both directives are present, take the last one
            while (reader.ReadNext()) {
                if (reader.CurrentName == "@page")
                    htmlParser = true;
                else if (reader.CurrentName == "@asset")
                    htmlParser = false;
            }

            // Set the parser type
            codeDocument.SetFileKind(htmlParser ? c_docTypeHtml : c_docTypeGeneric);
        }
    }

    private class TextOnlyParsingPhase : IRazorEnginePhase {
        public static void Attach(RazorProjectEngineBuilder builder) {
            for (int i = 0; i < builder.Phases.Count; i++) {
                if (builder.Phases[i] is TextOnlyParsingPhase) {
                    builder.Phases.Remove(builder.Phases[i]);
                    builder.Phases.Add(new TextOnlyParsingPhase());
                }
            }
            builder.Phases.Insert(1, new PreProcessPhase());
        }

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument) {
            var options = RazorParserOptions.Create(builder => {
                builder.Directives.Clear(); // Optional according to AI which has made a lot of errors so far
            });

            var syntaxTree = RazorSyntaxTree.Parse(codeDocument.Source, options);

            codeDocument.SetSyntaxTree(syntaxTree);
        }

    }

    private class CustomDocumentClassifierPass : IRazorDocumentClassifierPass {
        public int Order => 500; // Run before the default pass

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) {
            if (codeDocument.GetFileKind() != c_docTypeGeneric)
                return; // Only handle our custom class

            documentNode.DocumentKind = c_docTypeGeneric;

            // Set code generation options
            var codeGenOptions = RazorCodeGenerationOptions.CreateDefault();
            documentNode.Target = CodeTarget.CreateDefault(codeDocument, codeGenOptions);

            // Build the IR structure (namespace → class → method)
            var ns = new NamespaceDeclarationIntermediateNode {
                Content = "GeneratedTemplates"
            };

            var cls = new ClassDeclarationIntermediateNode {
                ClassName = "Template_" + Guid.NewGuid().ToString("N"),
                Modifiers = { "public" }
            };

            var method = new MethodDeclarationIntermediateNode {
                MethodName = "ExecuteAsync",
                Modifiers = { "public", "async" },
                ReturnType = "System.Threading.Tasks.Task"
            };

            // Attach nodes
            documentNode.Children.Add(ns);
            ns.Children.Add(cls);
            cls.Children.Add(method);
        }

    }

    /*
    private class CustomDocumentClassifierPass : DocumentClassifierPassBase {
        public override int Order => DefaultFeatureOrder - 100; // Run before the default pass

        protected override bool IsMatch(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) {
            return codeDocument.GetFileKind() == c_docTypeGeneric;
        }

        protected override string DocumentKind => c_docTypeGeneric;
    }
    */

    private class CustomClassNamePass : IRazorDocumentClassifierPass {
        public int Order => 1001; // Run after built-in passes

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) {
            Console.WriteLine($"  *** documentNode.DocumentKind = {documentNode.DocumentKind}");
            documentNode.DocumentKind = codeDocument.GetFileKind();

            var classNode = documentNode.FindPrimaryClass();
            var namespaceNode = documentNode.FindPrimaryNamespace();
            Debug.Assert(classNode is not null && namespaceNode is not null);
            if (classNode is null || namespaceNode is null) return;

            var filePath = codeDocument.Source.RelativePath ?? codeDocument.Source.FilePath;

            // Customize the class name
            classNode.ClassName = PathTool.SanitizeToCSharpName(Path.GetFileNameWithoutExtension(filePath));
            classNode.BaseType =  string.Equals(codeDocument.GetFileKind(), c_docTypeHtml) ? c_baseClassHtml : c_baseClassGeneric;
            string ns;
            if (codeDocument.TryComputeNamespace(true, out ns)) {
                namespaceNode.Content = ns;
            }

            // Record the source filename
            classNode.Children.Add(new CSharpCodeIntermediateNode {
                Children = {
                    new IntermediateToken {
                        Kind = TokenKind.CSharp,
                        Content = string.Concat("protected override string SourceName => @\"", filePath.Replace("\"", "\"\""), "\";")
                    }
                }
            });
        }
    }

    private class CustomDirectivesPass : IRazorOptimizationPass {

        // Order is important:
        //   Must come after the basic CSharp structure is prepped in DefaultRazorDocumentClassifierPhase
        //   Must come before instances DirectiveIntermediateNode are removed
        // IRazorOptimizationPass is part of the DefaultRazorDirectiveClassifierPhase
        // Pass 1050: Directive removal (this will not work after that pass)
        public int Order => 1001;

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) {
            // Console.WriteLine("==== CustomDirectivesPass");

            // Enumerate directives and process those that we care about
            foreach (var directiveNode in documentNode.FindDescendantNodes<DirectiveIntermediateNode>()) {
                switch (directiveNode.DirectiveName) {
                case c_pageDirectiveName:
                    ProcessPageDirective(documentNode, directiveNode);
                    break;

                case c_layoutDirectiveName:
                    ProcessLayoutDirective(documentNode, directiveNode);
                    break;
                }
            }
        }

        private void ProcessPageDirective(DocumentIntermediateNode documentNode, DirectiveIntermediateNode directiveNode) {
            var value = (directiveNode.Tokens.FirstOrDefault()?.Content ?? string.Empty).Trim(' ', '"');
            var node = new CustomDataNode("page", value);
            documentNode.Children.Add(node);
        }

        private void ProcessLayoutDirective(DocumentIntermediateNode documentNode, DirectiveIntermediateNode directiveNode) {
            var value = (directiveNode.Tokens.FirstOrDefault()?.Content ?? string.Empty).Trim(' ', '"');

            var method = documentNode.FindPrimaryMethod();
            if (method is null)
                throw new InvalidOperationException("Expected method to be ready. Possibly the wrong phase or pass.");

            method.Children.Insert(0, new CSharpCodeIntermediateNode {
                Children = {
                        new IntermediateToken {
                            Kind = TokenKind.CSharp,
                            Content = $"Layout = \"{value}\";"
                        }
                    }
            });
        }
    }

#if DEBUG
    public class TracePhase : IRazorEnginePhase {
        string _label;

        public TracePhase(string label) {
            _label = label;
        }

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument) {
            Console.WriteLine("TestPhase: " + _label);
            //DumpRecursive(1, codeDocument.GetSyntaxTree().Root)
            DumpRecursive(1, codeDocument.GetDocumentIntermediateNode());

            if (Engine is not null) {
                var enumerator = Engine.Phases.GetEnumerator();
                while (enumerator.MoveNext()) {
                    if (Object.ReferenceEquals(enumerator.Current, this)) {
                        break;
                    }
                }
                if (enumerator.MoveNext()) {
                    Console.WriteLine($"Phase: {enumerator.Current.GetType().FullName}");
                }
            }
        }

        public static void DumpRecursive(int level, IntermediateNode node) {
            if (node is null)
                return;
            Console.Write($"{new string(' ', level * 2)}{node.GetType().Name}");

            switch (node) {
            case DirectiveIntermediateNode nd: {
                Console.Write($": {nd.DirectiveName}");
                break;
            }

            case MalformedDirectiveIntermediateNode nd: {
                Console.Write($": {nd.DirectiveName}");
                break;
            }

            case CustomDataNode nd: {
                Console.Write($": name={nd.Name} value={nd.Value}");
                break;
            }
            }

            Console.WriteLine();

            foreach (var diagnostic in node.Diagnostics) {
                Console.WriteLine($"{new string(' ', level * 2)}  Err: {diagnostic}");
            }

            foreach (var child in node.Children) {
                DumpRecursive(level + 1, child);
            }
        }

        public static void Attach(RazorProjectEngineBuilder builder) {
            for (int i = 0; i * 2 <= builder.Phases.Count; ++i) {
                builder.Phases.Insert(i * 2, new TracePhase(i.ToString()));
            }
        }
    }
#endif // DEBUG

    public static RazorProjectEngineBuilder AddToRazorProject(RazorProjectEngineBuilder builder) {
        //PreProcessPhase.Attach(builder);

        // Adding directives registers them but they must be processed in the later passes or phases.
        builder.AddDirective(c_pageDirective);
        builder.AddDirective(c_assetDirective);
        builder.AddDirective(c_layoutDirective);

        // Custom passes are called within phases
        builder.Features.Add(new CustomClassNamePass());
        //builder.Features.Add(new CustomDocumentClassifierPass());
        builder.Features.Add(new CustomDirectivesPass());

        //TracePhase.Attach(builder);
        return builder; // Supports chaining syntax
    }

    public static IReadOnlyDictionary<string, string> GetCustomData(RazorCodeDocument doc) {
        var data = new Dictionary<string, string>();
        foreach (var node in doc.GetDocumentIntermediateNode().Children) {
            if (node is CustomDataNode din) {
                data[din.Name] = din.Value;
            }
        }

        return data;
    }

    public static bool ShouldRegisterToRun(RazorCodeDocument doc) {
        // TODO: Update this to work exclusively with data left by the document classification pass.
        foreach (var node in doc.GetDocumentIntermediateNode().Children) {
            if (node is CustomDataNode din && din.Name == "page") {
                return din.Value != "none";
            }
        }

        // Default: does an underscore appear in the path
        foreach(var part in doc.Source.RelativePath.Split(c_directorySeparatorChars, StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)) {
            if (part[0] == '_')
                return false;
        }

        return true;
    }

    public static string GetClassFullName(RazorCodeDocument doc) {
        var documentNode = doc.GetDocumentIntermediateNode();
        var namespaceNode = documentNode.FindPrimaryNamespace();
        var classNode = documentNode.FindPrimaryClass();
        return string.Concat(namespaceNode.Content, ".", classNode.ClassName);
    }
}
