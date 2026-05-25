using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;
public static class SelfTest {
    public static string RunAll(out bool success) {
        var sb = new StringBuilder();
        var wr = new StringWriter(sb);

        success = RazorDirectiveExtractor.SelfTest(wr);
        wr.Flush();
        return sb.ToString();
    }

}
