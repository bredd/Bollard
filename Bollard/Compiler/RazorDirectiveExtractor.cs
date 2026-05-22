using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;

namespace Bollard;

internal class RazorDirectiveExtractor {

    // Input
    RazorSourceDocument _source;
    int _offset = 0;
    int _line = 0;
    bool _done = false;

    // Buffer
    char[] _buf = new char[1024];

    // Output
    string _currentName = String.Empty;
    string _currentValue = String.Empty;

    public RazorDirectiveExtractor(RazorSourceDocument source) {
        _source = source;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiWhiteSpace(char c) {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAsciiLetter(char c) {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }

    private bool BufferLine(ref int len) {
        if (_done)
            return false;

        if (_line >= _source.Lines.Count) {
            _done = true;
            return false;
        }

        var lineLen = _source.Lines.GetLineLength(_line++);

        // Grow the buffer if needed (very rare)
        if (len + lineLen > _buf.Length) {
            var newBuf = new char[len + lineLen];
            Array.Copy(_buf, newBuf, len);
            _buf = newBuf;
        }

        _source.CopyTo(_offset, _buf, len, lineLen);
        _offset += lineLen;
        len += lineLen;
        return true;
    }

    private void RemoveRazorComment(ref int rlen, int p) {
        int len = rlen;
        int w = p;
        while (w > 0 && IsAsciiWhiteSpace(_buf[w - 1]))
            --w;
        p += 2;
        for (; ; ) {
            // Refill the buffer if needed
            if (p >= len) {
                p = len = w; // Drop the whitespace plus the rest of the line
                if (!BufferLine(ref len)) {
                    rlen = len;
                    return;
                }
                continue;
            }

            // Check for end-of-comment
            if (p < len-1 && _buf[p] == '*' && _buf[p + 1] == '@')
                break;

            ++p;
        }

        // Skip end of comment
        p += 2;

        // Remove trailing whitespace
        while (p < len && IsAsciiWhiteSpace(_buf[p]))
            ++p;

        // Substitute a single space for the comment
        Debug.Assert(p > w);
        _buf[w++] = ' ';
        Array.Copy(_buf, p, _buf, w, len - p);
        rlen = w + len - p;
    }

    private void RemoveHtmlComment(ref int rlen, int p) {
        int len = rlen;
        int w = p;
        while (w > 0 && IsAsciiWhiteSpace(_buf[w - 1]))
            --w;
        p += 4;
        for (; ; ) {
            // Refill the buffer if needed
            if (p >= len) {
                p = len = w; // Drop the whitespace plus the rest of the line
                if (!BufferLine(ref len)) {
                    rlen = len;
                    return;
                }
                continue;
            }

            // Look for end-of-comment
            if (p < len-2 && _buf[p] == '-' && _buf[p + 1] == '-' && _buf[p+2] == '>')
                break;

            // Non-blank HTML comments end the preamble so any non-whitepace ends the comment and the parsing
            if (!IsAsciiWhiteSpace(_buf[p])) {
                _done = true;
                rlen = w;
                return;
            }

            ++p;
        }

        // Skip end of comment
        p += 3;

        // Remove trailing whitespace
        while (p < len && IsAsciiWhiteSpace(_buf[p]))
            ++p;

        // Substitute a single space for the comment
        Debug.Assert(p > w);
        _buf[w++] = ' ';
        Array.Copy(_buf, p, _buf, w, len - p);
        rlen = w + len - p;
    }

    private bool RemoveComment(ref int rlen) {
        int len = rlen;

        // Look for a comment
        // Buffer boundries are always on a line so we can use appropriate counts to look ahead
        int p = 0;
        while (p < len) {
            char c = _buf[p];

            // If a Razor comment
            if (c == '@' && p < len - 1 && _buf[p + 1] == '*') {
                RemoveRazorComment(ref rlen, p);
                return true;
            }

            // If an HTML comment
            if (c == '<' && p < len - 3 && _buf[p + 1] == '!' && _buf[p+2] == '-' && _buf[p+3] == '-') {
                RemoveHtmlComment(ref rlen, p);
                return true;
            }

            ++p;
        }
        return false;
    }

    public bool ReadNext() {
        // Usually it's just read one line. But sometimes comments can interrupt things.
        // Accordingly, this is optimized to perform well with single-line directives,
        // blank lines, and comment lines.
        // It works with multi-line but is not performance optimized in that case.

        if (_done)
            return false;

        // Loop over blank lines
        int p;
        int len;
        for (; ; ) {
            // Prefill the buffer
            len = 0;
            if (!BufferLine(ref len))
                return false;

            // Keep removing comments until there are none
            while (RemoveComment(ref len)) ;

            // Skip whitespace
            p = 0;
            while (p < len && IsAsciiWhiteSpace(_buf[p])) ++p;

            // Non-blank line
            if (p < len) break;
        }

        // Markup that's not a directive so the preamble is over
        if (_buf[p] != '@') {
            _done = true;
            return false;
        }

        // Directive name must be all letters. Otherwise, end of preamble
        int n = p;
        ++p;
        while (p < len && IsAsciiLetter(_buf[p])) ++p;
        if (p < len && !IsAsciiWhiteSpace(_buf[p])) {
            _done = true;
            return false;
        }
        int ne = p;

        // Skip whitespace after name.
        while (p < len && IsAsciiWhiteSpace(_buf[p])) ++p;

        // Remove trailing whitespace
        while (len > p && IsAsciiWhiteSpace(_buf[len - 1])) --len;

        _currentName = new String(_buf, n, ne - n);
        _currentValue = p < len ? new string(_buf, p, len - p) : string.Empty;
        return true;
    }

    public string CurrentName => _currentName;
    public string CurrentValue => _currentValue;

}
