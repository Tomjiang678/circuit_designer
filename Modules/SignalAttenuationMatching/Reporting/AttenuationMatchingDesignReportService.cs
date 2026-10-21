using System.Text;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Reporting;

public sealed class AttenuationMatchingDesignReportService
{
    public string ExportHtml(AttenuationMatchingDesignResult result) => new AttenuationMatchingHtmlReportBuilder().Build(result);
    public string ExportJson(AttenuationMatchingDesignResult result) => new AttenuationMatchingJsonReportBuilder().Build(result);

    public void SaveHtml(AttenuationMatchingDesignResult result, string fileName) => Save(result, fileName, html: true);
    public void SaveJson(AttenuationMatchingDesignResult result, string fileName) => Save(result, fileName, html: false);

    private static void Save(AttenuationMatchingDesignResult result, string fileName, bool html)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A report file path is required.", nameof(fileName));
        File.WriteAllText(fileName, html ? new AttenuationMatchingHtmlReportBuilder().Build(result) : new AttenuationMatchingJsonReportBuilder().Build(result), new UTF8Encoding(false));
    }
}
