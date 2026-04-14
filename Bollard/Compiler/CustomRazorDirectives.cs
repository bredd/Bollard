using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using static System.Net.Mime.MediaTypeNames;
using static Bollard.CustomRazorDirectives;

namespace Bollard;
internal class CustomRazorDirectives {
    const string c_pageDirectiveName = "page";
    const string c_layoutDirectiveName = "layout";

    // The descriptor tells the parser what this is.
    private static readonly DirectiveDescriptor c_pageDirective =
        DirectiveDescriptor.CreateSingleLineDirective(c_pageDirectiveName,
            builder => {
                // Name and description arguments are just for diagnostic feedback to the user. They don't affect operation
                builder.AddOptionalStringToken("keyword", "'none'");
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

    private abstract class CSharpGenerationNode : IntermediateNode {
        public override IntermediateNodeCollection Children => IntermediateNodeCollection.ReadOnly; // Badly named but returns empty which is what we want.
        public abstract void GenerateCSharp(DocumentIntermediateNode doc);
    }

    private class CustomDirectiveIntermediateNode : IntermediateNode {
        required public string Name { get; set; }
        required public string Value { get; set; }

        public override IntermediateNodeCollection Children => IntermediateNodeCollection.ReadOnly; // Badly named but returns empty which is what we want.
        public override void Accept(IntermediateNodeVisitor visitor)
            => visitor.VisitDefault(this);
    }

    private class LayoutDirectiveNode : CSharpGenerationNode {
        required public string Value { get; set; }

        public override void Accept(IntermediateNodeVisitor visitor)
            => visitor.VisitDefault(this);

        public override void GenerateCSharp(DocumentIntermediateNode doc) {
            var method = doc.FindPrimaryMethod();
            if (method is null)
                throw new InvalidOperationException("Expected method to be ready. Possibly the wrong pass.");
            method.Children.Insert(0, new CSharpCodeIntermediateNode {
                Children = {
                    new IntermediateToken {
                        Kind = TokenKind.CSharp,
                        Content = $"Layout = \"Value\";"
                    }
                }
            });
        }
    }

    public class ClassifierPass : IRazorDirectiveClassifierPass {
        public int Order => 0;

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) {
            foreach (var directive in documentNode.FindDescendantNodes<DirectiveIntermediateNode>()) {
                var name = directive.DirectiveName;
                var value = directive.Tokens.FirstOrDefault()?.Content ?? string.Empty;

                if (name == c_layoutDirectiveName) {
                    var node = new LayoutDirectiveNode() { Value = value };
                    documentNode.Children.Add(node);
                }
                else {
                    var node = new CustomDirectiveIntermediateNode() {
                        Name = name,
                        Value = value
                    };
                    documentNode.Children.Add(node);
                }
            }
        }
    }

    public class CSharpGenerationPhase : IRazorCSharpLoweringPhase {
        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument) {
            var documentNode = codeDocument.GetDocumentIntermediateNode();
            if (documentNode is null)
                throw new InvalidOperationException("DocumentIntermediateNode should be present here.");
            foreach (var node in documentNode.FindDescendantNodes<CSharpGenerationNode>()) {
                node.GenerateCSharp(documentNode);
            }
        }

        public static void Attach(RazorProjectEngineBuilder builder) {
            // Insert right before the last phase (CSharp Generation).
            var phases = builder.Phases;
            phases.Insert(phases.Count-1, new CSharpGenerationPhase());
        }
    }

#if DEBUG
    public class TestPhase : IRazorDirectiveClassifierPhase {
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
            Console.WriteLine($"{new string(' ', level * 2)}{node.GetType().Name}");

            switch (node) {
            case DirectiveIntermediateNode nd: {
                Console.WriteLine($"{new string(' ', level * 2)}: {nd.DirectiveName}");
                break;
            }

            case MalformedDirectiveIntermediateNode nd: {
                Console.WriteLine($"{new string(' ', level * 2)}: {nd.DirectiveName}");
                break;
            }

            case CustomDirectiveIntermediateNode nd: {
                Console.WriteLine($"=== @{nd.Name} {nd.Value}");
                break;
            }
            }

            foreach (var diagnostic in node.Diagnostics) {
                Console.WriteLine($"{new string(' ', level * 2)}Err: {diagnostic}");
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

        CSharpGenerationPhase.Attach(builder);
        //TestPhase.Attach(builder);

        builder.Features.Add(new ClassifierPass());
        return builder; // Supports chaining syntax
    }

    public static IReadOnlyDictionary<string, string> GetCustomDirectives(RazorCodeDocument doc) {
        var directives = new Dictionary<string, string>();
        foreach(var node in doc.GetDocumentIntermediateNode().Children) {
            if (node is CustomDirectiveIntermediateNode din) {
                directives[din.Name] = din.Value;
            }
        }

        return directives;
    }
}

