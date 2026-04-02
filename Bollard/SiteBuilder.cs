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
    const string c_defaultConfig = @"Console.WriteLine(""(Using Default Configuration)"");";    // TODO: Make this only print in verbose mode.

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

    /// <summary>
    /// Fully-qualified native format root of the directory tree from which the site will be built.
    /// </summary>
    public string SourceDir { get; private set; } = string.Empty;

    /// <summary>
    /// For single-file mode, set this value to the file. Otherwise it should be null
    /// </summary>
    public string? SourceFile { get; private set; }

    private void LoadVerbatimDir(DirectoryInfo di) {
        foreach (var fi in di.EnumerateFiles("*", c_verbatimEnumOptions)) {
            _copies.Add(new Copy(fi.FullName, PathTool.GetSiteRelativePath(fi.FullName, di.FullName)));
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
            _pages.Add(new Page(fi.FullName, PathTool.GetSiteRelativePath(fi.FullName, SourceDir)));
            return true;

        default:
            if (!copyAssets) return false;
            _copies.Add(new Copy(fi.FullName, PathTool.GetSiteRelativePath(fi.FullName, SourceDir)));
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

        // Parse all of the CS sources
        foreach(var source in _csSources) {
            builder.ParseCSharp(source);
        }

        // Add default config if no default entry point
        // TODO: Have a test case that uses a Main function instead of top-level statements
        if (!builder.HasEntryPoint()) {
            builder.ParseCSharpString(c_defaultConfig, "_defaultConfig.cs");
        }

        builder.BuildAssembly();
        builder.ReportDiagnostics(minSeverity: Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        if (builder.SuccessLevel >= Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            throw new ApplicationException("Errors in the build.");

        _assembly = builder.Assembly;
    }

    public void Build() {
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
        Console.WriteLine($"Compiled in {stopwatch.ElapsedMilliseconds:N0}ms.");
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
        Console.WriteLine($"Run completed in {stopwatch.ElapsedMilliseconds:N0}ms.");
    }
}
