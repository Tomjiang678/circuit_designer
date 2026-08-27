using CircuitModuleDesigner.Forms;

namespace CircuitModuleDesigner;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
