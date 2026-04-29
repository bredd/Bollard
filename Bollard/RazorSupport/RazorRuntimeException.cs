using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bollard;
internal class RazorRuntimeException : Exception {
    // Nothing here but a specialization of exception
    public RazorRuntimeException(string message, string? sourceName = null)
        : base(string.IsNullOrWhiteSpace(sourceName) ? message : $"({sourceName}) {message}") {
    }
}
