using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Bollard;

/// <summary>
/// Handles locating assemblies at compile time and resolving assemblies at runtime.
/// </summary>
/// <remarks>
/// This is a singleton class as there should only be one AssemblyReferenceManager and it should be tied to the default AssemblyLoadContext
/// </remarks>
internal class AssemblyReferenceManager {
    static readonly char[] c_slashes = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    #region Singleton Pattern

    public static readonly AssemblyReferenceManager Instance;

    static AssemblyReferenceManager() {
        Instance = new AssemblyReferenceManager();
    }

    private AssemblyReferenceManager() {
        // Register to resolve assemblies
        AssemblyLoadContext.Default.Resolving += Alc_ResolveAssembly;
    }

    #endregion Singleton Pattern

    readonly string? _referenceAssemblyDirectory = GetReferenceAssemblyDirectory();
    readonly string _localAssemblyDirectory = AppContext.BaseDirectory;
    HashSet<string> _assemblyRefSeen = new HashSet<string>();
    Dictionary<string, string> _assemblyRefMap = new Dictionary<string, string>();
    List<MetadataReference> _assemblyRefs = new List<MetadataReference>();

    public IEnumerable<MetadataReference> MetadataReferences => _assemblyRefs;

    public bool Add(string assemblyName, string? referencingPath = null, Location? location = null) {
        string? assemblyPath = null;
        bool isReferenceAssembly = false;

        // If the path is not absolute, find the assmbly
        if (Path.IsPathFullyQualified(assemblyName)) {
            assemblyPath = Path.Exists(assemblyName) ? assemblyName : null;
        }
        else {
            // If a relative path to the assembly is given, it must be located as specified relative to the referencing file
            if (assemblyName.IndexOfAny(c_slashes) >= 0) {
                if (referencingPath is null) {
                    throw new InvalidOperationException("Internal error: referencingPath should be specified when an assembly with a path is referenced.");
                }
                assemblyPath = Path.Combine(Path.GetFileName(referencingPath), assemblyName);
            }

            // Try the reference assembly directory
            if (assemblyPath is null && _referenceAssemblyDirectory is not null) {
                assemblyPath = Path.Combine(_referenceAssemblyDirectory, assemblyName);
                if (File.Exists(assemblyPath)) {
                    isReferenceAssembly = true;
                }
                else {
                    assemblyPath = null;
                }
            }

            // Try the local assembly directory (where the base executable is located)
            if (assemblyPath is null) {
                assemblyPath = Path.Combine(_localAssemblyDirectory, assemblyName);
                if (!File.Exists(assemblyPath)) {
                    assemblyPath = null;
                }
            }
        }

        // Report if not found
        if (assemblyPath is null)
            return false;

        var abyName = AssemblyName.GetAssemblyName(assemblyPath);
        if (_assemblyRefSeen.Add(abyName.FullName)) {
            _assemblyRefs.Add(MetadataReference.CreateFromFile(assemblyPath));
            if (!isReferenceAssembly) {
                _assemblyRefMap[abyName.FullName] = assemblyPath;
            }
        }
        return true;
    }

    /// <summary>
    /// Getting the path to the reference assembly directory is surprisingly complicated.
    /// That's because the path you get easily is to the runtime assemblies which may be stripped.
    /// It's also because the reference assemblies only have two parts to their version numbers.
    /// </summary>
    /// <returns>The reference assembly path or null.</returns>
    /// <remarks>
    /// Null may be returned if the application is packaged as single-file or NativeAOT.
    /// The contents may be enumerated to get a list of all possible assemblies.
    /// </remarks>
    private static string? GetReferenceAssemblyDirectory() {
        string? deps = (string?)AppContext.GetData("FX_DEPS_FILE");
        if (deps is null)
            return null;

        // Extract the runtime version (e.g., 8.0.2)
        var runtimeDir = Path.GetDirectoryName(deps)!;
        var version = Path.GetFileName(runtimeDir)!;
        var versionParts = version.Split('.');

        // Go all the way to the dotNetRoot (typical runtimeDir is C:\Program Files\dotnet\shared\MicrosoftNETCore.App\8.0.25)
        var dotnetRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir)));
        if (dotnetRoot is null)
            return null;

        // Assemble the new dir. Typical is C:\Program Files\dotnet\Microsoft.NETCore.App.Ref\8.0.25\ref\net8.0
        var assemblyDir = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", version, "ref", $"net{versionParts[0]}.{versionParts[1]}");

        return Path.Exists(assemblyDir) ? assemblyDir : null;
    }

    // Event handler for AssemblyLoadContext.Resolving
    private Assembly? Alc_ResolveAssembly(AssemblyLoadContext alc, AssemblyName assemblyName) {
        //Console.WriteLine($"===== Resolving Assembly `{assemblyName.FullName}`");
        if (_assemblyRefMap.TryGetValue(assemblyName.FullName, out string? path)) {
            return alc.LoadFromAssemblyPath(path);
        }
        return null;
    }
}
