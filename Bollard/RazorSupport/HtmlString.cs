using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard.RazorSupport;

/// <summary>
/// Equivalent to <see cref="System.Web.IHtmlString"/>
/// </summary>
public interface IHtmlString {
    public string ToHtmlString();
}

/// <summary>
/// Simple implementation of <see cref="IHtmlString"/>
/// </summary>
public class HtmlString : IHtmlString {

    string _value;

    public HtmlString(string value) {
        _value = value;
    }

    public string ToHtmlString() {
        return _value;
    }

    public override string ToString() {
        return _value;
    }

    public override bool Equals(object? obj) {
        if (obj is string str) {
            return _value == str;           
        }
        return false;
    }

    public override int GetHashCode() {
        return _value.GetHashCode();
    }
}
