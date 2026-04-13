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
    public static readonly DirectiveDescriptor c_pageDirective =
        DirectiveDescriptor.CreateSingleLineDirective(c_pageDirectiveName,
            builder => {
                // Name and description arguments are just for diagnostic feedback to the user. They don't affect operation
                builder.AddOptionalStringToken("keyword", "'none'");
                builder.AddOptionalStringToken("path", "Site path to output destination.");
                builder.Usage = DirectiveUsage.FileScopedSinglyOccurring; // Modifies the prior setting.
            });

    public static readonly DirectiveDescriptor c_layoutDirective =
    DirectiveDescriptor.CreateSingleLineDirective(c_layoutDirectiveName,
        builder => {
            // Name and description arguments are just for diagnostic feedback to the user. They don't affect operation
            builder.AddOptionalStringToken("path", "Site path to output destination.");
            builder.Usage = DirectiveUsage.FileScopedSinglyOccurring; // Modifies the prior setting.
        });

    public sealed class CustomDirectiveIntermediateNode : IntermediateNode {
        required public string Name { get; set; }
        required public string Value { get; set; }

        public override IntermediateNodeCollection Children => IntermediateNodeCollection.ReadOnly; // Badly named but returns empty which is what we want.
        public override void Accept(IntermediateNodeVisitor visitor)
            => visitor.VisitDefault(this);
    }

    public class ClassifierPass : IRazorDirectiveClassifierPass {
        public int Order => 0;

        public RazorEngine? Engine { get; set; }

        public void Execute(RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) {
            Console.WriteLine("@Page ClassifierPass");
            TestPhase.DumpRecursive(1, documentNode);

            foreach (var directive in documentNode.FindDescendantNodes<Microsoft.AspNetCore.Razor.Language.Intermediate.DirectiveIntermediateNode>()) {
                var name = directive.DirectiveName;
                var value = directive.Tokens.FirstOrDefault()?.Content ?? string.Empty;
                Console.WriteLine($"==== Directive: @{name} {value}");

                var node = new CustomDirectiveIntermediateNode() {
                    Name = name,
                    Value = value
                };

                documentNode.Children.Add(node);
            }
        }
    }

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

    public static RazorProjectEngineBuilder AddToRazorProject(RazorProjectEngineBuilder builder) {
        builder.AddDirective(c_pageDirective);
        builder.AddDirective(c_layoutDirective);

        TestPhase.Attach(builder);

        //builder.Phases.Add(new ClassifierPhase());
        //builder.Phases.Add(new TestPhase());
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

