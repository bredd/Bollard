using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Bollard;

// Should have a functional base class and then subclasses with encoding for the following:
//    HTML (including XML and SVG)
//    JSON
//    Others?

public abstract class RazorTemplate {

    #region Available to Razor

    // HTML.Raw

    #endregion

    #region Called by Razor

    protected virtual void WriteLiteral(string? literal = null) {
        throw new NotImplementedException();
    }

    protected virtual void Write(object? obj = null) {
        throw new NotImplementedException();
    }

    protected virtual void BeginWriteAttribute(string name, string prefix, int prefixOffset, string suffix, int suffixOffset, int attributeValuesCount) {
        throw new NotImplementedException();
    }

    protected virtual void WriteAttributeValue(string prefix, int prefixOffset, object value, int valueOffset, int valueLength, bool isLiteral) {
        throw new NotImplementedException();
    }

    protected virtual void EndWriteAttribute() {
        throw new NotImplementedException();
    }

    #endregion Called by Razor
}
