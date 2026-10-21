using RequestExplorer.ViewModels.Nodes;

namespace RequestExplorer.Services.Variables;

public sealed class DisabledVariableHint
{
    public DisabledVariableHint(string name, string location, TreeNodeViewModel? container)
    {
        Name = name;
        Location = location;
        Container = container;
    }

    public string Name { get; }

    /// <summary>Container name where the disabled variable is defined.</summary>
    public string Location { get; }

    public TreeNodeViewModel? Container { get; }
}
