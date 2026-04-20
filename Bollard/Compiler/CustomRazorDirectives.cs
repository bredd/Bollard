using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using static System.Net.Mime.MediaTypeNames;
using static Bollard.CustomRazorDirectives;

/* RazorProjectEngine phases and passes determined to date
 *   DefaultRazorParsingPhase
 *   DefaultRazorSyntaxTreePhase
 *   DefaultRazorTagHelperBinderPhase
 *   DefaultRazorIntermediateNodeLoweringPhase (first production of the parse tree)
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
internal class CustomRazorDirectives {

    const string c_pageDirectiveName = "page";
    const string c_layoutDirectiveName = "layout";
    const string c_namespaceDirectiveName = "namespace";

    // The descriptor tells the parser what this is.
    private static readonly DirectiveDescriptor c_pageDirective =
        DirectiveDescriptor.CreateSingleLineDirective(c_pageDirectiveName,
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

    private class CustomClassNamePass : IRazorDocumentClassifierPass {
        static char[] s_directorySeparators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        public int Order => 1001; // Run after built-in passes

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) {
            var classNode = documentNode.FindPrimaryClass();
            var namespaceNode = documentNode.FindPrimaryNamespace();
            Debug.Assert(classNode is not null && namespaceNode is not null);
            if (classNode is null || namespaceNode is null) return;

            var filePath = codeDocument.Source.FilePath;

            classNode.ClassName = PathTool.SanitizeToCSharpName(Path.GetFileNameWithoutExtension(filePath));
            string ns;
            if (codeDocument.TryComputeNamespace(true, out ns)) {
                namespaceNode.Content = ns;
            }
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
    public class TestPhase : IRazorEnginePhase {
        string _label;

        public TestPhase(string label) {
            _label = label;
        }

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument) {
            Console.WriteLine("TestPhase: " + _label);
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
                builder.Phases.Insert(i * 2, new TestPhase(i.ToString()));
            }
        }
    }
#endif // DEBUG

    public static RazorProjectEngineBuilder AddToRazorProject(RazorProjectEngineBuilder builder) {
        builder.AddDirective(c_pageDirective);
        builder.AddDirective(c_layoutDirective);
        builder.Features.Add(new CustomClassNamePass());
        builder.Features.Add(new CustomDirectivesPass());

        //TestPhase.Attach(builder);
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

    public static string GetClassFullName(RazorCodeDocument doc) {
        var documentNode = doc.GetDocumentIntermediateNode();
        var namespaceNode = documentNode.FindPrimaryNamespace();
        var classNode = documentNode.FindPrimaryClass();
        return string.Concat(namespaceNode.Content, ".", classNode.ClassName);
    }
}
