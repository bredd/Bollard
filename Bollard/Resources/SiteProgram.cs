// This file is included when the Bollard Site Code is compiled.

// Global usings streamline config.cs and other source file
// This will generate a hidden diagnostic of "unnecessary using directive" if nothing in the application references one of these namespaces.
// Since the diagnostic is hidden, we leave it alone.
global using System;
global using System.IO;
global using System.Collections.Generic;

// Elements in this class are available to configuration source file, typically "config.cs"

internal static partial class Program {

    private static Bollard.ISite Site;
    private static string greeting = "(uninitialized)";

    static Program() {
        Site = Bollard.Project.GetDefaultSite();
    }

    public static void Prep(string message) {
        greeting = message;
    }
}
