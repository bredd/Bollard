// Generate an error
//#ref "HelloWorld.dll"

// Required for Trace.WriteLine
#ref "System.Diagnostics.TraceSource.dll"
using System.Diagnostics;

Console.WriteLine("Config.cs");
Console.WriteLine(Directory.GetCurrentDirectory());
Trace.WriteLine("Sample trace message.");
return 0; // Exit the launch function

// Generate some warnings
int x = 9;
Console.WriteLine("Unreachable");
