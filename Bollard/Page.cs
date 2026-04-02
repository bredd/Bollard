using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;
internal class Page {
    string _src = null!;
    string _dst = null!;

    public Page(string src, string dst) {
        _src = src;
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
