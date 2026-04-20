using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Bollard;
static internal class CompilerHelp {


    public static Location CreateLocation(string localPath, int line = 0, int character = 0) {
        var lp = new LinePosition(line, character);
        return Location.Create(localPath, new TextSpan(), new LinePositionSpan(lp, lp));
    }
}
