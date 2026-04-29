## Paths

**Site-Relative Path**: A full to a file relative to the site root. Begins with a forward slash and uses forward slashes in the path.

## Source File Processing

Files are processed according to their directory name, file name, and extension. Directory names and filenames may have an underscore (`_`) prefix which modifies how files are processed.

### Precedents
Since the configuration is provided in a C# file, all code must be compiled before configuration is processed. This requires that there be a way to determine which files are to be compiled and which are not outside of the configuration.

Precedents from other static site generators are the following:
* Jekyll: Copies verbatim (excludes from processing) any file without front matter or any files in directories designated for no processing in the configuration.
* Jekyll: Excludes from directly outputting anything in a directory that starts with underscore (though they may be processed or referenced in various ways).
* Hugo: Verbatim: Any file in the /static/ directory
* Graze: Anything in the assets/static directory

With these precedents in mind, I decided to use a directory name for compiling exclusion. Here are the rules.

### General Exclusions
* Regardless of where they are located in the directory hierarchy, hidden and system directories files (on Windows) are excluded.
* Everywhere except within the `/_verbatim/` directory, directories and files with names beginning with dot (`.`) are excluded. For example, the `\.git` directory is excluded.

### Directory Treatment
A directory with an underscore prefix can appear anywhere in the hierarchy. When that happens, it applies to that directory and all subdirectories. Special treatment for `_site`, `_verbatim`, and `_lowered` only applies at the project root level.

* `/_site/` directory: This is the default output directory. All contents are ignored for processing and replaced, updated, or removed during the build process.
* `/_verbatim/`: Files in this directory are designated to be copied verbatim (without processing) to the output directory. Like files copied from other directories, they are loaded into the `Site.Copy` collection which can be modified by `config.cs` or other code before it is processed.
* `/_lowered/`: All files are excluded. When the `lowering` command-line flag is presented, the `.cs` and/or `.html` versions of razor and markdown files are stored here for diagnostic purposes. 
* **All other directories**: Compiled files (extensions listed below) are compiled for later execution.
* **Directories *NOT* starting with underscore (`_`)**: Non-compiled files are designated for copying to the `/_site/` directory by loading them into the `Site.Copy` collection. By default, compiled files are registered to be run (see details under **Compiled file treatment** below.)
* **Directories starting with underscore and subdirectories thereof**: Non-compiled files are ignored. Compiled files are compiled for later execution as described below. By default, compiled files are not registered to be run (see details under **Compiled file treatment** below.)

### Compiled File Treatment
Compiled files are those with these extensions.

* `.cs` files: Classes are complied into the assembly. Only the top-level statements or the `main()` function (typically in the `config.cs` file) function will be executed by default. The rest are available to be called by other C# code.
* `.cshtml` files: The file is compiled to an object that is included in the overall assembly.
    * The default base class is `Bollard.RazorPage`.
* `.razor` files: The file is compiled to an object that is included in the overall assembly.
    * The default base class is `Bollard.RazorTemplate`.
    * This is the extension to use when creating non-html resources using Razor such as `.svg`.
* `.md` files: The file is processed from Markdown to HTML and then compiled with the Razor compiler as if it were a .cshtml file.
    * The default base class is `Bollard.RazorPage`
    * On very rare occasions an @ sign might be misinterpreted when a MarkDown file doesn't intentionally include any Razor code. However, the Razor compiler is very sensitive to the thing following @ being a valid C# expression so errors are rare. When a problem occurs, then the @ should be escaped. For example: @@DateTime.Now

#### Default Registration
Compiling a file simply includes the class in the compiled assembly. To be run, it must either be registered to run or be called by C# code somewhere else in the system. Registration to run requires indicating where the output should be stored.

Files with a `.cshtml` extension automatically registered *unless* the filename begins with underscore (`_`) or a directory in its path begins with underscore. Automatic registration is as if the file has a bare `@page` directive (see below). This means that the default base class is also set to `HtmlPage`. If the file has an actual `@page` or `@asset` directive then the directive overrides automatic registration.

#### Registration to be run with the @page or @asset directive
Compiling a file simply includes the class in the compiled assembly. To be run, it must either be registered to run or be called by C# code somewhere else in the system. Registration to run includes indicating where the output should be stored.

* `@page`: A bare `@page` directive sets the default base class to `HtmlTemplate` and registers the class built from the file to be run.
    * The output file will be the path in the output directory that corresponds to the location of the input file but with a `.html` extension.
* `@page "<path>"`: Sets the default base class to `HtmlTemplate` and registers the class built from the file to be run.
    * The path indicates the path destination filename relative to the output directory. The filename extension is part of the path.
    * Directory paths SHOULD use forward slashes.
    A leading slash indicates a path relative to the base output directory. Without leading slash it is relative to the directory corresponding to the location of the input file.
    * If the value is strictly an extension (e.g. `@page ".svg"`) then the directory path and filename will be the same as the input but the extension will be changed. To create a file that begins with a dot (period) you must provide a directory path as well.
* `@page "none"`: (Quotes are required). Sets the default base class to `HtmlTemplate` but does not register the class built from the file to be run. If, for some *wack* reason you need to create an output called "none" with no extension use `@page ".\none"`.
* `@asset`: Sets the default base class to `RazorTemplate` and registers the class built from the file to be run.
    * The output file will be the path in the output directory that corresponds to the location of the input file but with the (final) extension stripped. Thus "example.json.razor" will result in a file called "example.json".
* `@asset "<path>"`: Sets the default base class to `RazorTemplate` and registers the class built from the file to be run.
    * Path treatment is the same as with `@page`.
    * Thus, `@page` and `@asset` are the same except for the base class.

**Important**: Regardless of the value that appears in the directive, Razor code can set `Path` to a new value which will override any prior setting.

### Layouts

More to come here.

### Future compatibility enhancements

#### _ViewImports.cshtml 
* Documented here: https://www.learnrazorpages.com/razor-pages/files/viewimports
* Should strictly be directives.
* Options
    * call builder.SetDefaultImportFileName("_ViewImports.cshtml") and implement IRazorImportSourceProvider
    * Add MVC Razor Extensions through the right NuGet and builder.AddMvcRazorExtensions()
    * builder.Features.Add(IRazorDocumentClassifierPass) to indicate the special document classes for _ViewImports and _ViewStart
    * builder.Features.Add(new DefaultImportFeature(source))
    * Probably more. CoPilot seems to have most of the info.

#### _ViewStart.cshtml
* Code that is executed at the start of each Razor Page
* Usually used simply to set the default layout (though that could also be done in the _ViewImports.cshtml file)
* Strictly one top-level C# code block: @{ Layout = "_Layout" } plus comments, whitespace, etc.
* Implementation
    * Register a document classifier with RazorProjectEngine
    * builder.Features.Add(IRazorDocumentClassifierPass) to indicate the special document classes for _ViewImports and _ViewStart
    * Probably more. CoPilot seems to have most of the info.

#### @section directive
* Lets you produce sections that a layout will insert in the right places.
* Requires extensions to the Razor engine
* Like _ViewImports and _ViewStart, implemented by builder.AddMvcRazorExtensions();

#### Tag Helpers
* https://www.learnrazorpages.com/razor-pages/tag-helpers


## Future Feature Notes

## Feature Backlog
* Set current culture
    * In config.cs this should work: `CultureInfo.DefaultThreadCurrentCulture = cultureInfo; + CultureInfo.` `CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;`
    * However, a helper function may be appropriate.
    * Also, determine whether the default should be InvariantCulture rather than the OS setting.
    * Also, support page-scope culture changes.






