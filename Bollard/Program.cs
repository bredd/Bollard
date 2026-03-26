using System.IO.Enumeration;
using System.Reflection;
using System.Text.Json.Nodes;
using Bollard;
using BollardBlogger;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

const string c_SiteProgramResource = "Bollard.Resources.SiteProgram.cs";
const string c_syntax = @"Syntax: BollardBlogger [sourcePath]";
const string c_configFilename = "_bollard_config.json";
const string c_defaultConfig = @"Console.WriteLine(""(Using Default Configuration)"");";    // TODO: Make this only print in verbose mode.

Console.WriteLine("Testing...");
var builder = new AssemblyBuilder();
builder.SourceDir = @"C:\Users\brand\Source\bredd\Bollard\Tests\NewArchitecture";
builder.AddAssemblyReference(typeof(Bollard.Project).Assembly.Location);

// Add the common code
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(c_SiteProgramResource);
    if (stream is null)
        throw new InvalidOperationException($"Resource '{c_SiteProgramResource}' not found in assembly.");
    builder.ParseCSharp(stream, c_SiteProgramResource);
}

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

var entryPoint = builder.Assembly!.EntryPoint!; // If an entry point doesn't exist it would have errored before this point.

// Find the Prep function and call it
//var prepMethod = entryPoint.DeclaringType!.GetMethod("Prep", BindingFlags.Public | BindingFlags.Static, null, [typeof(string)], null);
//if (prepMethod is not null) {
//    prepMethod.Invoke(null, ["Phred was here."]);
//}

Console.WriteLine("Invoking EntryPoint.");

builder.Assembly!.EntryPoint!.Invoke(null, new object?[] { Array.Empty<string>() });

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

