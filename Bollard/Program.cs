using System.IO.Enumeration;
using System.Text.Json.Nodes;
using BollardBlogger;

const string c_syntax = @"Syntax: BollardBlogger [sourcePath]";
const string c_configFilename = "_bollard_config.json";

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
            Console.WriteLine("File or path does not exist: {arg}");
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

