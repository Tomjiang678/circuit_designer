namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;

public interface IFilterReportExporter
{
    string FileExtension { get; }
    string Export(FilterDesignReportModel report);
}
