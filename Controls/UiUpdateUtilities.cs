using System.Diagnostics;

namespace CircuitModuleDesigner.Controls;

internal static class UiControlHost
{
    public static void ReplaceSingleChild(Control host, Control child)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(child);

        host.SuspendLayout();
        try
        {
            foreach (Control existing in host.Controls.Cast<Control>().ToArray())
            {
                host.Controls.Remove(existing);
                existing.Dispose();
            }
            child.Dock = DockStyle.Fill;
            host.Controls.Add(child);
        }
        finally
        {
            host.ResumeLayout(false);
        }
    }
}

internal static class UiPerformanceTrace
{
    [Conditional("DEBUG")]
    public static void Stage(string module, string stage, TimeSpan elapsed) =>
        Debug.WriteLine($"[UI PERF] {module} {stage} {elapsed.TotalMilliseconds:F1} ms");

    [Conditional("DEBUG")]
    public static void Design(string module, TimeSpan buildRequest, TimeSpan coreDesign, TimeSpan render, TimeSpan total)
    {
        Debug.WriteLine($"[UI PERF] {module}");
        Debug.WriteLine($"BuildRequest       {buildRequest.TotalMilliseconds,7:F1} ms");
        Debug.WriteLine($"CoreDesign         {coreDesign.TotalMilliseconds,7:F1} ms (includes synthesis and verification)");
        Debug.WriteLine("Verification           included in CoreDesign; no duplicate UI-thread verification");
        Debug.WriteLine("RenderSummary          included in RenderResult");
        Debug.WriteLine($"RenderResult       {render.TotalMilliseconds,7:F1} ms (summary/schematic/analysis/calculations)");
        Debug.WriteLine("RenderReport          lazy (measured when selected)");
        Debug.WriteLine($"Total              {total.TotalMilliseconds,7:F1} ms");
    }
}
