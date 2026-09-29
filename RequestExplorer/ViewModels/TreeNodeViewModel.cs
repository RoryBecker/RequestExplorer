using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RequestExplorer.ViewModels;

public enum TreeNodeKind
{
    Collection,
    Folder,
    Request
}

public abstract partial class TreeNodeViewModel : ObservableObject
{
    protected TreeNodeViewModel(string name)
    {
        _name = name;
        Children = [];
    }

    public abstract TreeNodeKind Kind { get; }

    public TreeNodeViewModel? Parent { get; set; }

    public ObservableCollection<TreeNodeViewModel> Children { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name;

    /// <summary>True when this request (or a descendant) has unsaved edits. Wired fully in Phase 3.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(ShowsDirtyMarker))]
    private bool _isDirty;

    public bool ContainsDirtyDescendant =>
        Children.Any(c => c.IsDirty || c.ContainsDirtyDescendant);

    public bool ShowsDirtyMarker => IsDirty || ContainsDirtyDescendant;

    public string DisplayName => ShowsDirtyMarker ? $"{Name} *" : Name;

    public void NotifyDirtyPresentationChanged()
    {
        OnPropertyChanged(nameof(ContainsDirtyDescendant));
        OnPropertyChanged(nameof(ShowsDirtyMarker));
        OnPropertyChanged(nameof(DisplayName));
        Parent?.NotifyDirtyPresentationChanged();
    }

    partial void OnIsDirtyChanged(bool value) => NotifyDirtyPresentationChanged();

    partial void OnNameChanged(string value) => OnNameUpdated(value);

    protected virtual void OnNameUpdated(string value)
    {
    }

    public void AddChild(TreeNodeViewModel child)
    {
        child.Parent = this;
        Children.Add(child);
        NotifyDirtyPresentationChanged();
    }

    public void InsertChild(TreeNodeViewModel child, int index)
    {
        child.Parent = this;
        if (index < 0 || index > Children.Count)
        {
            index = Children.Count;
        }

        Children.Insert(index, child);
        NotifyDirtyPresentationChanged();
    }

    public bool RemoveChild(TreeNodeViewModel child)
    {
        if (!Children.Remove(child))
        {
            return false;
        }

        child.Parent = null;
        NotifyDirtyPresentationChanged();
        return true;
    }

    public int CountDescendants()
    {
        var count = 0;
        foreach (var child in Children)
        {
            count += 1 + child.CountDescendants();
        }

        return count;
    }
}
