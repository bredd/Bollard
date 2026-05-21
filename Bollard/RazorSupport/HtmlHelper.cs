using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;

/// <summary>
/// This is the Bollard equivalend of System.Web.WebPages.Html.HtmlHelper which appears
/// as the Html property of the HtmlTemplate base class.
/// </summary>
internal class HtmlHelper {

    private static readonly SearchValues<char> s_encodeHtmlChars = SearchValues.Create("<>&\"'");

    private static string EncodeChar(char c) {
        switch (c) {
        case '<': return "&lt;";
        case '>': return "&gt;";
        case '&': return "&amp;";
        case '"': return"&quot;";
        case '\'': return "&#39;";
        default: return c.ToString();
        }
    }

    public static void EncodeTo(string? value, StringBuilder sb) {
        if (string.IsNullOrEmpty(value))
            return;

        sb.EnsureCapacity(sb.Length + value.Length + value.Length / 10);    // Assume roughly 10% growth due to encoding.

        ReadOnlySpan<char> span = value.AsSpan();
        for (; ;) {
            int hit = span.IndexOfAny(s_encodeHtmlChars);
            if (hit < 0) {
                // No more matches — append the remainder and finish
                sb.Append(span);
                break;
            }

            sb.Append(span.Slice(0, hit));
            sb.Append(EncodeChar(span[hit]));
            span = span.Slice(hit + 1);
        }
    }

    public static void EncodeTo(string? value, TextWriter tw) {
        if (string.IsNullOrEmpty(value))
            return;

        ReadOnlySpan<char> span = value.AsSpan();
        for (; ; ) {
            int hit = span.IndexOfAny(s_encodeHtmlChars);
            if (hit < 0) {
                // No more matches — append the remainder and finish
                tw.Write(span);
                break;
            }

            tw.Write(span.Slice(0, hit));
            tw.Write(EncodeChar(span[hit]));
            span = span.Slice(hit + 1);
        }
    }

}
