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

    public static void ValidateSiteRelativePath(string path) {
        if (path[0] != '/')
            throw new InvalidOperationException($"Site-relative paths must start with a forward slash: {path}");
#if DEBUG
        if (path.IndexOfAny(c_siteRelativeProhibitedChars) >= 0)
            throw new InvalidOperationException($"Site-relative paths must use forward slashes and no colons: {path}");
#endif
    }

    public static string GetSiteRelativePath(string path, string basePath) {
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
}