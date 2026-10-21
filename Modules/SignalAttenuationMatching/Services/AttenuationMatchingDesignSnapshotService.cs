using System.Text;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Services;

public sealed class AttenuationMatchingDesignSnapshotService
{
    public string Build(AttenuationMatchingDesignResult result) => new Reporting.AttenuationMatchingJsonReportBuilder().Build(result);

    public void Save(AttenuationMatchingDesignResult result, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A snapshot file path is required.", nameof(fileName));
        File.WriteAllText(fileName, Build(result), new UTF8Encoding(false));
    }
}
