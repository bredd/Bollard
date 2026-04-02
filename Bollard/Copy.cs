using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;

/// <summary>
/// Represents a file to be copied verbatim from the source tree to the destionation.
/// </summary>
public class Copy {
    string _src;
    string _dst;

    public Copy(string src, string dst) {
        // Use the properties so that the validation is invoked.
        Src = src;  
        Dst = dst;
    }

    /// <summary>
    /// Full native-format path to the source file
    /// </summary>
    public string Src {
        get => _src;
    }

    /// <summary>
    /// Site-relative path to the destination.
    /// </summary>
    public string Dst {
        get => _dst;

        set {
            PathTool.ValidateSiteRelativePath(value);
            _dst = value;
        }
    }

}
