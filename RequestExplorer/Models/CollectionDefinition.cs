namespace RequestExplorer.Models;

public sealed class CollectionDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Collection";
    public List<FolderDefinition> Folders { get; set; } = [];
    public List<RequestDefinition> Requests { get; set; } = [];
}
