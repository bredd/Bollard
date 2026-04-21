using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;

/// <summary>
/// Minimal template that doesn't do any special encoding. Therefore, it has no HTML class (for HTML.Raw())
/// </summary>
public abstract class RazorTemplate {

    #region Entry Point

    public abstract Task ExecuteAsync();

    #endregion Entry Point

    #region Available to Razor

    // HTML.Raw

    #endregion Available to Razor

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
