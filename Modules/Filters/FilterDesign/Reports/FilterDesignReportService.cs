using System.Text;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;

public sealed class FilterDesignReportService
{
    private readonly FilterDesignReportBuilder _builder = new();

    public FilterDesignReportModel Build(FilterDesignResult result) => _builder.Build(result);

    public void SaveHtml(FilterDesignReportModel report, string fileName) =>
        File.WriteAllText(fileName, new HtmlFilterReportExporter().Export(report), new UTF8Encoding(false));

    public void SaveSnapshot(FilterDesignReportModel report, string fileName) =>
        File.WriteAllText(fileName, new JsonFilterDesignSnapshotExporter().Export(report.Snapshot), new UTF8Encoding(false));
}
