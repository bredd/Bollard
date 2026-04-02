using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;
internal class SiteBuilder {

    const string c_SiteProgramResource = "Bollard.Resources.SiteProgram.cs";

    static readonly EnumerationOptions c_rootDirEnumOptions = new EnumerationOptions() {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };
    static readonly EnumerationOptions c_verbatimEnumOptions = new EnumerationOptions() {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        MaxRecursionDepth = 6,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false
    };

    List<Copy> _copies = new List<Copy>();
    List<string> _csSources = new List<string>();
    List<Page> _pages = new List<Page>();


    /// <summary>
    /// Fully-qualified native format root of the directory tree from which the site will be built.
    /// </summary>
    public string SourceDir { get; set; } = string.Empty;

    /// <summary>
    /// For single-file mode, set this value to the file. Otherwise it should be null
    /// </summary>
    public string? SourceFile { get; set; }

    private void LoadVerbatimDir(DirectoryInfo di) {
        foreach (var fi in di.EnumerateFiles("*", c_verbatimEnumOptions)) {
            _copies.Add(new Copy(fi.FullName, PathTool.GetSiteRelativePath(fi.FullName, di.FullName)));
        }
    }

    private void LoadDir(DirectoryInfo di, bool copyAssets, bool recurse) {
        var options = new EnumerationOptions() {
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MaxRecursionDepth = recurse ? 6 : 0,
            RecurseSubdirectories = recurse,
            ReturnSpecialDirectories = false
        };
        foreach(var fi in di.EnumerateFiles("*", options)) {
            if (fi.Name[0] == '.')
                continue;   // No .gitignore, .env, etc. If those are required then they go in _verbatim

            switch (fi.Extension.ToLowerInvariant()) {
            case ".cs":
                _csSources.Add(fi.FullName);
                break;

            case ".cshtml":
            case ".razor":
            case ".md":
                _pages.Add(new Page(fi.FullName, PathTool.GetSiteRelativePath(fi.FullName, SourceDir)));
                break;

            default:
                if (copyAssets) {
                    _copies.Add(new Copy(fi.FullName, PathTool.GetSiteRelativePath(fi.FullName, di.FullName)));
                }
                break;
            }
        }
    }

    private void LoadFiles() {
        var di = new DirectoryInfo(SourceDir);
        LoadDir(di, true, false);

        foreach (var sdi in di.GetDirectories("*", c_rootDirEnumOptions)) {
            if (sdi.Name[0] == '.')
                continue; // Skip linux-style special directories.
            if (string.Equals(sdi.Name, "_site", StringComparison.OrdinalIgnoreCase))
                continue; // Skip the output directory even if it's a case mismatch
            if (string.Equals(sdi.Name, "_verbatim", StringComparison.Ordinal)) {
                LoadVerbatimDir(sdi);
                continue;
            }
            LoadDir(sdi, true, sdi.Name[0] == '_');
        }
    }

    private void BuildAssembly() {
        var builder = new AssemblyBuilder();
        builder.SourceDir = SourceDir;

        // Reference self
        builder.AddAssemblyReference(typeof(Bollard.Bridge).Assembly.Location);

        // Add the common code
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(c_SiteProgramResource);
            if (stream is null)
                throw new InvalidOperationException($"Resource '{c_SiteProgramResource}' not found in assembly.");
            builder.ParseCSharp(stream, c_SiteProgramResource);
        }

        // If single-file mode, add the one file
        if (SourceFile is not null) {
            builder.ParseCSharp(Path.Combine(SourceDir, SourceFile));
        }

        // Else, add all source file in the directory tree

        builder.ParseCSharp(@"C:\Users\brand\Source\bredd\Bollard\Tests\NewArchitecture\Config.cs");

        // Add default config if no default entry point
        // TODO: Have a test case that uses a Main function instead of top-level statements
        if (!builder.HasEntryPoint()) {
            builder.ParseCSharpString(c_defaultConfig, "_defaultConfig.cs");
        }

        builder.BuildAssembly();
        builder.ReportDiagnostics(minSeverity: Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden);
        if (builder.SuccessLevel >= Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            return -1;

    }

    public void Go() {


    }
}
