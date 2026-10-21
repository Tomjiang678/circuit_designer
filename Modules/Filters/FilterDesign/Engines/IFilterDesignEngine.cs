using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;

public interface IFilterDesignEngine
{
    FilterDesignResult Design(FilterDesignRequest request);
    FilterDesignAvailability GetAvailability(FilterDesignRequest request);
}
