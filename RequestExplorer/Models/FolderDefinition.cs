namespace RequestExplorer.Models;

public sealed class FolderDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Folder";
    public List<FolderDefinition> Folders { get; set; } = [];
    public List<RequestDefinition> Requests { get; set; } = [];
}
