using System.Text.Json;
using RequestExplorer.Models;
using RequestExplorer.Services.Postman;

namespace RequestExplorer.Services.Export.FormatProviders;

public sealed class PostmanCollectionExportFormatProvider : IExportFormatProvider
{
    public RequestExportFormat Format => RequestExportFormat.PostmanCollection;

    public string DisplayName => "Postman Collection v2.1";

    public string Export(RequestDefinition request)
    {
        var document = PostmanCollectionMapper.ToCollectionDocument(request);
        return JsonSerializer.Serialize(document, PostmanCollectionMapper.JsonOptions);
    }
}
