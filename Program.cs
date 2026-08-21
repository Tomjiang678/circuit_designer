using CircuitModuleDesigner.Forms;
using CircuitModuleDesigner.Core.Circuit.Diagnostics;

namespace CircuitModuleDesigner;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--circuit-core-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            CircuitGraphSmokeTest.Run();
            Console.WriteLine("CircuitGraph smoke test passed.");
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
