using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    const string c_rootNamespace = "BollardPages";

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

    static readonly DiagnosticDescriptor c_diagSourceFileNotFound = new DiagnosticDescriptor("BB1002", "Source File Not Found", "Source file '{0}' not found", "Bollard", DiagnosticSeverity.Error, true);


    List<Diagnostic> _diagnostics = new List<Diagnostic>();
    List<Copy> _copies = new List<Copy>();
    List<string> _csSources = new List<string>();
    List<string> _razorSources = new List<string>();
    List<string> _buildAssets = new List<string>(); // A list of class names to be built by default
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

    public DiagnosticSeverity CompileSuccessLevel { get; private set; }

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
        case ".md":
            _razorSources.Add(fi.FullName);
            return true;

        default:
            if (!copyAssets)
                return false;
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
        foreach (var fi in di.EnumerateFiles("*", options)) {
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
        foreach (var source in _csSources) {
            builder.ParseCSharp(source);
        }

        // Parse all of the Razor sources
        ParseRazorSources(builder);

        // Add default config if no default entry point
        // TODO: Have a test case that uses a Main function instead of top-level statements
        if (!builder.HasEntryPoint()) {
            builder.ParseCSharpString(c_defaultConfig, "_defaultConfig.cs");
        }

        builder.BuildAssembly();

        _diagnostics.AddRange(builder.Diagnostics);
        _assembly = builder.Assembly;
    }

    public DiagnosticSeverity Compile() {
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

        // Check success level
        var successLevel = DiagnosticSeverity.Hidden;
        foreach (var diagnostic in _diagnostics) {
            if (successLevel < diagnostic.Severity) {
                successLevel = diagnostic.Severity;
            }
        }
        CompileSuccessLevel = successLevel;
        return successLevel;
    }

    public DiagnosticSeverity ReportDiagnostics(TextWriter? writer = null, DiagnosticSeverity minSeverity = DiagnosticSeverity.Warning) {
        if (writer is null)
            writer = Console.Out;
        var successLevel = DiagnosticSeverity.Hidden;
        foreach (var diagnostic in _diagnostics) {
            if (diagnostic.Severity >= minSeverity) {
                writer.WriteLine(diagnostic.ToString());
            }
            if (successLevel < diagnostic.Severity) {
                successLevel = diagnostic.Severity;
            }
        }
        return successLevel;
    }

    public void Run() {
        if (_assembly is null)
            throw new InvalidOperationException("No assembly to run.");

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        // Find each assetbuilder that should be run
        var assetBuilders = new List<RazorTemplate>();
        foreach (var className in _buildAssets) {
            var classType = _assembly.GetType(className, false);
            if (classType is null) {
                Console.WriteLine($"Error: Failed to find class {classType}.");
                continue;
            }

            var obj = Activator.CreateInstance(classType);
            RazorTemplate? assetBuilder = obj as RazorTemplate;

            if (assetBuilder is null) {
                var alc1 = AssemblyLoadContext.GetLoadContext(obj.GetType().Assembly);
                var alc2 = AssemblyLoadContext.GetLoadContext(typeof(RazorTemplate).Assembly);

                Console.WriteLine(alc1.Name);
                Console.WriteLine(alc2.Name);

                Console.WriteLine($"Error: Class {className} is not RazorTemplate");
                continue;
            }
            assetBuilders.Add(assetBuilder);
        }

        // TODO: Clean this up.
        var destDir = Path.Combine(SourceDir, "_site");
        Directory.CreateDirectory(destDir);

        // Invoke the assembly entrypoint to update the configuration.
        // TODO: Command-line argument passthrough to the entrypoint.
        // TODO: Add assetbuilders list to the locals that the entrypoint can manipulate.
        // TODO: Let entrypoint change the destination directory.
        _assembly.EntryPoint!.Invoke(null, new object?[] { Array.Empty<string>() });

        foreach (var assetBuilder in assetBuilders) {
            var razorTemplate = assetBuilder as RazorTemplate;
            if (razorTemplate is null) {
                Console.WriteLine("Error: Only razorTemplate classes supported so far.");
                // TODO: Determine how paths are handled for other template types. Perhaps Path becomes a part of IAssetBuilder.
                continue;
            }

            // TODO: Set parameters such as site that the assetBuilder should be able to access
            razorTemplate.Path = razorTemplate.GetType().FullName + ".html";

            assetBuilder.Produce();

            using (var stream = File.Create(PathTool.GetAbsolutePath(destDir, razorTemplate.Path))) {
                assetBuilder.Deliver(stream);
            }
        }

        stopwatch.Stop();
        if (Verbosity >= VerbosityLevel.Default)
            Console.WriteLine($"Site built in {stopwatch.ElapsedMilliseconds:N0}ms.");
    }

    private void ParseRazorSources(AssemblyBuilder builder) {

        // Prep the Razor engine
        var razorEngine = RazorProjectEngine.Create(
            RazorConfiguration.Default,
            RazorProjectFileSystem.Create(SourceDir),
            builder => {
                builder.SetRootNamespace(c_rootNamespace); // Namespace prefix recoginized by RazorCodeDocument.TryComputeNamespace
                RazorCustomizations.AddToRazorProject(builder);
            });

        // Create the _lowered directory if needed
        var loweredDir = string.Empty;
        if (Lowering) {
            loweredDir = Path.Combine(SourceDir, c_loweredDirectory);
            // Does nothing if the directory already exists
            Directory.CreateDirectory(loweredDir);
        }

        // Convert and parse all of the Razor sources
        foreach (var filename in _razorSources) {
            var item = razorEngine.FileSystem.GetItem(filename, FileKinds.Legacy); // Alternative is FileKinds.Component which does not support the MVC extensions
            if (!item.Exists) {
                _diagnostics.Add(Diagnostic.Create(c_diagSourceFileNotFound, CompilerHelp.CreateLocation(filename), filename));
                continue;
            }
            var doc = razorEngine.Process(item);
            var csDoc = doc.GetCSharpDocument();

            // Aggregate diagnostics
            var worstDiagnostic = DiagnosticSeverity.Hidden;
            foreach (RazorDiagnostic? diag in csDoc.Diagnostics) {
                var ddiag = builder.ToCompilerDiagnostic(diag);
                _diagnostics.Add(ddiag);
                if (worstDiagnostic < ddiag.Severity)
                    worstDiagnostic = ddiag.Severity;
            }

            // Export lowered version if requested
            if (Lowering) {
                var loweredFilename = PathTool.GetAbsolutePath(loweredDir,
                    PathTool.ChangeExtension(PathTool.GetLocalPath(filename, SourceDir), ".cs"));
                var dir = Path.GetDirectoryName(loweredFilename)!;
                if (dir.Length > loweredDir.Length)
                    Directory.CreateDirectory(dir);
                using var writer = new StreamWriter(loweredFilename);
                writer.Write(doc.GetCSharpDocument().GeneratedCode);
            }

#if DEBUG
            Console.WriteLine();
            Console.WriteLine($"=== Class: {RazorCustomizations.GetClassFullName(doc)} worstDiagnostic={worstDiagnostic}");
            foreach (var datum in RazorCustomizations.GetCustomData(doc)) {
                Console.WriteLine($"  CustomData: name={datum.Key} value={datum.Value}");
            }
            Console.WriteLine($"  Register to run: {RazorCustomizations.ShouldRegisterToRun(doc)}");
#endif

            // If error, skip to the next
            if (worstDiagnostic >= DiagnosticSeverity.Error) {
                continue;
            }

            // Add to the set of code to be compiled
            builder.ParseCSharpString(csDoc.GeneratedCode, builder.GetDiagnosticPath(filename));

            // Conditionally add to the list of classes to run
            if (RazorCustomizations.ShouldRegisterToRun(doc)) {
                _buildAssets.Add(RazorCustomizations.GetClassFullName(doc));
            }

        }

#if DEBUG
        Console.WriteLine("=== Finished Razor Parsing");
        Console.WriteLine();
#endif

    }

}
