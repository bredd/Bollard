## Paths

**Site-Relative Path**: A full to a file relative to the site root. Begins with a forward slash and uses forward slashes in the path.

## Source File Processing

** Unprocessed Files
Since the configuration is provided in a C# file, all code must be compiled before configuration is processed. This requires that there be a way to determine which files are to be compiled and which are not outside of the configuration.

Precedents from other static site generators are the following:
* Jekyll: Copies verbatim (excludes from processing) any file without front matter or any files in directories designated for no processing in the configuration.
* Jekyll: Excludes from directly outputting anything in a directory that starts with underscore (though they may be processed or referenced in various ways).
* Hugo: Verbatim: Any file in the /static/ directory
* Graze: Anything in the assets/static directory

With these precedents in mind, I decided to use a directory name for compiling exclusion. Here are the rules.

**General Exclusions**
* Regardless of where they are located in the directory hierarchy, hidden and system directories files (on Windows) and directories with names (`.`) are ignored. This includes things like the `\.git` directory.
* Everywhere except within the `/_verbatim/` directory, directories and files with names beginning with dot (`.`) are ignored.

**Directory Treatment**
These rules apply to direct subdirectories of the project root. Directories further down in the hierarchy are processed according to their parent (except for the exclusion rules above).

* `/_site/` directory: This is the default output directory. All contents are ignored for processing and replaced, updated, or removed during the build process.
* `/_verbatim/`: Files in this directory are designated to be copied verbatim (without processing) to the `_site` directory. Like files copied from other directories, they are loaded into the `Site.Copy` collection which can be modified by the `config.cs` file before it is processed.
* **All other directories** (directories other than `\_verbatim\`): Files with a `.cs`, `.cshtml`, `.razor`, or `.md`, or `.csmd` extension are compiled for later execution. This includes directories with an underscore prefix.
* **Directories *NOT* starting with underscore (`_`)**: Non-build files are designated for copying to the `/_site/` directory by loading them into the `Site.Copy` collection.
* **Directories starting with underscore**: All files other than those that were compiled are ignored. They remain available for code to reference.

**Compiled File Treatment**
* `.cs` files: Classes are complied into the assembly. Only the top-level statements or the `main()` function (typically in the `config.cs` file) function will be executed by default.
* `.cshtml` and `.razor` files:
* `.md` and `.csmd` files:

