// Generate an error
//#ref "HelloWorld.dll"

// Required for Trace.WriteLine
#ref "System.Diagnostics.TraceSource.dll"
using System.Diagnostics;

// Required for TestTarget
#ref "C:\Users\brand\Source\bredd\Bollard\Tests\NewArchitecture\_lib\TestTargetLibrary.dll"
using TestTargetLibrary;

Console.WriteLine();
Console.WriteLine("Config.cs");
Console.WriteLine(Directory.GetCurrentDirectory());
Trace.WriteLine("Sample trace message.");
Console.WriteLine($"Greeting = {greeting}");
Console.WriteLine($"Site.Test = {Site.Test}");
Console.WriteLine($"Can you {(TestTarget.AllCaps("Shout"))}?");
Console.WriteLine($"3 * 5 = {Multiply.Times(3, 5)}");
Console.WriteLine("Config.cs test succeeded.");
Console.WriteLine();
return 0; // Exit the launch function

// Generate some warnings
int x = 9;
Console.WriteLine("Unreachable");
