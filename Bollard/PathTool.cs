using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;

namespace Bollard;
static internal class PathTool {

#if DEBUG
    private static readonly char[] c_siteRelativeProhibitedChars = { '\\', ':' };
#endif
    private static readonly char[] c_slashes = { '/', '\\' };

    public static void ValidateSiteRelativePath(string path) {
        if (path[0] != '/')
            throw new InvalidOperationException($"Site-relative paths must start with a forward slash: {path}");
#if DEBUG
        if (path.IndexOfAny(c_siteRelativeProhibitedChars) >= 0)
            throw new InvalidOperationException($"Site-relative paths must use forward slashes and no colons: {path}");
#endif
    }

    /// <summary>
    /// A local path starts with a slash and indicates the path relative to the base of the site. It uses forward slashes as path separators.
    /// </summary>
    /// <param name="path">The path to convert.</param>
    /// <param name="basePath">The base path of the site.</param>
    /// <returns>A local path.</returns>
    /// <remarks>
    /// The path MUST start with basePath.
    /// </remarks>
    public static string GetLocalPath(string path, string basePath) {
        int diLen = basePath.Length;
        Debug.Assert(path.StartsWith(basePath));
        Debug.Assert(basePath[diLen - 1] != Path.DirectorySeparatorChar);
        if (!path.StartsWith(basePath)) return path;    // Release version fallback.
        if (path.Length <= diLen)
            return "/";
        Debug.Assert(path[diLen] == Path.DirectorySeparatorChar || path[diLen] == '/');
        var localName = path.Substring(diLen);
        if (Path.DirectorySeparatorChar != '/')
            localName = localName.Replace(Path.DirectorySeparatorChar, '/');
        return localName;
    }

    /// <summary>
    /// Gets an absolte OS-compatible path from a site-relative path.
    /// </summary>
    /// <param name="basePath">Absolute path in OS format of the destination.</param>
    /// <param name="localPath">Site-relative path using forward slashes.</param>
    /// <returns></returns>
    public static string GetAbsolutePath(string basePath, string localPath) {
        if (Path.DirectorySeparatorChar != '/')
            localPath = localPath.Replace('/', Path.DirectorySeparatorChar);
        if (localPath.StartsWith(Path.DirectorySeparatorChar))
            localPath = localPath.Substring(1);
        var absolutePath = Path.Combine(basePath, localPath);
        if (!absolutePath.StartsWith(basePath))
            throw new ApplicationException("Attempt to access files outside the build directory: " + localPath);
        return absolutePath;
    }

    public static string ChangeExtension(string path, string extension) {
        int dot = path.LastIndexOf('.');
        int slash = path.LastIndexOfAny(c_slashes);
        if (dot > slash)
            path = path.Substring(0, dot);
        return string.Concat(path, extension);
    }

    /// <summary>
    /// Converts a filename (just a name, not a path) to a valid C# name.
    /// </summary>
    /// <param name="filename"></param>
    /// <returns>A valid C# name.</returns>
    public  static string SanitizeToCSharpName(string filename) {
        var chars = filename.ToCharArray();
        var result = new StringBuilder(chars.Length);

        // First character: must be a valid identifier-start character
        char first = chars[0];
        if (SyntaxFacts.IsIdentifierStartCharacter(first)) {
            result.Append(first);
        }
        else {
            // If invalid, prefix with '_' and sanitize the first character
            result.Append('_');

            if (SyntaxFacts.IsIdentifierPartCharacter(first))
                result.Append(first);
            else
                result.Append('_');
        }

        // Remaining characters: identifier-part characters only
        for (int i = 1; i < chars.Length; i++) {
            char c = chars[i];
            if (SyntaxFacts.IsIdentifierPartCharacter(c))
                result.Append(c);
            else
                result.Append('_');
        }

        return result.ToString();
    }

}