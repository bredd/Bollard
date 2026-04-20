using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Bollard;
static internal class CompilerHelp {
    public static Diagnostic ToCompilerDiagnostic(this RazorDiagnostic diag, string? projectBasePath = null) {
        string filePath = (projectBasePath != null) ? PathTool.GetLocalPath(diag.Span.FilePath, projectBasePath) : diag.Span.FilePath;
        if (filePath.StartsWith('/')) filePath = filePath.Substring(1);
        // The integer values between RazorDiagnosticSeverity and Microsoft.CodeAnalysis.DiagnosticSeverity are equivalent
        var descriptor = new DiagnosticDescriptor(diag.Id, "Razor " + diag.Id, "{0}", "Razor", (DiagnosticSeverity)(int)diag.Severity, true);
        var textSpan = new TextSpan(diag.Span.AbsoluteIndex, 0);
        var linePositionSpan = new LinePositionSpan(new LinePosition(diag.Span.LineIndex, diag.Span.CharacterIndex),
            new LinePosition(diag.Span.LineIndex, Math.Max(diag.Span.CharacterIndex, diag.Span.EndCharacterIndex)));
        var location = Location.Create(filePath, textSpan, linePositionSpan);
        return Diagnostic.Create(descriptor, location, diag.GetMessage());
    }
}
