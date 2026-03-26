using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bollard;
internal class TestSite : ISite {
    public string Test { get => "From TestSite" ; set { /* do nothing */} }
}
