using RequestExplorer.Models;

namespace RequestExplorer.Services.Export;

public interface IExportFormatProvider
{
    RequestExportFormat Format { get; }

    string DisplayName { get; }

    string Export(RequestDefinition request);
}
