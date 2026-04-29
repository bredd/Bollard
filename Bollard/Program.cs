using System.IO.Enumeration;
using System.Reflection;
using System.Text.Json.Nodes;
using Bollard;
using BollardBlogger;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

const string c_syntax = @"Syntax: BollardBlogger [sourcePath]";
const string c_configFilename = "_bollard_config.json";

// Parse the command line
string? source = null;
VerbosityLevel verbosity = VerbosityLevel.Default;
bool lowering = false;
DiagnosticSeverity diagnosticLevel = DiagnosticSeverity.Warning;
bool help;
string[] siteArgs = [];
for (int i = 0; i < args.Length; i++) {
    switch (args[i]) {
    case "-quiet":
        verbosity = VerbosityLevel.Quiet;
        break;

    case "-verbose":
        verbosity = VerbosityLevel.Verbose;
        break;

    case "-extraverbose":
        verbosity = VerbosityLevel.Extra;
        break;

    case "-lowering":
        lowering = true;
        break;

    case "-diagnostic":
        ++i;
        if (!Enum.TryParse(args[i], true, out diagnosticLevel))
            throw new ApplicationException("Unknown diagnostic severity: " + args[i]);
        break;

    case "-h":
        help = true;
        goto loopbreak;

    case "--":
        siteArgs = args[(i + 1)..];
        goto loopbreak;

    default:
        if (args[i][0] == '-')
            throw new ApplicationException("Unknown option: " + args[i]);
        if (source is not null)
            throw new ApplicationException("Unexpected argument: " + args[i]);
        source = args[i];
        break;
    }
}
loopbreak:

if (source is null)
    source = Environment.CurrentDirectory;

Console.WriteLine("Producing site from: " + source);

// SiteBuilder checks that the file or directory exists.
var siteBuilder = new SiteBuilder(source) {
    Verbosity = verbosity,
    Lowering = lowering,
    DiagnosticLevel = diagnosticLevel,
    SiteArgs = siteArgs
};

var successLevel = siteBuilder.Compile();
siteBuilder.ReportDiagnostics(minSeverity: DiagnosticSeverity.Hidden);
siteBuilder.Run(); // For now, even if there were errors
return 0;

// Parse the command line
string? srcDir = null;
string? srcFile = null;
bool writeSyntax = false;
foreach(var arg in args) {
    // Only accept one argument for now
    if (srcDir is not null || srcFile is not null) {
        Console.WriteLine($"Unexpected argument: {arg}");
        writeSyntax = true;
    }
    else if (arg.ToLowerInvariant() == "-h") {
        writeSyntax = true;
    }
    else {
        var fullPath = Path.GetFullPath(arg);
        if (File.Exists(fullPath)) {
            srcFile = fullPath;
        }
        else if (Directory.Exists(fullPath)) {
            srcDir = fullPath;
        }
        else {
            Console.WriteLine($"File or path does not exist: {arg} ({fullPath})");
            writeSyntax = true;
        }
    }
}
if (srcDir is null && srcFile is null) {
    srcDir = Environment.CurrentDirectory;
}

if (writeSyntax) {
    Console.WriteLine(c_syntax);
    return 1;
}

try {
    Console.WriteLine("Producing website from: " + srcDir ?? srcFile);

    Site site;

    // If building from a directory
    if (srcDir is not null) {

        // If it has a configuration file
        var configFilename = Path.Combine(srcDir, c_configFilename);
        if (File.Exists(configFilename)) {
            JsonObject config;
            using (var stream = File.OpenRead(configFilename)) {
                config = (JsonObject)JsonNode.Parse(stream)!;
            }
            site = new Site(srcDir, config);
        }

        else {
            // TODO: Create a site object with default configuration for a folder without config
            throw new ApplicationException($"No '{c_configFilename}' found in path '{srcDir}'.");
        }
    }

    // Else, building from a file
    else {
        site = new Site(srcFile!);
    }

    Console.WriteLine("output: " + site.LocalDstFolder);
    Console.WriteLine("siteRoot: " + site.Url);
    site.Prep();
    site.Render();
}
catch (Exception ex) {
    Console.WriteLine(ex.ToString());
}

return 0;

