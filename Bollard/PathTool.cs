using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    public static string GetLocalPath(string path, string basePath) {
        int diLen = basePath.Length;
        Debug.Assert(path.StartsWith(basePath));
        Debug.Assert(basePath[diLen - 1] != Path.DirectorySeparatorChar);
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

}