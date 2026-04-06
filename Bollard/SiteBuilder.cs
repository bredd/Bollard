using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Bollard;

enum VerbosityLevel {
    Quiet = 0,
    Default = 1, 
    Verbose = 2,
    Extra = 3
}

internal class SiteBuilder {

    const string c_SiteProgramResource = "Bollard.Resources.SiteProgram.cs";
    const string c_defaultConfig = @"Console.WriteLine(""(Using Default Configuration)"");";    // TODO: Make this only print in verbose mode.
    const string c_loweredDirectory = "_lowered";

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
    Assembly? _assembly;

    public SiteBuilder(string source) {
        try {
            source = Path.GetFullPath(source);
        }
        catch (Exception ex) {
            throw new ApplicationException("Invalid source path: " + source, ex);
        }
        if (File.Exists(source)) {
            SourceDir = Path.GetDirectoryName(source)!;
            SourceFile = source;
        }
        else if (Directory.Exists(source)) {
            SourceDir = source;
        }
        else {
            throw new ApplicationException("Source directory or file not found: " + source);
        }
    }

    public VerbosityLevel Verbosity { get; set; }

    public bool Lowering { get; set; }

    public DiagnosticSeverity DiagnosticLevel { get; set; } = DiagnosticSeverity.Warning;

    /// <summary>
    /// Fully-qualified native format root of the directory tree from which the site will be built.
    /// </summary>
    public string SourceDir { get; private set; } = string.Empty;

    public string[] SiteArgs { get; set; } = [];

    /// <summary>
    /// For single-file mode, set this value to the file. Otherwise it should be null
    /// </summary>
    public string? SourceFile { get; private set; }

    public DiagnosticSeverity BuildSuccessLevel { get; private set; }

    private void LoadVerbatimDir(DirectoryInfo di) {
        foreach (var fi in di.EnumerateFiles("*", c_verbatimEnumOptions)) {
            _copies.Add(new Copy(fi.FullName, PathTool.GetLocalPath(fi.FullName, di.FullName)));
        }
    }

    private bool LoadFile(FileInfo fi, bool copyAssets) {
        if (fi.Name[0] == '.')
            return false;

        switch (fi.Extension.ToLowerInvariant()) {
        case ".cs":
            _csSources.Add(fi.FullName);
            return true;

        case ".cshtml":
        case ".razor":
        case ".csmd":
        case ".md":
            _pages.Add(new Page(fi.FullName, PathTool.GetLocalPath(fi.FullName, SourceDir)));
            return true;

        default:
            if (!copyAssets) return false;
            _copies.Add(new Copy(fi.FullName, PathTool.GetLocalPath(fi.FullName, SourceDir)));
            return true;
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
            LoadFile(fi, copyAssets);
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
            if (string.Equals(sdi.Name, c_loweredDirectory, StringComparison.OrdinalIgnoreCase))
                continue; // Skip the lowered directory even if it's a case mismatch
            if (string.Equals(sdi.Name, "_verbatim", StringComparison.Ordinal)) {
                LoadVerbatimDir(sdi);
                continue;
            }
            LoadDir(sdi, true, sdi.Name[0] == '_');
        }
    }

    private void BuildAssembly() {
        
        // Prep the C# compiler
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

        // Parse all of the CS sources
        foreach(var source in _csSources) {
            builder.ParseCSharp(source);
        }

        // Prep the Razor engine
        var razorEngine = RazorProjectEngine.Create(
            RazorConfiguration.Default,
            RazorProjectFileSystem.Create(SourceDir),
            builder => {
                //builder.SetRootNamespace("RootNamespace"); // Namespace prefix
                builder.SetNamespace("HooDoo");       // Can be overridden by @namespace
                builder.ConfigureClass((document, node) => {
                    node.BaseType = "RazorTemplate";  // This can be overridden by the @inherits directive
                    node.ClassName = "Template";      // This could be derived from the filename by using document.Source.FilePath;
                });
                // The following will add a new directive to be parsed (e.g. @mydirective Go). But making The directive do anything is a different task.
                // builder.AddDirective(DirectiveDescriptor.CreateSingleLineDirective("mydirective", b => b.AddMemberToken("memberTokenName", "memberTokenDescription")));
            });

        // Create the _lowered directory if needed
        var loweredDir = string.Empty;
        if (Lowering) {
            loweredDir = Path.Combine(SourceDir, c_loweredDirectory);
            // Does nothing if the directory already exists
            Directory.CreateDirectory(loweredDir);
        }

        // Convert and parse all of the Razor sources
        foreach (var page in _pages) {
            var item = razorEngine.FileSystem.GetItem(page.Src, FileKinds.Legacy); // Alternative is FileKinds.Component which does not support the MVC extensions
            // TODO: Gracefully degrade if a file is not found.
            if (!item.Exists)
                throw new FileNotFoundException("File not found in RazorProjectFileSystem", page.Src);
            var doc = razorEngine.Process(item);

            // This doesn't work to find the @page directive because the MVC extensions are not loaded.
            // My expected plan is to write my own equivalent extensions but that is TBD.
            var ir = doc.GetDocumentIntermediateNode();
            foreach (var node in ir.FindDescendantNodes<DirectiveIntermediateNode>()) {
                Console.WriteLine(node);
            }

            if (Lowering) {
                using var writer = new StreamWriter(PathTool.GetAbsolutePath(loweredDir, PathTool.ChangeExtension(page.Dst, ".cs")));
                writer.Write(doc.GetCSharpDocument().GeneratedCode);
            }

            // TODO: Add to compilation.
        }

        // Add default config if no default entry point
        // TODO: Have a test case that uses a Main function instead of top-level statements
        if (!builder.HasEntryPoint()) {
            builder.ParseCSharpString(c_defaultConfig, "_defaultConfig.cs");
        }

        BuildSuccessLevel = builder.BuildAssembly();
        builder.ReportDiagnostics(minSeverity: DiagnosticLevel);

        _assembly = builder.Assembly;
    }

    public DiagnosticSeverity Build() {
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        // If single-file mode, prep appropriately
        if (SourceFile is not null) {
            var fi = new FileInfo(SourceFile);
            if (!fi.Exists)
                throw new FileNotFoundException("Source file not found: " + fi.FullName);
            SourceDir = fi.DirectoryName!;
            // TODO: Destination directory = SourceDir
            if (!LoadFile(fi, false))
                throw new ApplicationException("Nothing to do with source file: " + fi.FullName);
        }

        // Otherwise, prep a directory tree for processing
        else {
            if (!Directory.Exists(SourceDir)) {
                throw new ApplicationException("Directory not found: " + SourceDir);
            }
            // TODO: Destination directory = SourceDir + "/_site";
            LoadFiles();
        }

        BuildAssembly();

        stopwatch.Stop();
        if (Verbosity >= VerbosityLevel.Default)
            Console.WriteLine($"Compiled in {stopwatch.ElapsedMilliseconds:N0}ms.");

        return BuildSuccessLevel;
    }

    public void Run() {
        if (_assembly is null)
            throw new InvalidOperationException("No assembly to run.");

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        // Invoke the assembly entrypoint to update the configuration.
        // TODO: Command-line argument passthrough to the entrypoint.
        _assembly.EntryPoint!.Invoke(null, new object?[] { Array.Empty<string>() });

        stopwatch.Stop();
        if (Verbosity >= VerbosityLevel.Default)
            Console.WriteLine($"Build completed in {stopwatch.ElapsedMilliseconds:N0}ms.");
    }
}
